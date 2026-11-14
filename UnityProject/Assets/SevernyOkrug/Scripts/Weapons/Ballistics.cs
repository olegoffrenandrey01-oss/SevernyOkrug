// Simulated projectiles: gravity + drag, per-frame segment raycasts, body-part damage, impact decals,
// supersonic crack near the player.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public class Ballistics : MonoBehaviour
    {
        sealed class ShotRecord { public bool hit; }
        struct Proj
        {
            public Vector3 pos, vel;
            public float life, startSpeed, dmgMult, penetration;
            public int surfaces;
            public ItemDef ammo;
            public ShotRecord shot;
            public Transform shooter;
            public bool fromPlayer, cracked, cosmetic;
        }

        static Ballistics _i;
        readonly List<Proj> _list = new List<Proj>(256);
        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly HitDistComparer _cmp = new HitDistComparer();
        public static Transform Listener; // player camera
        public static int ShotsFired, Hits;

        class HitDistComparer : IComparer<RaycastHit> { public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance); }

        public static Ballistics I
        {
            get
            {
                if (_i == null)
                {
                    var g = new GameObject("SEV_Ballistics");
                    _i = g.AddComponent<Ballistics>();
                }
                return _i;
            }
        }

        public static void Fire(Vector3 origin, Vector3 dir, ItemDef ammo, Transform shooter, bool fromPlayer, float dmgMult = 1f)
        {
            if (ammo == null) return;
            if (fromPlayer) ShotsFired++;
            Emit(origin, dir, ammo, shooter, fromPlayer, dmgMult, false);
        }

        static void Emit(Vector3 origin, Vector3 dir, ItemDef ammo, Transform shooter, bool fromPlayer, float damage, bool cosmetic)
        {
            int pellets = Mathf.Clamp(ammo.pellets, 1, 16);
            var shot = fromPlayer ? new ShotRecord() : null;
            var forward = dir.normalized;
            var right = Vector3.Cross(Mathf.Abs(forward.y) > 0.99f ? Vector3.right : Vector3.up, forward).normalized;
            var up = Vector3.Cross(forward, right);
            for (int i = 0; i < pellets; i++)
            {
                // Fixed disk pattern: LAN peers replay the same pellet directions from the existing shot message.
                float angle = i * 2.399963f;
                float radius = pellets == 1 ? 0f : Mathf.Sqrt((i + 0.5f) / pellets) * Mathf.Tan(ammo.pelletSpread * Mathf.Deg2Rad);
                var d = (forward + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius).normalized;
                I._list.Add(new Proj { pos = origin, vel = d * ammo.speed, startSpeed = ammo.speed, ammo = ammo,
                    shooter = shooter, fromPlayer = fromPlayer, shot = shot, dmgMult = damage, penetration = ammo.penetration, cosmetic = cosmetic });
            }
        }

        /// <summary>LAN replay: identical pellet pattern, impacts only, no damage.</summary>
        public static void FireCosmetic(Vector3 origin, Vector3 dir, ItemDef ammo, Transform shooter)
        {
            if (ammo != null) Emit(origin, dir, ammo, shooter, false, 0f, true);
        }

        public static void ClearAll() { if (_i != null) _i._list.Clear(); ImpactFX.Clear(); }
        public static int InFlight => _i != null ? _i._list.Count : 0;

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 g = Physics.gravity;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var p = _list[i];
                Vector3 next = p.pos + p.vel * dt + 0.5f * dt * dt * g;
                Vector3 seg = next - p.pos;
                float len = seg.magnitude;
                bool done = false;
                if (len > 1e-4f)
                {
                    Vector3 dir = seg / len;
                    int n = Physics.RaycastNonAlloc(p.pos, dir, _hits, len, ~0, QueryTriggerInteraction.Ignore);
                    // NonAlloc truncation does not guarantee the nearest hits. Dense fences/props need a complete result.
                    var hits = _hits;
                    if (n == _hits.Length) { hits = Physics.RaycastAll(p.pos, dir, len, ~0, QueryTriggerInteraction.Ignore); n = hits.Length; }
                    if (n > 1) System.Array.Sort(hits, 0, n, _cmp);
                    for (int k = 0; k < n; k++)
                    {
                        var h = hits[k];
                        if (p.shooter != null && h.collider.transform.IsChildOf(p.shooter)) continue;
                        ProcessHit(ref p, h, dir);
                        var surface = h.collider.GetComponent<BulletSurface>();
                        bool body = h.collider.GetComponentInParent<Hitbox>() != null;
                        float budget = p.penetration * Mathf.Clamp01(p.vel.magnitude / Mathf.Max(1f, p.startSpeed));
                        if (!body && p.surfaces < 4 && surface != null && surface.TryCross(h.collider, h, dir, budget, out float cost))
                        {
                            p.penetration = Mathf.Max(0f, p.penetration - cost);
                            p.dmgMult *= Mathf.Clamp(surface.damageRetention, 0.05f, 1f) * Mathf.Clamp01(1f - cost / Mathf.Max(1f, budget) * 0.5f);
                            p.surfaces++;
                            continue; // Keep checking this segment: a target may be inside a hollow crate.
                        }
                        done = true;
                        break;
                    }
                    if (!done && !p.fromPlayer && !p.cracked && Listener != null)
                    {
                        Vector3 lp = Listener.position;
                        float t = Mathf.Clamp(Vector3.Dot(lp - p.pos, dir), 0f, len);
                        Vector3 c = p.pos + dir * t;
                        if ((c - lp).sqrMagnitude < 9f)
                        {
                            p.cracked = true;
                            if (p.ammo.speed > 340f) SFX.PlayAt(SFX.Pick(SFX.Flybys, SFX.Crack), c, 0.9f, 1f, 30f, 0.1f);
                            else SFX.PlayAt(SFX.Pick(SFX.Flybys, SFX.Rustle), c, 0.35f, 1f, 12f, 0.2f);
                        }
                    }
                }
                p.vel += g * dt;
                float sp = p.vel.magnitude;
                p.vel *= 1f / (1f + p.ammo.drag * sp * dt);
                p.pos = next;
                p.life += dt;
                if (done || p.life > 4f || p.pos.y < -1000f || sp < 60f) { _list.RemoveAt(i); continue; }
                _list[i] = p;
            }
        }

        void ProcessHit(ref Proj p, RaycastHit h, Vector3 dir)
        {
            var hb = h.collider.GetComponent<Hitbox>();
            if (hb == null) hb = h.collider.GetComponentInParent<Hitbox>();
            float speedFrac = Mathf.Clamp01(p.vel.magnitude / Mathf.Max(1f, p.startSpeed));
            if (p.cosmetic)
            {
                bool flesh = hb != null && hb.owner != null && !hb.owner.IsDead;
                if (flesh && !hb.owner.isPlayer) SFX.PlayAt(SFX.Pick(SFX.FleshHits, SFX.Hit), h.point, 0.8f, 2f, 60f, 0.1f);
                else if (!flesh) SFX.PlayAt(SFX.ImpactFor(h.collider), h.point, 0.6f, 1.5f, 70f, 0.12f);
                ImpactFX.Spawn(h.point, h.normal, flesh);
                return;
            }
            if (hb != null && hb.owner != null && !hb.owner.IsDead)
            {
                var part = hb.Resolve(h.point);
                float dmg = p.ammo.damage * p.dmgMult * Mathf.Lerp(0.55f, 1f, speedFrac);
                hb.owner.ApplyDamage(new DamageInfo
                {
                    part = part, damage = dmg, penetration = p.penetration * Mathf.Lerp(0.6f, 1f, speedFrac),
                    point = h.point, dir = dir, attacker = p.shooter != null ? p.shooter.gameObject : null, bullet = true, cause = "bullet"
                });
                if (p.fromPlayer && p.shot != null && !p.shot.hit) { p.shot.hit = true; Hits++; }
                // the player's own hits are voiced in PlayerController (2D); others as 3D sounds at the hit point
                if (!hb.owner.isPlayer)
                    SFX.PlayAt(hb.owner.LastHitArmored ? SFX.Pick(SFX.ArmorHits, SFX.Thud) : SFX.Pick(SFX.FleshHits, SFX.Hit), h.point, 0.8f, 2f, 60f, 0.1f);
                ImpactFX.Spawn(h.point, h.normal, true);
            }
            else
            {
                SFX.PlayAt(SFX.ImpactFor(h.collider), h.point, 0.6f, 1.5f, 70f, 0.12f);
                ImpactFX.Spawn(h.point, h.normal, false);
                var rb = h.rigidbody;
                if (rb != null && !rb.isKinematic) rb.AddForceAtPosition(dir * 2f, h.point, ForceMode.Impulse);
            }
            if (p.fromPlayer) NoiseSystem.Emit(h.point, 14f, p.shooter != null ? p.shooter.gameObject : null, false);
        }
    }

    /// <summary>Pooled bullet holes + dust/blood puffs.</summary>
    public static class ImpactFX
    {
        const int MaxDecals = 160, MaxPuffs = 24;
        static readonly Queue<GameObject> _decals = new Queue<GameObject>();
        static GameObject[] _puffs;
        static float[] _puffT;
        static int _puffNext;
        static GameObject _root;
        static MaterialPropertyBlock _mpb;

        static void Ensure()
        {
            if (_root != null) return;
            _root = new GameObject("SEV_ImpactFX");
            _decals.Clear();
            _puffs = new GameObject[MaxPuffs];
            _puffT = new float[MaxPuffs];
            _mpb = new MaterialPropertyBlock();
        }

        public static void Clear()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _decals.Clear();
        }

        public static void Spawn(Vector3 p, Vector3 n, bool blood)
        {
            Ensure();
            if (!blood)
            {
                GameObject d;
                if (_decals.Count >= MaxDecals) d = _decals.Dequeue();
                else
                {
                    d = Prim.Make(PrimitiveType.Quad, _root.transform, Vector3.zero, Vector3.one, GameAssets.Unlit(new Color(0.05f, 0.05f, 0.05f)), false, null, "hole");
                    d.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                d.transform.position = p + n * 0.006f;
                d.transform.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                d.transform.localScale = Vector3.one * Random.Range(0.035f, 0.06f);
                d.SetActive(true);
                _decals.Enqueue(d);
            }
            int i = _puffNext; _puffNext = (_puffNext + 1) % MaxPuffs;
            if (_puffs[i] == null)
            {
                _puffs[i] = Prim.Make(PrimitiveType.Sphere, _root.transform, Vector3.zero, Vector3.one, GameAssets.Unlit(new Color(0.5f, 0.48f, 0.44f, 0.5f), true), false, null, "puff");
                _puffs[i].GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var g = _puffs[i];
            g.transform.position = p + n * 0.05f;
            g.transform.localScale = Vector3.one * 0.05f;
            var r = g.GetComponent<MeshRenderer>();
            Color c = blood ? new Color(0.35f, 0.02f, 0.02f, 0.7f) : new Color(0.55f, 0.53f, 0.5f, 0.55f);
            _mpb.SetColor("_BaseColor", c); _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
            g.SetActive(true);
            _puffT[i] = Time.time;
            if (_root.GetComponent<ImpactFXUpdater>() == null) _root.AddComponent<ImpactFXUpdater>();
        }

        public static void Tick()
        {
            if (_puffs == null) return;
            for (int i = 0; i < _puffs.Length; i++)
            {
                var g = _puffs[i];
                if (g == null || !g.activeSelf) continue;
                float t = (Time.time - _puffT[i]) / 0.45f;
                if (t >= 1f) { g.SetActive(false); continue; }
                g.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.45f, Mathf.Sqrt(t));
            }
        }
    }
}
