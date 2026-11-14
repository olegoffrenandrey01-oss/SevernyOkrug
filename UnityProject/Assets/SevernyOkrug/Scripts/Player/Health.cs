// Body-part health (EFT-like): 7 parts, destroyed limbs spill damage, bleeding, armor with durability.
using System;
using UnityEngine;

namespace Severny
{
    public enum BodyPart { Head, Thorax, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }

    public struct DamageInfo
    {
        public BodyPart part;
        public float damage, penetration;
        public Vector3 point, dir;
        public GameObject attacker;
        public bool bullet;
        public string cause;
    }

    public class Health : MonoBehaviour
    {
        public static readonly float[] DefaultMax = { 35f, 85f, 70f, 60f, 60f, 65f, 65f };
        public static readonly string[] PartNames = { "Голова", "Грудь", "Живот", "Л. рука", "П. рука", "Л. нога", "П. нога" };
        public const int Parts = 7;

        public float[] max = (float[])DefaultMax.Clone();
        public float[] hp;
        public bool[] bleeding = new bool[Parts];
        public bool isPlayer;
        public float bleedRate = 0.7f;      // HP/s per bleeding part
        public float painkillerTimer;
        public ItemInstance armor, helmet;   // live references (durability is written back)
        public DamageInfo lastDamage;
        public float lastDamageTime = -99f;

        public bool IsDead { get; private set; }
        /// <summary>LAN proxy (remote player avatar / bot copy on a client): hits are not applied here but forwarded
        /// to whoever owns the real health (the other player / the host).</summary>
        public Action<DamageInfo> Forward;
        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Died;

        void Awake() { if (hp == null || hp.Length != Parts) hp = (float[])max.Clone(); }

        public void ResetFull() { hp = (float[])max.Clone(); bleeding = new bool[Parts]; IsDead = false; painkillerTimer = 0f; }

        public float Total { get { float s = 0; foreach (var v in hp) s += Mathf.Max(0, v); return s; } }
        public float TotalMax { get { float s = 0; foreach (var v in max) s += v; return s; } }
        public bool Destroyed(BodyPart p) => hp[(int)p] <= 0f;
        public bool AnyBleeding { get { foreach (var b in bleeding) if (b) return true; return false; } }
        public bool LegsBroken => Destroyed(BodyPart.LeftLeg) || Destroyed(BodyPart.RightLeg);
        public bool ArmsBroken => Destroyed(BodyPart.LeftArm) || Destroyed(BodyPart.RightArm);
        public bool PainSuppressed => painkillerTimer > 0f;
        public float SpeedMultiplier => LegsBroken && !PainSuppressed ? 0.72f : 1f;
        public bool CanSprint => !(LegsBroken && !PainSuppressed);
        public float AimSwayMultiplier => ArmsBroken && !PainSuppressed ? 2.2f : 1f;

        /// <summary>True when the last bullet was stopped by armor / helmet (for hit sounds).</summary>
        public bool LastHitArmored { get; private set; }

        public void ApplyDamage(DamageInfo di)
        {
            LastHitArmored = false;
            if (IsDead) return;
            if (Forward != null) { Forward(di); return; }
            if (hp == null || hp.Length != Parts) hp = (float[])max.Clone();
            float dmg = di.damage;
            ItemInstance arm = di.part == BodyPart.Head ? helmet : (di.part == BodyPart.Thorax || di.part == BodyPart.Stomach) ? armor : null;
            if (di.bullet && !ItemInstance.IsEmpty(arm) && arm.dur > 0f)
            {
                var ad = arm.Def;
                float cls = ad.armorClass * 10f, pen = di.penetration;
                float chance = pen >= cls ? Mathf.Lerp(0.55f, 0.95f, Mathf.Clamp01((pen - cls) / 15f))
                                          : Mathf.Lerp(0.02f, 0.45f, Mathf.Clamp01((pen - (cls - 15f)) / 15f));
                float durFrac = Mathf.Clamp01(arm.dur / Mathf.Max(1f, ad.durability));
                chance = Mathf.Lerp(0.95f, chance, durFrac);
                bool penetrated = UnityEngine.Random.value < chance;
                arm.dur = Mathf.Max(0f, arm.dur - (2.0f + pen * 0.07f) * (penetrated ? 0.8f : 1f));
                dmg *= penetrated ? 0.85f : 0.1f;
                LastHitArmored = !penetrated;
                if (isPlayer) SFX.Play2D(SFX.Pick(SFX.ArmorHits, SFX.Thud), penetrated ? 0.55f : 0.8f);
            }
            lastDamage = di; lastDamageTime = Time.time;
            int i = (int)di.part;
            if (hp[i] <= 0f)
            {
                if (di.part == BodyPart.Head || di.part == BodyPart.Thorax) { Kill(di); return; }
                Spill(dmg * 0.7f, i, di);
            }
            else
            {
                hp[i] -= dmg;
                if (hp[i] <= 0f)
                {
                    float over = -hp[i]; hp[i] = 0f;
                    if (di.part == BodyPart.Head || di.part == BodyPart.Thorax) { Kill(di); return; }
                    Spill(over * 0.5f, i, di);
                }
            }
            if (IsDead) return;
            if (di.bullet && dmg > 10f && di.part != BodyPart.Head && UnityEngine.Random.value < 0.33f) bleeding[i] = true;
            if (di.cause == "fall" && UnityEngine.Random.value < 0.3f) bleeding[i] = true;
            Damaged?.Invoke(di);
        }

        void Spill(float amount, int except, DamageInfo di)
        {
            int alive = 0;
            for (int j = 0; j < Parts; j++) if (j != except && hp[j] > 0f) alive++;
            if (alive == 0) { Kill(di); return; }
            float each = amount / alive;
            for (int j = 0; j < Parts; j++)
            {
                if (j == except || hp[j] <= 0f) continue;
                hp[j] -= each;
                if (hp[j] <= 0f)
                {
                    hp[j] = 0f;
                    if (j == (int)BodyPart.Head || j == (int)BodyPart.Thorax) { Kill(di); return; }
                }
            }
        }

        void Update()
        {
            if (IsDead) return;
            if (painkillerTimer > 0f) painkillerTimer -= Time.deltaTime;
            for (int i = 0; i < Parts; i++)
            {
                if (!bleeding[i]) continue;
                float d = bleedRate * Time.deltaTime;
                if (hp[i] > 0f) hp[i] = Mathf.Max(0f, hp[i] - d);
                else Spill(d, i, new DamageInfo { part = (BodyPart)i, damage = d, cause = "bleed" });
                if (IsDead) return;
                if (hp[i] <= 0f && (i == (int)BodyPart.Head || i == (int)BodyPart.Thorax)) { Kill(new DamageInfo { part = (BodyPart)i, cause = "bleed" }); return; }
            }
        }

        /// <summary>Heals damaged, non-destroyed parts (most damaged first). Returns HP used.</summary>
        public float HealAmount(float amount)
        {
            float used = 0f;
            while (amount > 0.01f)
            {
                int best = -1; float bestFrac = 1f;
                for (int i = 0; i < Parts; i++)
                {
                    if (hp[i] <= 0f || hp[i] >= max[i]) continue;
                    float f = hp[i] / max[i];
                    if (f < bestFrac) { bestFrac = f; best = i; }
                }
                if (best < 0) break;
                float need = max[best] - hp[best], d = Mathf.Min(need, amount);
                hp[best] += d; amount -= d; used += d;
            }
            return used;
        }

        public bool StopOneBleed()
        {
            int[] order = { 1, 2, 0, 3, 4, 5, 6 };
            foreach (var i in order) if (bleeding[i]) { bleeding[i] = false; return true; }
            return false;
        }

        public bool RestoreDestroyedPart()
        {
            for (int i = 2; i < Parts; i++) if (hp[i] <= 0f) { hp[i] = 1f; bleeding[i] = false; return true; }
            return false;
        }

        public bool NeedsHealing { get { for (int i = 0; i < Parts; i++) if (hp[i] > 0f && hp[i] < max[i]) return true; return false; } }
        public bool HasDestroyed { get { for (int i = 2; i < Parts; i++) if (hp[i] <= 0f) return true; return false; } }

        public void Kill(DamageInfo di)
        {
            if (IsDead) return;
            IsDead = true;
            lastDamage = di;
            Died?.Invoke(di);
        }
    }
}
