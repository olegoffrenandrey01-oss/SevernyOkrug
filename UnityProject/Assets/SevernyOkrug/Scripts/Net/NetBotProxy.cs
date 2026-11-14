// LAN co-op, client side: a copy of a host's bot. Same look (body seed), moves by the host's 10 Hz snapshots,
// replays its shots (flash, sound, cosmetic bullets — real damage comes from the host), forwards our hits to the host
// and becomes a lootable corpse when the host reports its death.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public class NetBotProxy : MonoBehaviour
    {
        public static readonly Dictionary<int, NetBotProxy> All = new Dictionary<int, NetBotProxy>();

        public int nid;
        public Health health;
        public ItemDef wdef, ammo;
        BotBody _bb;
        WeaponView _view;
        Transform _aim, _eye;
        Vector3 _target, _vel;
        Quaternion _aimRot = Quaternion.identity;
        float _yaw, _last = -1f, _flashOff, _deadT;
        bool _combat, _dead;
        Quaternion _deadFrom;

        public bool IsDead => _dead;
        public Transform TorchMount => _aim != null ? _aim : transform;

        public static NetBotProxy Create(int nid, Vector3 pos, float yaw, int bodySeed, string weaponId, string ammoId, string armorId, string helmetId, Transform parent)
        {
            var g = new GameObject("Мародёр");
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var p = g.AddComponent<NetBotProxy>();
            p.nid = nid; p._target = pos; p._yaw = yaw;
            p.health = g.AddComponent<Health>();
            p.health.Forward = di => NetSession.I?.SendHitBot(nid, di);      // our hits go to the host's real bot
            if (!string.IsNullOrEmpty(armorId)) p.health.armor = ItemInstance.Create(armorId);
            if (!string.IsNullOrEmpty(helmetId)) p.health.helmet = ItemInstance.Create(helmetId);
            p.wdef = ItemDB.Get(weaponId); if (p.wdef == null || p.wdef.type != ItemType.Weapon) p.wdef = ItemDB.Get("p9m");
            p.ammo = ItemDB.Get(ammoId); if (p.ammo == null) p.ammo = ItemDB.Get(ItemDB.DefaultAmmo(p.wdef.caliber));
            p._bb = BotBody.Create(g.transform, p.health, p.wdef, new System.Random(bodySeed));
            if (p._bb != null) { p._aim = p._bb.aim; p._eye = p._bb.eye; p._view = p._bb.view; }
            else p.BuildFallback();
            p._aimRot = p._aim != null ? p._aim.rotation : g.transform.rotation;
            All[nid] = p;
            return p;
        }

        void BuildFallback()
        {
            var mat = GameAssets.Lit(new Color(0.27f, 0.29f, 0.22f), 0.08f);
            Prim.Make(PrimitiveType.Capsule, transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 0.9f, 0.6f), mat, false, null, "Capsule");
            var hbGo = new GameObject("Hitbox"); hbGo.transform.SetParent(transform, false);
            var cap = hbGo.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0f, 0.9f, 0f); cap.height = 1.8f; cap.radius = 0.32f;
            var hb = hbGo.AddComponent<Hitbox>(); hb.owner = health; hb.heightBased = true; hb.height = 1.8f;
            _aim = new GameObject("Aim").transform; _aim.SetParent(transform, false); _aim.localPosition = new Vector3(0.15f, 1.42f, 0.2f);
            _eye = new GameObject("Eye").transform; _eye.SetParent(transform, false); _eye.localPosition = new Vector3(0f, 1.66f, 0.1f);
            _view = WeaponModels.Build(wdef, _aim, false, false);
        }

        void OnDestroy() { if (All.TryGetValue(nid, out var p) && p == this) All.Remove(nid); }

        public void ApplyState(Vector3 pos, float yaw, Quaternion aim, Vector3 vel, bool combat)
        {
            if (_dead) return;
            if ((pos - transform.position).sqrMagnitude > 64f) transform.position = pos;   // first update / teleport
            _target = pos; _yaw = yaw; _aimRot = aim; _vel = vel; _combat = combat; _last = Time.time;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_dead) { DeadAnim(dt); return; }
            Vector3 prev = transform.position;
            Vector3 goal = _target + _vel * Mathf.Clamp(Time.time - _last, 0f, 0.2f);
            transform.position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-10f * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, _yaw, 0f), 1f - Mathf.Exp(-12f * dt));
            Vector3 v = (transform.position - prev) / dt; v.y = 0f;
            if (_bb != null) { _bb.velocity = v; _bb.combat = _combat; }
            if (_aim != null) _aim.rotation = Quaternion.Slerp(_aim.rotation, _aimRot, 1f - Mathf.Exp(-14f * dt));
            if (_flashOff > 0f && Time.time >= _flashOff) { _flashOff = 0f; if (_view != null) { if (_view.flash != null) _view.flash.SetActive(false); if (_view.light != null) _view.light.enabled = false; } }
        }

        public void Shot(Vector3 from, Vector3 dir)
        {
            if (_dead) return;
            Vector3 muzzle = _view != null && _view.muzzle != null ? _view.muzzle.position : from;
            float camDist = Ballistics.Listener != null ? Vector3.Distance(Ballistics.Listener.position, muzzle) : 50f;
            SFX.PlayAt(camDist < 70f ? SFX.ShotFor(wdef) : SFX.DistantFor(wdef), muzzle, 1f, 10f, 700f, 0.06f);
            if (_view != null) { if (_view.flash != null) _view.flash.SetActive(true); if (_view.light != null) _view.light.enabled = true; _flashOff = Time.time + 0.05f; }
            Ballistics.FireCosmetic(from, dir, ammo, transform);
        }

        public void Die(Vector3 point, Vector3 dir, List<ItemInstance> loot)
        {
            if (_dead) return;
            _dead = true;
            var d = new DamageInfo { point = point, dir = dir, bullet = true };
            health.Forward = null;
            health.Kill(d);
            if (_bb != null) _bb.Die(d);
            else foreach (var hb in GetComponentsInChildren<Hitbox>()) { var c = hb.GetComponent<Collider>(); if (c != null) c.enabled = false; }
            _deadFrom = transform.rotation;
            SFX.PlayAt(SFX.Thud, transform.position + Vector3.up, 0.8f, 2f, 40f);
            if (SFX.Death != null) SFX.PlayAt(SFX.Death, transform.position + Vector3.up * 1.5f, 0.9f, 3f, 60f, 0.1f);
            var lc = gameObject.AddComponent<LootContainer>();
            lc.isCorpse = true; lc.title = "Мародёр"; lc.prefilled = true;
            lc.nid = NetWorld.BotCorpseNid(nid);
            lc.grid = NetWorld.GridFor(loot, "Тело", 6, 6);
            if (_bb == null)
            {
                var col = gameObject.AddComponent<BoxCollider>();
                col.isTrigger = true; col.center = new Vector3(0, 0.3f, -0.9f); col.size = new Vector3(0.8f, 0.6f, 1.9f);
            }
        }

        void DeadAnim(float dt)
        {
            if (_bb != null || _deadT >= 1f) return;
            _deadT = Mathf.Min(1f, _deadT + dt * 2.2f);
            transform.rotation = _deadFrom * Quaternion.Euler(-88f * _deadT * _deadT, 0, 0);
        }
    }
}
