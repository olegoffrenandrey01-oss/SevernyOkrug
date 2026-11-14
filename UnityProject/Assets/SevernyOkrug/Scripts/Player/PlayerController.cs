// First-person controller: walk/sprint/crouch/lean/jump, stamina, weight, footsteps + noise,
// fall damage, interaction (F), meds (H / inventory), death camera, under-map safety net.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour, IBotTarget
    {
        public static PlayerController Local;

        public CharacterController cc;
        public Health health;
        public Hitbox hitbox;
        public Camera cam;
        public Transform head, camT, weaponHolder;
        public PlayerWeapon weapon;
        public Loadout loadout;

        [Header("Movement")]
        public float walkSpeed = 3.2f, sprintSpeed = 5.7f, crouchSpeed = 1.6f, slowWalkSpeed = 1.5f, adsSpeedMul = 0.62f;
        public float standHeight = 1.8f, crouchHeight = 1.15f, eyeStand = 1.62f, eyeCrouch = 1.02f;
        public float jumpSpeed = 4.4f, gravity = -16f;
        public float stamina = 100f, staminaMax = 100f;

        float _yaw, _pitch, _recoilPitch, _recoilYaw, _lean, _crouchT, _vy, _airMinVy, _stepDist, _exhaustedUntil, _regenDelay;
        float _weightCache, _weightTimer, _safeTimer, _deathT;
        bool _crouched, _grounded;
        Vector3 _horiz, _lastSafe;
        Interactable _focus;
        AudioSource _breath, _wind;
        readonly RaycastHit[] _hits = new RaycastHit[12];

        public bool Crouched => _crouched;
        public bool IsSprinting { get; private set; }
        public float SpeedNow { get; private set; }
        public float Lean => _lean;
        public Vector3 BobOffset { get; private set; }
        public float MinSafeY = -200f;

        // meds
        ItemInstance _medItem; InventoryGrid _medGrid; float _medTimer, _medDuration;
        public bool IsUsingMed => _medItem != null;

        public static PlayerController Spawn(Vector3 pos, float yawDeg, Loadout loadout, Transform parent)
        {
            var root = new GameObject("Player");
            if (parent != null) root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yawDeg, 0));
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.32f; cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.42f; cc.slopeLimit = 52f; cc.skinWidth = 0.04f; cc.minMoveDistance = 0f;
            var pc = root.AddComponent<PlayerController>();
            pc.cc = cc;
            pc.loadout = loadout;
            pc.health = root.AddComponent<Health>();
            pc.health.isPlayer = true;
            pc.health.armor = loadout.Get(EquipSlot.Armor);
            pc.health.helmet = loadout.Get(EquipSlot.Helmet);
            pc.hitbox = root.AddComponent<Hitbox>();
            pc.hitbox.owner = pc.health; pc.hitbox.heightBased = true; pc.hitbox.height = 1.8f;
            pc.head = new GameObject("Head").transform;
            pc.head.SetParent(root.transform, false);
            pc.head.localPosition = new Vector3(0, pc.eyeStand, 0);
            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            pc.camT = camGo.transform;
            pc.camT.SetParent(pc.head, false);
            pc.cam = camGo.AddComponent<Camera>();
            pc.cam.nearClipPlane = 0.03f; pc.cam.farClipPlane = 1600f;
            pc.cam.fieldOfView = GameSettings.Fov;
            camGo.AddComponent<AudioListener>();
            EnvironmentSetup.ConfigureCamera(pc.cam);
            pc.weaponHolder = new GameObject("WeaponHolder").transform;
            pc.weaponHolder.SetParent(pc.camT, false);
            pc._yaw = yawDeg;
            pc.weapon = root.AddComponent<PlayerWeapon>();
            pc.weapon.Init(pc);
            Ballistics.Listener = pc.camT;
            Local = pc;
            return pc;
        }

        void Start()
        {
            _lastSafe = transform.position;
            health.Died += OnDied;
            health.Damaged += d =>
            {
                Hud.DamageFlash();
                bool flesh = d.bullet && !health.LastHitArmored;
                if (flesh) SFX.Play2D(SFX.Pick(SFX.FleshHits, SFX.Hit), 0.6f);
                if ((flesh || d.cause == "fall") && Time.time >= _nextHurtSnd)
                {
                    _nextHurtSnd = Time.time + 0.9f;
                    SFX.Play2D(SFX.Pick(SFX.Hurts, null), 0.75f, 0.04f);
                }
            };
            _breath = gameObject.AddComponent<AudioSource>();
            _breath.clip = SFX.Breath; _breath.loop = true; _breath.volume = 0f; _breath.spatialBlend = 0f; _breath.Play();
            _wind = gameObject.AddComponent<AudioSource>();
            _wind.clip = SFX.Wind; _wind.loop = true; _wind.volume = 0.22f; _wind.spatialBlend = 0f; _wind.Play();
        }

        void OnDestroy() { if (Local == this) Local = null; }
        void OnEnable() { BotTargets.Add(this); }
        void OnDisable() { BotTargets.Remove(this); }

        // IBotTarget (bots see / shoot the player through this)
        public Transform Root => transform;
        public GameObject Go => gameObject;
        public Vector3 Head => camT != null ? camT.position : transform.position + Vector3.up * 1.6f;
        public float Height => cc != null ? cc.height : 1.8f;
        public bool Dead => health == null || health.IsDead;

        /// <summary>Call after the loadout changed (inventory UI, pickups): updates armor refs and the held weapon.</summary>
        public void RefreshEquipment()
        {
            health.armor = loadout.Get(EquipSlot.Armor);
            health.helmet = loadout.Get(EquipSlot.Helmet);
            if (weapon != null) weapon.SyncWithLoadout();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (health.IsDead) { DeathCam(dt); return; }
            Look(dt);
            Crouch(dt);
            Move(dt);
            LeanUpdate(dt);
            Stamina(dt);
            InteractUpdate();
            MedUpdate(dt);
            if (GameInput.GDown(GKey.Heal)) QuickHeal();
            if (GameInput.GDown(GKey.Flashlight)) ToggleTorch();
            SafetyNet(dt);
        }

        // ---------------- look / recoil ----------------
        void Look(float dt)
        {
            Vector2 md = GameInput.GMouseDelta();
            float s = GameSettings.Sensitivity * 0.08f * (weapon != null && weapon.IsAiming ? 0.6f : 1f);
            _yaw += md.x * s;
            _pitch -= md.y * s * (GameSettings.InvertY ? -1f : 1f);
            _pitch = Mathf.Clamp(_pitch, -86f, 86f);
            float k = 1f - Mathf.Exp(-7.5f * dt);
            _recoilPitch = Mathf.Lerp(_recoilPitch, 0f, k);
            _recoilYaw = Mathf.Lerp(_recoilYaw, 0f, k);
            transform.rotation = Quaternion.Euler(0f, _yaw + _recoilYaw, 0f);
            camT.localRotation = Quaternion.Euler(_pitch - _recoilPitch, 0f, 0f);
        }

        /// <summary>Weapon recoil: part springs back, part stays (player must pull down).</summary>
        public void AddRecoil(float up, float side)
        {
            _recoilPitch += up * 0.62f;
            _pitch -= up * 0.38f;
            _recoilYaw += side * 0.5f;
            _yaw += side * 0.5f;
        }

        // ---------------- movement ----------------
        float WeightMul
        {
            get
            {
                _weightTimer -= Time.deltaTime;
                if (_weightTimer <= 0f) { _weightTimer = 1f; _weightCache = loadout != null ? loadout.TotalWeight() : 0f; }
                return _weightCache > 32f ? Mathf.Lerp(1f, 0.72f, Mathf.Clamp01((_weightCache - 32f) / 30f)) : 1f;
            }
        }
        public float CarriedWeight => _weightCache;

        void Move(float dt)
        {
            Vector2 inp = Vector2.zero;
            if (GameInput.GHeld(GKey.Forward)) inp.y += 1f;
            if (GameInput.GHeld(GKey.Back)) inp.y -= 1f;
            if (GameInput.GHeld(GKey.Right)) inp.x += 1f;
            if (GameInput.GHeld(GKey.Left)) inp.x -= 1f;
            bool aiming = weapon != null && weapon.IsAiming;
            bool wantSprint = GameInput.GHeld(GKey.Sprint) && inp.y > 0.1f && !_crouched && health.CanSprint && stamina > 0.5f
                              && Time.time > _exhaustedUntil && !aiming && !IsUsingMed;
            float speed = _crouched ? crouchSpeed : (GameInput.GHeld(GKey.Walk) ? slowWalkSpeed : walkSpeed);
            if (wantSprint) speed = sprintSpeed;
            if (aiming) speed *= adsSpeedMul;
            if (IsUsingMed) speed *= 0.5f;
            speed *= health.SpeedMultiplier * WeightMul;
            Vector3 wish = transform.right * inp.x + transform.forward * inp.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            float accel = _grounded ? 11f : 1.5f;
            _horiz = Vector3.Lerp(_horiz, wish * speed, 1f - Mathf.Exp(-accel * dt));

            if (_grounded)
            {
                if (_vy < 0f) _vy = -2f;
                if (GameInput.GDown(GKey.Jump) && !_crouched && stamina >= 10f)
                {
                    _vy = jumpSpeed; stamina -= 12f; _regenDelay = 1f;
                    SFX.Play2D(SFX.Jump, 0.45f, 0.05f);
                    NoiseSystem.Emit(transform.position, 10f, gameObject);
                }
            }
            _vy += gravity * dt;
            if (!_grounded) _airMinVy = Mathf.Min(_airMinVy, _vy);
            var flags = cc.Move((_horiz + Vector3.up * _vy) * dt);
            bool was = _grounded;
            _grounded = (flags & CollisionFlags.Below) != 0 || cc.isGrounded;
            if ((flags & CollisionFlags.Above) != 0 && _vy > 0f) _vy = 0f;
            if (!was && _grounded) Landed(-_airMinVy);
            if (_grounded) _airMinVy = 0f;

            Vector3 hv = cc.velocity; hv.y = 0f;
            SpeedNow = hv.magnitude;
            IsSprinting = wantSprint && SpeedNow > walkSpeed * 0.8f;
            // footsteps + noise
            if (_grounded && SpeedNow > 0.4f)
            {
                _stepDist += SpeedNow * dt;
                float stride = IsSprinting ? 1.9f : (_crouched ? 0.85f : 1.45f);
                if (_stepDist >= stride)
                {
                    _stepDist = 0f;
                    float vol = IsSprinting ? 0.55f : (_crouched || GameInput.GHeld(GKey.Walk) ? 0.12f : 0.3f);
                    var steps = SFX.CurSteps; SFX.Play2D(steps[Random.Range(0, steps.Length)], vol * (SFX.SnowFootsteps ? 1.25f : 1f), 0.08f);
                    float radius = IsSprinting ? 30f : (_crouched ? 4f : (GameInput.GHeld(GKey.Walk) ? 6f : 14f));
                    NoiseSystem.Emit(transform.position, radius, gameObject);
                }
            }
            // head bob
            float bobAmt = _grounded ? Mathf.Clamp01(SpeedNow / sprintSpeed) : 0f;
            float ph = Time.time * (IsSprinting ? 13f : 9f);
            BobOffset = Vector3.Lerp(BobOffset, new Vector3(Mathf.Cos(ph * 0.5f) * 0.012f, Mathf.Sin(ph) * 0.018f, 0f) * bobAmt * (aiming ? 0.25f : 1f), 1f - Mathf.Exp(-10f * dt));
        }

        void Landed(float impact)
        {
            SFX.Play2D(impact > 3.5f ? SFX.Land : SFX.Steps[0], Mathf.Clamp01(0.3f + impact * 0.05f));
            if (impact > 6f) NoiseSystem.Emit(transform.position, 20f, gameObject);
            if (impact > 9.5f)
            {
                float dmg = (impact - 9.5f) * 9f;
                SFX.Play2D(SFX.Thud, 0.9f);
                health.ApplyDamage(new DamageInfo { part = BodyPart.LeftLeg, damage = dmg * 0.5f, cause = "fall" });
                health.ApplyDamage(new DamageInfo { part = BodyPart.RightLeg, damage = dmg * 0.5f, cause = "fall" });
            }
        }

        bool CanStand()
        {
            return CanOccupyHeight(standHeight);
        }

        bool CanOccupyHeight(float height)
        {
            // A cast does not report colliders overlapping its starting sphere. Test the target capsule,
            // inset by the controller skin so resting on the floor is permitted.
            float radius = Mathf.Max(0.05f, cc.radius - cc.skinWidth);
            Vector3 foot = transform.TransformPoint(cc.center) - Vector3.up * (cc.height * 0.5f);
            var overlaps = Physics.OverlapCapsule(foot + Vector3.up * (cc.radius + 0.01f),
                foot + Vector3.up * (height - cc.radius), radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var col in overlaps)
                if (col != cc && !col.transform.IsChildOf(transform)) return false;
            return true;
        }

        void Crouch(float dt)
        {
            if (GameInput.GDown(GKey.Crouch))
            {
                if (_crouched) { if (CanStand()) _crouched = false; else Hud.Notify("Мало места, чтобы встать"); }
                else _crouched = true;
            }
            if (_crouched && GameInput.GHeld(GKey.Sprint) && GameInput.GHeld(GKey.Forward) && health.CanSprint && CanStand()) _crouched = false;
            float nextT = Mathf.MoveTowards(_crouchT, _crouched ? 1f : 0f, dt * 5f);
            float h = Mathf.Lerp(standHeight, crouchHeight, nextT);
            if (h > cc.height && !CanOccupyHeight(h)) { _crouched = true; h = cc.height; }
            else _crouchT = nextT;
            cc.height = h; cc.center = new Vector3(0, h * 0.5f, 0);
            hitbox.height = h;
        }

        void LeanUpdate(float dt)
        {
            float target = 0f;
            if (GameInput.GHeld(GKey.LeanLeft)) target -= 1f;
            if (GameInput.GHeld(GKey.LeanRight)) target += 1f;
            if (IsSprinting) target = 0f;
            _lean = Mathf.MoveTowards(_lean, target, dt * 4.5f);
            float eye = Mathf.Lerp(eyeStand, eyeCrouch, _crouchT);
            float dist = 0.34f * _lean;
            if (Mathf.Abs(dist) > 0.01f)
            {
                Vector3 o = transform.position + Vector3.up * eye;
                Vector3 dir = transform.right * Mathf.Sign(dist);
                int n = Physics.SphereCastNonAlloc(o, 0.14f, dir, _hits, Mathf.Abs(dist) + 0.05f, ~0, QueryTriggerInteraction.Ignore);
                float allowed = Mathf.Abs(dist);
                for (int i = 0; i < n; i++)
                {
                    var c = _hits[i].collider;
                    if (c == cc || c.transform.IsChildOf(transform)) continue;
                    allowed = Mathf.Min(allowed, Mathf.Max(0f, _hits[i].distance - 0.05f));
                }
                dist = Mathf.Sign(dist) * allowed;
            }
            head.localPosition = new Vector3(dist, eye, 0f) + BobOffset;
            head.localRotation = Quaternion.Euler(0f, 0f, -_lean * 11f);
        }

        void Stamina(float dt)
        {
            if (IsSprinting)
            {
                stamina -= (9f + Mathf.Max(0f, _weightCache - 25f) * 0.25f) * dt;
                _regenDelay = 1.2f;
                if (stamina <= 0f) { stamina = 0f; _exhaustedUntil = Time.time + 3f; }
            }
            else if (_regenDelay > 0f) _regenDelay -= dt;
            else stamina = Mathf.Min(staminaMax, stamina + (SpeedNow < 0.3f ? 12f : 7f) * dt);
            if (_breath != null)
            {
                float target = stamina < 35f ? (1f - stamina / 35f) * 0.55f : 0f;
                _breath.volume = Mathf.MoveTowards(_breath.volume, target, dt * 0.5f);
            }
        }

        // ---------------- interaction ----------------
        void InteractUpdate()
        {
            _focus = null;
            if (GameInput.GameplayEnabled && !IsUsingMed)
            {
                int n = Physics.RaycastNonAlloc(new Ray(camT.position, camT.forward), _hits, 2.4f, ~0, QueryTriggerInteraction.Collide);
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var c = _hits[i].collider;
                    if (c == cc || c.transform.IsChildOf(transform)) continue;
                    if (_hits[i].distance >= best) continue;
                    var it = c.GetComponentInParent<Interactable>();
                    if (it == null) { if (!c.isTrigger) { best = _hits[i].distance; _focus = null; } continue; }
                    if (!it.CanInteract) continue;
                    best = _hits[i].distance; _focus = it;
                }
            }
            Hud.Prompt(_focus != null ? "F  " + _focus.Prompt : null);
            if (_focus != null && GameInput.GDown(GKey.Interact)) _focus.Interact(this);
        }

        // ---------------- meds ----------------
        IEnumerable<(ItemInstance, InventoryGrid)> AllItems()
        {
            foreach (var g in loadout.AllGrids())
                foreach (var it in g.items) yield return (it, g);
        }

        public void QuickHeal()
        {
            if (IsUsingMed) { CancelMed(); return; }
            (ItemInstance, InventoryGrid) pick = (null, null);
            if (health.AnyBleeding)
                foreach (var e in AllItems()) { var d = e.Item1.Def; if (d.type == ItemType.Medical && d.stopsBleed && e.Item1.dur > 0f) { pick = e; if (d.med == MedKind.Bandage) break; } }
            if (pick.Item1 == null && health.HasDestroyed)
                foreach (var e in AllItems()) if (e.Item1.Def.med == MedKind.Surgery) { pick = e; break; }
            if (pick.Item1 == null && health.NeedsHealing)
                foreach (var e in AllItems()) if (e.Item1.Def.med == MedKind.Medkit && e.Item1.dur > 0f) { pick = e; break; }
            if (pick.Item1 == null && health.LegsBroken && !health.PainSuppressed)
                foreach (var e in AllItems()) if (e.Item1.Def.med == MedKind.Painkiller) { pick = e; break; }
            if (pick.Item1 == null) { Hud.Notify(health.AnyBleeding ? "Нет средств от кровотечения" : "Лечение не требуется / нет медикаментов"); return; }
            StartMed(pick.Item1, pick.Item2);
        }

        public bool StartMed(ItemInstance it, InventoryGrid g)
        {
            var d = it?.Def;
            if (d == null || d.type != ItemType.Medical || health.IsDead) return false;
            if (IsUsingMed) return false;
            bool useful = (d.med == MedKind.Bandage && health.AnyBleeding)
                          || (d.med == MedKind.Medkit && (health.NeedsHealing || (d.stopsBleed && health.AnyBleeding)))
                          || (d.med == MedKind.Surgery && health.HasDestroyed)
                          || d.med == MedKind.Painkiller;
            if (!useful) { Hud.Notify("Сейчас это не нужно"); return false; }
            _medItem = it; _medGrid = g; _medDuration = _medTimer = d.useTime;
            _medClip = SFX.MedFor(d.med);
            _medSnd = SFX.Play2D(_medClip, 0.7f, 0.02f);
            return true;
        }

        AudioSource _medSnd; AudioClip _medClip;
        float _nextHurtSnd;
        Light _torch;
        public bool TorchOn => _torch != null && _torch.enabled;

        public void CancelMed() { _medItem = null; Hud.Progress(null, 0f); SFX.Stop(_medSnd, _medClip); _medSnd = null; }

        /// <summary>Flashlight (L): spot light on the camera.</summary>
        void ToggleTorch()
        {
            if (_torch == null)
            {
                var g = new GameObject("Torch");
                g.transform.SetParent(camT, false);
                g.transform.localPosition = new Vector3(0.12f, -0.1f, 0.15f);
                _torch = g.AddComponent<Light>();
                _torch.type = LightType.Spot; _torch.range = 38f; _torch.spotAngle = 62f; _torch.innerSpotAngle = 24f;
                _torch.intensity = 16f; _torch.color = new Color(1f, 0.94f, 0.84f);
                _torch.shadows = LightShadows.None; _torch.renderMode = LightRenderMode.ForcePixel;
                _torch.enabled = false;
            }
            _torch.enabled = !_torch.enabled;
            SFX.Play2D(SFX.Torch, 0.6f, 0.05f);
            Hud.Notify(_torch.enabled ? "Фонарь включён (L)" : "Фонарь выключен (L)", 1f);
        }

        void MedUpdate(float dt)
        {
            if (_medItem == null) return;
            _medTimer -= dt;
            if (_medTimer > 1.2f && (_medSnd == null || !_medSnd.isPlaying || _medSnd.clip != _medClip)) _medSnd = SFX.Play2D(_medClip, 0.6f, 0.02f);
            Hud.Progress("Применение: " + _medItem.Def.shortName + "  (H — отмена)", 1f - _medTimer / Mathf.Max(0.01f, _medDuration));
            if (_medTimer > 0f) return;
            var it = _medItem; var g = _medGrid; var d = it.Def;
            _medItem = null; Hud.Progress(null, 0f);
            bool consumed = false;
            switch (d.med)
            {
                case MedKind.Bandage:
                    health.StopOneBleed(); consumed = true; Hud.Notify("Кровотечение остановлено"); break;
                case MedKind.Medkit:
                    if (d.stopsBleed && health.AnyBleeding && it.dur >= 1f) { health.StopOneBleed(); it.dur -= 30f; }
                    float used = health.HealAmount(Mathf.Min(d.healPerUse, Mathf.Max(0f, it.dur)));
                    it.dur -= used;
                    consumed = it.dur <= 0.5f;
                    Hud.Notify("Восстановлено " + Mathf.RoundToInt(used) + " ед.");
                    break;
                case MedKind.Surgery:
                    if (health.RestoreDestroyedPart()) { it.dur -= 1f; Hud.Notify("Часть тела восстановлена"); }
                    consumed = it.dur <= 0.5f; break;
                case MedKind.Painkiller:
                    health.painkillerTimer = 120f; it.dur -= 1f; consumed = it.dur <= 0.5f; Hud.Notify("Обезболивающее: 120 с"); break;
            }
            if (consumed && g != null) g.Remove(it); else g?.Notify();
        }

        // ---------------- death / safety ----------------
        void OnDied(DamageInfo di)
        {
            CancelMed();
            if (SFX.Death != null) SFX.Play2D(SFX.Death, 0.9f, 0f);
            if (weapon != null) weapon.OnOwnerDied();
            if (_breath != null) _breath.volume = 0f;
            cc.enabled = false;
            RaidManager.I?.OnPlayerDied(di);
        }

        void DeathCam(float dt)
        {
            _deathT = Mathf.Min(1f, _deathT + dt * 1.4f);
            head.localPosition = Vector3.Lerp(new Vector3(0, eyeStand, 0), new Vector3(0.2f, 0.25f, 0), _deathT * _deathT);
            head.localRotation = Quaternion.Euler(0, 0, 75f * _deathT);
        }

        void SafetyNet(float dt)
        {
            _safeTimer -= dt;
            if (_grounded && _safeTimer <= 0f) { _safeTimer = 1f; _lastSafe = transform.position; }
            if (transform.position.y < MinSafeY)
            {
                Debug.LogWarning("[Severny] Player fell below map at " + transform.position + " — returned to " + _lastSafe);
                NavTester.LogEvent("player_fell_below_map", transform.position);
                cc.enabled = false; transform.position = _lastSafe + Vector3.up * 0.5f; cc.enabled = true;
                _vy = 0f; _airMinVy = 0f;
                Hud.Notify("Провал под карту — возвращены на последнюю точку");
            }
        }
    }
}
