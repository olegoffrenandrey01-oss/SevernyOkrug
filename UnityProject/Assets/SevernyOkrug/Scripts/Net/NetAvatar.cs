// LAN co-op: another player's character in this world. Uses the bot character models (BotBody) with the player's
// weapon, follows the state messages (20 Hz) smoothly, plays their shots / footsteps / flashlight, ragdolls on death.
// On the host it is also a bot target (IBotTarget): bots see, hear and shoot it; hits are forwarded to its owner.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public class NetAvatar : MonoBehaviour, IBotTarget
    {
        public static readonly Dictionary<int, NetAvatar> All = new Dictionary<int, NetAvatar>();

        public const byte FCrouch = 1, FSprint = 2, FTorch = 4, FDead = 8, FAim = 16, FArmor = 32, FHelmet = 64;

        public int pid;
        public string nick = "Игрок";
        public Health health;
        Transform _body, _head;
        BotBody _bb;
        WeaponView _fallbackView;
        ItemDef _wdef;
        string _weaponId = "?";
        bool _armor, _helmet, _dead, _haveState;
        Vector3 _target, _vel;
        float _yaw, _pitch, _lastState = -1f, _lastShot = -99f, _stepDist, _flashOff;
        byte _flags;
        Light _torch;

        public bool Crouched => (_flags & FCrouch) != 0;
        public bool IsSprinting => (_flags & FSprint) != 0;
        public bool TorchOn => (_flags & FTorch) != 0 && !_dead;
        public bool Aiming => (_flags & FAim) != 0;
        public float SpeedNow { get; private set; }
        public bool Dead => _dead;
        public Transform Root => transform;
        public GameObject Go => gameObject;
        public Vector3 Head => _head != null ? _head.position : transform.position + Vector3.up * (Crouched ? 1.15f : 1.62f);
        public float Height => Crouched ? 1.3f : 1.8f;
        WeaponView View => _bb != null ? _bb.view : _fallbackView;

        public static NetAvatar Create(int pid, string nick, Transform parent, Vector3 pos, float yaw)
        {
            var g = new GameObject("Игрок " + nick);
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var a = g.AddComponent<NetAvatar>();
            a.pid = pid; a.nick = nick;
            a._target = pos; a._yaw = yaw;
            a.health = g.AddComponent<Health>();
            All[pid] = a;
            return a;
        }

        void OnEnable() { if (NetSession.IsHost) BotTargets.Add(this); }
        void OnDisable() { BotTargets.Remove(this); }
        void OnDestroy() { if (All.TryGetValue(pid, out var a) && a == this) All.Remove(pid); }

        public void ApplyState(Vector3 pos, float yaw, float pitch, byte flags, string weaponId)
        {
            if (_dead) return;
            float now = Time.time;
            if (_haveState)
            {
                float dt = Mathf.Clamp(now - _lastState, 0.02f, 0.5f);
                Vector3 v = (pos - _target) / dt;
                _vel = v.sqrMagnitude > 100f ? Vector3.zero : Vector3.Lerp(_vel, v, 0.6f);   // teleports are not velocity
            }
            else transform.position = pos;
            _haveState = true;
            _target = pos; _yaw = yaw; _pitch = pitch; _flags = flags; _lastState = now;
            bool armor = (flags & FArmor) != 0, helmet = (flags & FHelmet) != 0;
            if (weaponId != _weaponId || armor != _armor || helmet != _helmet || (_bb == null && _body == null)) Rebuild(weaponId, armor, helmet);
            if ((flags & FDead) != 0) Die(new DamageInfo { point = transform.position + Vector3.up, dir = transform.forward });
        }

        void Rebuild(string weaponId, bool armor, bool helmet)
        {
            _weaponId = weaponId; _armor = armor; _helmet = helmet;
            if (_body != null) { _body.gameObject.SetActive(false); Destroy(_body.gameObject); }
            _bb = null; _fallbackView = null; _torch = null;
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            health.armor = armor ? ItemInstance.Create("vest3") : null;
            health.helmet = helmet ? ItemInstance.Create("helm2") : null;
            _wdef = ItemDB.Get(weaponId);
            if (_wdef == null || _wdef.type != ItemType.Weapon) _wdef = ItemDB.Get("p9m");
            _bb = BotBody.Create(_body, health, _wdef, new System.Random(pid * 7919 + 17));
            if (_bb != null) _head = _bb.eye;
            else BuildFallback();
        }

        /// <summary>No character models in the project: a capsule with a weapon.</summary>
        void BuildFallback()
        {
            var mat = GameAssets.Lit(new Color(0.25f, 0.3f, 0.22f), 0.1f);
            var vis = Prim.Make(PrimitiveType.Capsule, _body, new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 0.9f, 0.6f), mat, false, null, "Capsule");
            var hbGo = new GameObject("Hitbox"); hbGo.transform.SetParent(_body, false);
            var cap = hbGo.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0f, 0.9f, 0f); cap.height = 1.8f; cap.radius = 0.32f;
            var hb = hbGo.AddComponent<Hitbox>(); hb.owner = health; hb.heightBased = true; hb.height = 1.8f;
            _head = new GameObject("Head").transform; _head.SetParent(_body, false); _head.localPosition = new Vector3(0f, 1.62f, 0.1f);
            var aim = new GameObject("Aim").transform; aim.SetParent(_body, false); aim.localPosition = new Vector3(0.18f, 1.4f, 0.25f);
            _fallbackView = WeaponModels.Build(_wdef, aim, false, false);
            if (vis == null) return;
        }

        Transform AimT => _bb != null ? _bb.aim : _fallbackView != null && _fallbackView.root != null ? _fallbackView.root.transform.parent : transform;

        void Update()
        {
            float dt = Time.deltaTime;
            if (_dead || dt <= 0f) return;
            Vector3 prev = transform.position;
            Vector3 goal = _target + _vel * Mathf.Clamp(Time.time - _lastState, 0f, 0.12f);
            if ((transform.position - goal).sqrMagnitude > 36f) transform.position = goal;
            else transform.position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-14f * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, _yaw, 0f), 1f - Mathf.Exp(-16f * dt));
            Vector3 v = (transform.position - prev) / dt; v.y = 0f;
            SpeedNow = Mathf.Lerp(SpeedNow, v.magnitude, 1f - Mathf.Exp(-10f * dt));
            bool combat = Aiming || Time.time - _lastShot < 2.5f;
            if (_bb != null)
            {
                _bb.velocity = v; _bb.combat = combat;
                Quaternion want = combat ? Quaternion.Euler(_pitch, _yaw, 0f) : transform.rotation * Quaternion.Euler(28f, -25f, 0f);
                _bb.aim.rotation = Quaternion.Slerp(_bb.aim.rotation, want, 1f - Mathf.Exp(-12f * dt));
            }
            else if (AimT != null) AimT.rotation = Quaternion.Euler(combat ? _pitch : 25f, _yaw, 0f);
            // flashlight
            bool torch = TorchOn;
            if (torch && _torch == null && AimT != null)
            {
                var lg = new GameObject("Torch"); lg.transform.SetParent(AimT, false); lg.transform.localPosition = new Vector3(0.1f, -0.03f, 0.35f);
                _torch = lg.AddComponent<Light>();
                _torch.type = LightType.Spot; _torch.range = 36f; _torch.spotAngle = 60f; _torch.innerSpotAngle = 24f;
                _torch.intensity = 9f; _torch.color = new Color(1f, 0.95f, 0.85f); _torch.shadows = LightShadows.None;
            }
            if (_torch != null && _torch.enabled != torch) _torch.enabled = torch;
            if (torch && _torch != null && _bb == null) _torch.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            // footsteps (+ noise for the host's bots)
            if (SpeedNow > 0.5f)
            {
                _stepDist += SpeedNow * dt;
                float stride = IsSprinting ? 1.9f : Crouched ? 0.85f : 1.45f;
                if (_stepDist >= stride)
                {
                    _stepDist = 0f;
                    var steps = SFX.CurSteps;
                    if (steps != null && steps.Length > 0)
                        SFX.PlayAt(steps[Random.Range(0, steps.Length)], transform.position, IsSprinting ? 0.6f : Crouched ? 0.15f : 0.35f, 1.5f, IsSprinting ? 40f : 22f, 0.08f);
                    if (NetSession.IsHost) NoiseSystem.Emit(transform.position, IsSprinting ? 30f : Crouched ? 4f : 14f, gameObject);
                }
            }
            if (_flashOff > 0f && Time.time >= _flashOff) { _flashOff = 0f; var vw = View; if (vw != null) { if (vw.flash != null) vw.flash.SetActive(false); if (vw.light != null) vw.light.enabled = false; } }
        }

        /// <summary>A shot fired by this player (cosmetic replay: flash, sound, tracer-less projectile for impacts).</summary>
        public void Shot(Vector3 origin, Vector3 dir, ItemDef ammo, ItemDef weapon)
        {
            if (_dead) return;
            _lastShot = Time.time;
            var w = weapon ?? _wdef;
            var vw = View;
            Vector3 muzzle = vw != null && vw.muzzle != null ? vw.muzzle.position : origin;
            float camDist = Ballistics.Listener != null ? Vector3.Distance(Ballistics.Listener.position, muzzle) : 50f;
            SFX.PlayAt(camDist < 70f ? SFX.ShotFor(w) : SFX.DistantFor(w), muzzle, 1f, 10f, 700f, 0.06f);
            if (vw != null) { if (vw.flash != null) vw.flash.SetActive(true); if (vw.light != null) vw.light.enabled = true; _flashOff = Time.time + 0.05f; }
            Ballistics.FireCosmetic(origin, dir, ammo, transform);
            if (NetSession.IsHost) NoiseSystem.Emit(transform.position, w != null && w.isPistol ? 110f : 170f, gameObject, true);
        }

        public void Die(DamageInfo d)
        {
            if (_dead) return;
            _dead = true;
            BotTargets.Remove(this);
            if (_torch != null) _torch.enabled = false;
            if (_bb != null) { _bb.velocity = _vel; _bb.Die(d); }
            else if (_body != null) _body.localRotation = Quaternion.Euler(-85f, 0f, 0f);
            if (!health.IsDead) { var f = health.Forward; health.Forward = null; health.Kill(d); health.Forward = f; }
            SFX.PlayAt(SFX.Thud, transform.position + Vector3.up, 0.8f, 2f, 40f);
        }

        /// <summary>Loot of a killed player: a searchable corpse (or a bag if the body is gone).</summary>
        public LootContainer MakeCorpse(List<ItemInstance> items, int cnid)
        {
            if (!_dead) Die(new DamageInfo());
            var lc = gameObject.GetComponent<LootContainer>();
            if (lc == null) lc = gameObject.AddComponent<LootContainer>();
            lc.isCorpse = true; lc.title = nick; lc.nid = cnid;
            lc.grid = NetWorld.GridFor(items, "Тело: " + nick);
            lc.prefilled = true;
            if (_bb == null)
            {
                var col = gameObject.AddComponent<BoxCollider>();
                col.isTrigger = true; col.center = new Vector3(0f, 0.3f, 0f); col.size = new Vector3(1.2f, 0.6f, 1.9f);
            }
            return lc;
        }
    }
}
