// Player weapon handling: fire modes, chamber + magazine, mag-swap reload from rig/pockets,
// round-by-round mag loading when no loaded mags, approximate mag check (T), ADS, recoil, sway.
using UnityEngine;

namespace Severny
{
    public class PlayerWeapon : MonoBehaviour
    {
        enum State { Ready, Reloading, Loading, Checking, Switching }

        PlayerController _pc;
        Loadout _L;
        public EquipSlot current = EquipSlot.Primary;
        WeaponView _view;
        ItemInstance _item;
        ItemDef _def;
        State _state;
        float _timer, _duration, _nextFire, _adsT, _lowerT, _retract, _flashT, _handleT;
        bool _triggerReleased = true, _magInPlayed;
        ItemInstance _pendingMag; InventoryGrid _pendingGrid;
        Vector3 _kickPos, _kickRot;
        FPArms _arms;
        float _boltT;
        Vector2 _sway;
        Vector3 _aimOffset;   // v09: breathing + kick + mouse sway of the holder (degrees) — the only offsets ADS shots inherit
        readonly RaycastHit[] _hits = new RaycastHit[8];

        public bool IsAiming { get; private set; }
        /// <summary>LAN: the local player fired (origin, direction, ammo, weapon).</summary>
        public static event System.Action<Vector3, Vector3, ItemDef, ItemDef> LocalShot;
        public ItemInstance Item => _item;
        public ItemDef Def => _def;
        public int ShotsFired { get; private set; }

        public void Init(PlayerController pc)
        {
            _pc = pc; _L = pc.loadout;
            Equip(_L.Get(EquipSlot.Primary) != null ? EquipSlot.Primary : EquipSlot.Holster, true);
        }

        public void Equip(EquipSlot s, bool instant = false)
        {
            if (_view != null && _view.root != null) Destroy(_view.root);
            if (_arms != null) Destroy(_arms.gameObject);
            _view = null; _arms = null;
            current = s;
            _item = _L.Get(s);
            _def = _item?.Def;
            _state = State.Ready;
            _pendingMag = null;
            if (_def == null) return;
            _view = WeaponModels.Build(_def, _pc.weaponHolder, true, true);
            if (_view.detailed) _arms = FPArms.Create(_view.tag, _pc.camT, _view.root.transform);
            if (_view.sight != null) _view.sightLocal = _pc.weaponHolder.InverseTransformPoint(_view.sight.position);   // ADS centring from the final pose
            if (!instant) { _state = State.Switching; _timer = _duration = 0.45f; _lowerT = 1f; SFX.Play2D(SFX.WeaponSwitch, 0.5f); }
        }

        /// <summary>Call after inventory changes; re-equips if the held weapon was moved/removed.</summary>
        public void SyncWithLoadout()
        {
            var inSlot = _L.Get(current);
            if (inSlot != _item)
            {
                if (inSlot != null) Equip(current);
                else if (_L.Get(EquipSlot.Primary) != null) Equip(EquipSlot.Primary);
                else if (_L.Get(EquipSlot.Holster) != null) Equip(EquipSlot.Holster);
                else Equip(current);
            }
        }

        public void OnOwnerDied() { if (_view != null && _view.root != null) _view.root.SetActive(false); if (_arms != null) _arms.gameObject.SetActive(false); IsAiming = false; }

        void Update()
        {
            if (_pc == null || _pc.health.IsDead) return;
            float dt = Time.deltaTime;
            if (GameInput.GDown(GKey.Weapon1) && _L.Get(EquipSlot.Primary) != null && (current != EquipSlot.Primary || _item == null)) Equip(EquipSlot.Primary);
            if (GameInput.GDown(GKey.Weapon2) && _L.Get(EquipSlot.Holster) != null && (current != EquipSlot.Holster || _item == null)) Equip(EquipSlot.Holster);
            if (_item == null || _def == null) { IsAiming = false; return; }

            bool canAct = GameInput.GameplayEnabled && !_pc.IsUsingMed && !_pc.IsSprinting;
            IsAiming = canAct && GameInput.GAimHeld() && _state == State.Ready;
            float adsTime = (_def.isPistol ? 0.14f : 0.24f) * Mathf.Lerp(1.3f, 0.8f, _def.ergo / 100f);
            _adsT = Mathf.MoveTowards(_adsT, IsAiming ? 1f : 0f, dt / adsTime);

            switch (_state)
            {
                case State.Switching:
                    _timer -= dt; if (_timer <= 0f) _state = State.Ready; break;
                case State.Reloading:
                    _timer -= dt;
                    if (!_magInPlayed && _timer < _duration * 0.4f) { _magInPlayed = true; SFX.Play2D(SFX.MagIn, 0.6f); }
                    if (_timer <= 0f) FinishReload();
                    break;
                case State.Loading:
                    _timer -= dt;
                    if (GameInput.GFireDown() || GameInput.GDown(GKey.Reload)) { EndLoading(); break; }
                    if (_timer <= 0f) LoadOneRound();
                    break;
                case State.Checking:
                    _timer -= dt;
                    if (_timer <= 0f) { _state = State.Ready; Hud.Notify(MagCheckText(), 2.5f); }
                    break;
            }

            if (_state == State.Ready && canAct)
            {
                if (GameInput.GDown(GKey.FireMode))
                {
                    if (_def.fullAuto) { _item.fullAutoMode = !_item.fullAutoMode; Hud.Notify(_item.fullAutoMode ? "Режим: автоматический" : "Режим: одиночный", 1.5f); }
                    else Hud.Notify("Режим: только одиночный", 1.5f);
                    SFX.Play2D(SFX.FireMode, 0.5f);
                }
                else if (GameInput.GDown(GKey.Reload)) StartReload();
                else if (GameInput.GDown(GKey.CheckMag)) { _state = State.Checking; _timer = 1.1f; SFX.Play2D(SFX.MagOut, 0.3f); }
            }

            bool fireHeld = GameInput.GFireHeld() && canAct;
            if (!fireHeld) _triggerReleased = true;
            // v09: no shots while the weapon is still coming up after a sprint / weapon switch / med use
            if (_state == State.Ready && fireHeld && Time.time >= _nextFire && _lowerT < 0.3f)
            {
                bool auto = _def.fullAuto && _item.fullAutoMode;
                if (auto || _triggerReleased)
                {
                    _triggerReleased = false;
                    if (_item.chamber > 0) Shoot();
                    else { SFX.Play2D(SFX.DryClick, 0.6f); _nextFire = Time.time + 0.3f; Hud.Notify("Щелчок — R для перезарядки", 1.2f); }
                }
            }
            UpdateView(dt);
        }

        ItemDef CurrentAmmoDef()
        {
            var a = ItemDB.Get(_item.ammoId);
            if (a != null) return a;
            foreach (var d in ItemDB.All) if (d.type == ItemType.Ammo && d.caliber == _def.caliber) return d;
            return null;
        }

        void Shoot()
        {
            _nextFire = Time.time + 60f / Mathf.Max(60f, _def.rpm);
            var ammo = CurrentAmmoDef();
            _item.chamber = 0;
            if (_item.ammo > 0) { _item.ammo--; _item.chamber = 1; }
            float spread = _def.spreadDeg * (IsAiming ? 1f : 7f) + (_pc.SpeedNow > 0.5f ? (IsAiming ? 0.25f : 1.1f) : 0f);
            spread *= _pc.health.AimSwayMultiplier;
            Transform cam = _pc.camT;
            // v09: ADS shots follow the sights = camera axis + breathing/kick/sway only. They used to take weaponHolder.forward, which also
            // carried the lowered (sprint/switch) pose — the first shots after a sprint flew up to 28° to the left — and the wall-retract tilt.
            Vector3 dir = IsAiming ? cam.rotation * (Quaternion.Euler(_aimOffset) * Vector3.forward) : cam.forward;
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(spread * Mathf.Deg2Rad);
            dir = (dir + cam.right * r.x + cam.up * r.y).normalized;
            Ballistics.Fire(cam.position, dir, ammo, _pc.transform, true);
            LocalShot?.Invoke(cam.position, dir, ammo, _def);
            ShotsFired++;
            float v = _def.recoilV * Random.Range(0.85f, 1.15f) * (_pc.Crouched ? 0.85f : 1f) * (IsAiming ? 0.9f : 1f);
            float h = _def.recoilH * Random.Range(-1f, 1f);
            _pc.AddRecoil(v, h);
            _kickPos.z -= _def.isPistol ? 0.025f : 0.035f;
            _boltT = 1f;
            _kickRot.x -= 3.5f * v;
            _kickRot.y += h * 1.5f;
            SFX.Play2D(SFX.ShotFor(_def), 0.85f, 0.04f);
            if (_view != null) { _view.flash.SetActive(true); _view.light.enabled = true; _view.flash.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f)); }
            _flashT = 0.04f;
            NoiseSystem.Emit(_pc.transform.position, _def.isPistol ? 110f : 170f, _pc.gameObject, true);
        }

        int MagCapacity { get { var m = ItemDB.Get(_def.magId); return m != null ? m.capacity : 30; } }

        void StartReload()
        {
            int cap = MagCapacity;
            if (_item.ammo >= cap) { Hud.Notify("Магазин полный", 1.2f); return; }
            ItemInstance best = null; InventoryGrid bestG = null;
            foreach (var g in _L.QuickGrids())
            {
                var m = g.BestMag(_def.magId);
                if (m != null && m.ammo > 0 && (best == null || m.ammo > best.ammo)) { best = m; bestG = g; }
            }
            if (best != null && best.ammo > _item.ammo)
            {
                _pendingMag = best; _pendingGrid = bestG;
                _state = State.Reloading;
                _duration = _timer = _def.reloadTime + (_item.chamber == 0 ? 0.45f : 0f);
                _magInPlayed = false;
                SFX.Play2D(SFX.MagOut, 0.6f);
                NoiseSystem.Emit(_pc.transform.position, 8f, _pc.gameObject);
                return;
            }
            int loose = 0; foreach (var g in _L.AllGrids()) loose += g.AmmoCount(_def.caliber);
            if (loose > 0)
            {
                _state = State.Loading; _timer = 0.6f;
                Hud.Notify("Нет снаряжённых магазинов в разгрузке/карманах — снаряжаю текущий (ЛКМ/R — стоп)", 2.5f);
                return;
            }
            Hud.Notify("Нет патронов " + _def.caliber.Replace("x", "×"), 1.8f);
        }

        void FinishReload()
        {
            _state = State.Ready;
            if (_pendingMag == null || _pendingGrid == null || !_pendingGrid.items.Contains(_pendingMag)) { _pendingMag = null; return; }
            int oldAmmo = _item.ammo; string oldId = _item.ammoId;
            int x = _pendingMag.x, y = _pendingMag.y; bool rot = _pendingMag.rot;
            _pendingGrid.Remove(_pendingMag);
            _item.ammo = _pendingMag.ammo; _item.ammoId = _pendingMag.ammoId;
            var old = ItemInstance.Create(_def.magId); old.ammo = oldAmmo; old.ammoId = oldId;
            if (!_pendingGrid.TryPlaceAt(old, x, y, rot) && !_L.AutoPlace(old))
                WorldItem.Drop(old, _pc.transform.position + _pc.transform.forward * 0.6f + Vector3.up * 0.5f);
            _pendingMag = null;
            Rack();
        }

        void Rack()
        {
            if (_item.chamber == 0 && _item.ammo > 0) { _item.ammo--; _item.chamber = 1; SFX.Play2D(SFX.BoltRack, 0.6f); }
        }

        void LoadOneRound()
        {
            int cap = MagCapacity;
            if (_item.ammo >= cap) { EndLoading(); return; }
            string aid = _item.ammoId;
            int got = 0;
            foreach (var g in _L.AllGrids())
            {
                got = g.TakeAmmo(_def.caliber, 1, ref aid, _item.ammo == 0);
                if (got > 0) break;
            }
            if (got <= 0) { EndLoading(); return; }
            _item.ammo += got; _item.ammoId = aid;
            SFX.Play2D(SFX.DryClick, 0.25f, 0.15f);
            Hud.Progress("Снаряжение: " + _item.ammo + "/" + cap, (float)_item.ammo / cap);
            _timer = 0.32f;
            if (_item.ammo >= cap) EndLoading();
        }

        void EndLoading()
        {
            _state = State.Ready;
            Hud.Progress(null, 0f);
            Rack();
        }

        string MagCheckText()
        {
            float f = (float)_item.ammo / Mathf.Max(1, MagCapacity);
            string s = _item.ammo == 0 ? "Магазин пуст" : f < 0.2f ? "Почти пусто" : f < 0.45f ? "Меньше половины" : f < 0.7f ? "Около половины" : f < 0.98f ? "Почти полный" : "Полный";
            if (_def.isPistol || GameSettings.ShowFps) s += "  (" + _item.ammo + ")"; // pistol mags are easy to count
            return s + (_item.chamber > 0 ? " · патрон в патроннике" : " · патронник пуст");
        }

        public string StatusLine()
        {
            if (_def == null) return "";
            string mode = _def.fullAuto ? (_item.fullAutoMode ? "АВТ" : "ОД") : "ОД";
            return _def.shortName + "  " + mode;
        }

        void UpdateBoltAndHands(float dt)
        {
            if (_view.bolt != null)
            {
                _boltT = Mathf.MoveTowards(_boltT, 0f, dt * (_def.isPistol ? 16f : 20f));
                float b = _boltT > 0.6f ? (1f - _boltT) / 0.4f : _boltT / 0.6f;   // fast back, slower return
                _view.bolt.localPosition = _view.boltRest + _view.boltBack * (b * (_def.isPistol ? 0.024f : 0.07f));
            }
            if (_arms == null) return;
            float w = 0f;
            if (_state == State.Reloading && _view.mag != null)
            {
                float p = 1f - _timer / Mathf.Max(0.01f, _duration);
                w = Mathf.Clamp01(Mathf.Sin(Mathf.Clamp01((p - 0.06f) / 0.78f) * Mathf.PI) * 1.7f);
                Vector3 grab = _def.isPistol ? new Vector3(-0.012f, -0.075f, -0.005f) : new Vector3(-0.035f, -0.10f, 0.025f);
                _arms.leftPos = _view.mag.position + _pc.weaponHolder.TransformDirection(grab);   // grab offset is camera-aligned (holder frame)
                _arms.leftRot = _arms.LeftGripRot;
            }
            _arms.leftWeight = Mathf.MoveTowards(_arms.leftWeight, w, dt * 6f);
        }

        void UpdateView(float dt)
        {
            if (_view == null) return;
            if (_flashT > 0f) { _flashT -= dt; if (_flashT <= 0f) { _view.flash.SetActive(false); _view.light.enabled = false; } }
            Vector3 hip = _def.isPistol ? new Vector3(0.13f, -0.15f, 0.30f) : new Vector3(0.15f, -0.17f, 0.31f);
            Vector3 ads = _def.isPistol ? new Vector3(0f, -0.022f, 0.34f) : new Vector3(0f, -0.066f, 0.20f);
            if (_view.detailed)
            {
                hip = _arms != null ? _arms.hipLocal : (_def.isPistol ? ModelSpecs.Hip_P9M + FPArms.PistolHoldOffset : ModelSpecs.Hip_AV74);
                Vector3 sl = _view.sightLocal;   // sight in holder space
                float relief = _def.isPistol ? 0.44f : 0.30f;   // v10.1: pistol sights 44 cm from the eye (was 38) -> arms almost straight
                ads = new Vector3(-sl.x, -sl.y, relief - sl.z);
            }
            float a = Mathf.SmoothStep(0f, 1f, _adsT);
            Vector3 pos = Vector3.Lerp(hip, ads, a);
            bool handling = _state == State.Reloading || _state == State.Loading || _state == State.Checking;
            _handleT = Mathf.MoveTowards(_handleT, handling ? 1f : 0f, dt * 7f);
            // v10.1: the pistol is held at arm's length now - bring it in to the chest for reloads / mag checks (left hand reaches the mag)
            if (_def.isPistol) pos += new Vector3(0f, 0.04f, -0.16f) * Mathf.SmoothStep(0f, 1f, _handleT);
            if (_arms != null) { _arms.ads = a; _arms.poseLocal = pos; }
            bool lowered = _pc.IsSprinting || _pc.IsUsingMed || _state == State.Switching;
            _lowerT = Mathf.MoveTowards(_lowerT, lowered ? 1f : 0f, dt * 4f);
            Vector3 rot = new Vector3(22f, -28f, 12f) * _lowerT;
            pos += new Vector3(-0.04f, -0.06f, -0.06f) * _lowerT;
            if (_handleT > 0f)
            {
                float hb = Mathf.SmoothStep(0f, 1f, _handleT);   // v10.1: blended in/out instead of snapping
                rot += new Vector3(12f, -10f, 28f) * hb;
                pos += new Vector3(-0.03f, 0.02f, 0f) * hb;
            }
            if (handling)
            {
                if (_view.mag != null && _state == State.Reloading)
                {
                    float p = 1f - _timer / Mathf.Max(0.01f, _duration);
                    float off = p > 0.15f && p < 0.65f ? Mathf.Sin((p - 0.15f) / 0.5f * Mathf.PI) * 0.18f : 0f;
                    _view.mag.localPosition = _view.magRest + _view.magDown * off;   // v09: drop along the weapon's down (FBX root is X-rotated)
                }
            }
            else if (_view.mag != null) _view.mag.localPosition = _view.magRest;
            // wall retract
            int n = Physics.RaycastNonAlloc(_pc.camT.position, _pc.camT.forward, _hits, 0.8f, ~0, QueryTriggerInteraction.Ignore);
            float near = 0.8f;
            for (int i = 0; i < n; i++) { var c = _hits[i].collider; if (c.transform.IsChildOf(_pc.transform)) continue; near = Mathf.Min(near, _hits[i].distance); }
            _retract = Mathf.MoveTowards(_retract, 1f - near / 0.8f, dt * 5f);
            pos += new Vector3(0f, -0.03f, -0.22f) * _retract;
            rot += new Vector3(-25f, 0f, 0f) * _retract;
            if (_arms != null) _arms.expectVisible = _lowerT < 0.05f && _retract < 0.2f && _handleT < 0.05f;   // v10.2 out-of-view watchdog
            // mouse sway + breathing sway
            Vector2 md = GameInput.GMouseDelta();
            Vector2 target = Vector2.ClampMagnitude(new Vector2(md.y, -md.x) * 0.035f, 3.5f);
            _sway = Vector2.Lerp(_sway, target, 1f - Mathf.Exp(-9f * dt));
            float stam = _pc.stamina / Mathf.Max(1f, _pc.staminaMax);
            float bAmp = (IsAiming ? 0.12f + (1f - stam) * 0.6f : 0.35f) * _pc.health.AimSwayMultiplier;
            float t = Time.time;
            Vector3 breath = new Vector3(Mathf.Sin(t * 1.15f) * bAmp, Mathf.Sin(t * 0.72f + 1.3f) * bAmp * 0.8f, 0f);
            _kickPos = Vector3.Lerp(_kickPos, Vector3.zero, 1f - Mathf.Exp(-14f * dt));
            _kickRot = Vector3.Lerp(_kickRot, Vector3.zero, 1f - Mathf.Exp(-12f * dt));
            Vector3 bob = _pc.BobOffset * (IsAiming ? 0.3f : 1.4f);
            _pc.weaponHolder.localPosition = pos + _kickPos + bob;
            _aimOffset = breath + _kickRot + new Vector3(_sway.x, _sway.y, 0f);
            _pc.weaponHolder.localRotation = Quaternion.Euler(rot + _aimOffset);
            UpdateBoltAndHands(dt);
            float fov = GameSettings.Fov;
            _pc.cam.fieldOfView = Mathf.Lerp(fov, fov * (_def.isPistol ? 0.9f : 0.8f), a) + (_pc.IsSprinting ? 3f : 0f);
        }
    }
}
