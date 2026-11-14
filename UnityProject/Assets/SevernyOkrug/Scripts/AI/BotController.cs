// "Мародёры" AI: NavMeshAgent FSM (patrol / investigate / combat / search / dead), vision cone + LOS with
// awareness build-up, hearing via NoiseSystem, reaction delay, distance-based accuracy, bursts, reloads,
// cover-seeking when hurt, lootable corpse.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Severny
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class BotController : MonoBehaviour
    {
        public enum S { Patrol, Investigate, Combat, Search, Dead }
        public static readonly List<BotController> All = new List<BotController>();

        public S state = S.Patrol;
        public float difficulty = 1f;
        public float visionRange = 110f, fovHalf = 65f, patrolRadius = 55f;
        public string botName = "Мародёр";

        NavMeshAgent _agent;
        public Health health;
        Transform _eye, _aim, _legL, _legR, _body;
        WeaponView _view;
        BotBody _bb;                 // v07 detailed character (null -> stickman fallback)
        public ItemInstance weaponItem;
        ItemDef _wdef, _ammo;
        public InventoryGrid loot = new InventoryGrid("Тело", 6, 6);

        Vector3 _home, _lastKnown, _investigate;
        float _awareness, _lastSeen = -99f, _seenSince = -1f, _reactAt, _nextThink, _nextShot, _burstLeft, _reloadUntil;
        float _waitUntil, _searchUntil, _strafeAt, _coverUntil, _lookYaw, _walkPhase, _deadT, _stuckT;
        int _magAmmo, _magCap;
        bool _canSeeNow;
        Vector3 _lastPos;
        Quaternion _deadFrom;
        readonly RaycastHit[] _hits = new RaycastHit[8];
        static readonly NavMeshPath _path = new NavMeshPath();

        public bool IsDead => state == S.Dead;
        /// <summary>Weapon / aim transform (night: the flashlight is attached here).</summary>
        public Transform TorchMount => _aim != null ? _aim : transform;

        // v08: several possible targets (LAN co-op): the nearest living one, the attacker after a hit
        IBotTarget _target;
        IBotTarget Target => !BotTargets.Gone(_target) && !_target.Dead ? _target : null;

        /// <summary>LAN: network id (host) and the seed of the body look (clients build the same character).</summary>
        public int nid, bodySeed;
        public static event System.Action<BotController, Vector3, Vector3> AnyShot;   // bot, origin, direction
        public static event System.Action<BotController, DamageInfo> AnyDied;
        public Vector3 Velocity => _agent != null && _agent.enabled ? _agent.velocity : Vector3.zero;
        public Quaternion AimRotation => _aim != null ? _aim.rotation : transform.rotation;
        public bool InCombat => state == S.Combat;
        public ItemDef AmmoDef => _ammo;

        // ------------------------------------------------------------------ spawn / build
        public static BotController Spawn(Vector3 pos, Transform parent, float difficulty, System.Random rng, string weaponId = null)
        {
            var g = new GameObject("Bot_Marauder");
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0));
            var rb = g.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var ag = g.AddComponent<NavMeshAgent>();
            ag.radius = 0.35f; ag.height = 1.8f; ag.speed = 1.8f; ag.acceleration = 8f; ag.angularSpeed = 260f;
            ag.stoppingDistance = 0.3f; ag.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            ag.autoBraking = true;
            ag.updateRotation = false;       // v09: the controller turns the body itself (combat: always face the target, strafe sideways)
            var b = g.AddComponent<BotController>();
            b._agent = ag;
            b.difficulty = difficulty;
            b.health = g.AddComponent<Health>();
            bool rifle = weaponId != null ? weaponId == "av74" : rng.NextDouble() < 0.62;
            b.weaponItem = ItemInstance.Create(rifle ? "av74" : "p9m");
            b._wdef = b.weaponItem.Def;
            b._ammo = ItemDB.Get(rifle ? (rng.NextDouble() < 0.12 ? "a545bp" : "a545") : "a918");
            b.weaponItem.ammoId = b._ammo.id;
            b._magCap = ItemDB.Get(b._wdef.magId).capacity;
            b._magAmmo = b._magCap;
            float lk = Mathf.Max(1f, ItemDB.Luck / 1.25f);                 // night bots are better equipped
            if (rng.NextDouble() < 0.4 * lk) b.health.armor = ItemInstance.Create(rng.NextDouble() < 0.15 * lk ? "vest4" : "vest3");
            if (rng.NextDouble() < 0.3 * lk) b.health.helmet = ItemInstance.Create(rng.NextDouble() < 0.12 * lk ? "helm4" : "helm2");
            b.bodySeed = rng.Next(1, int.MaxValue);
            b.BuildBody(new System.Random(b.bodySeed));
            b.FillLoot(rng);
            return b;
        }

        void BuildBody(System.Random rng)
        {
            _bb = BotBody.Create(transform, health, _wdef, rng);
            if (_bb != null) { _body = transform; _aim = _bb.aim; _eye = _bb.eye; _view = _bb.view; return; }
            Color[] jackets = { new Color(0.27f, 0.29f, 0.22f), new Color(0.22f, 0.23f, 0.24f), new Color(0.31f, 0.27f, 0.2f), new Color(0.18f, 0.2f, 0.17f) };
            var jacket = GameAssets.Lit(jackets[rng.Next(jackets.Length)], 0.08f);
            var pants = GameAssets.Lit(new Color(0.19f, 0.2f, 0.18f), 0.08f);
            var mask = GameAssets.Lit(new Color(0.12f, 0.12f, 0.12f), 0.05f);
            var vest = GameAssets.Lit(new Color(0.2f, 0.23f, 0.18f), 0.1f);
            var helm = GameAssets.Lit(new Color(0.22f, 0.25f, 0.2f), 0.2f);
            _body = new GameObject("Body").transform; _body.SetParent(transform, false);
            _legL = Pivot("legL", new Vector3(-0.1f, 0.92f, 0)); _legR = Pivot("legR", new Vector3(0.1f, 0.92f, 0));
            HB(Prim.Box(_legL, new Vector3(0, -0.45f, 0), new Vector3(0.16f, 0.9f, 0.19f), pants), BodyPart.LeftLeg);
            HB(Prim.Box(_legR, new Vector3(0, -0.45f, 0), new Vector3(0.16f, 0.9f, 0.19f), pants), BodyPart.RightLeg);
            HB(Prim.Box(_body, new Vector3(0, 1.06f, 0), new Vector3(0.36f, 0.26f, 0.22f), jacket), BodyPart.Stomach);
            HB(Prim.Box(_body, new Vector3(0, 1.37f, 0), new Vector3(0.42f, 0.36f, 0.25f), jacket), BodyPart.Thorax);
            HB(Prim.Sphere(_body, new Vector3(0, 1.68f, 0.01f), 0.23f, mask), BodyPart.Head);
            if (health.armor != null) Prim.Box(_body, new Vector3(0, 1.3f, 0), new Vector3(0.45f, 0.42f, 0.29f), vest, false);
            if (health.helmet != null) Prim.Make(PrimitiveType.Sphere, _body, new Vector3(0, 1.72f, 0f), new Vector3(0.26f, 0.19f, 0.27f), helm, false);
            _aim = new GameObject("Aim").transform; _aim.SetParent(_body, false); _aim.localPosition = new Vector3(0, 1.45f, 0);
            HB(Prim.Box(_aim, new Vector3(-0.2f, -0.06f, 0.18f), new Vector3(0.1f, 0.1f, 0.42f), jacket, true, Quaternion.Euler(0, 18, 0)), BodyPart.LeftArm);
            HB(Prim.Box(_aim, new Vector3(0.2f, -0.08f, 0.08f), new Vector3(0.1f, 0.1f, 0.34f), jacket, true, Quaternion.Euler(0, -10, 0)), BodyPart.RightArm);
            _view = WeaponModels.Build(_wdef, _aim, false, false);
            _view.root.transform.localPosition = _wdef.isPistol ? new Vector3(0.0f, -0.05f, 0.42f) : new Vector3(0.08f, -0.06f, 0.25f);
            _eye = new GameObject("Eye").transform; _eye.SetParent(_body, false); _eye.localPosition = new Vector3(0, 1.66f, 0.1f);
        }

        Transform Pivot(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(_body, false); t.localPosition = p; return t; }

        void HB(GameObject g, BodyPart part)
        {
            var h = g.AddComponent<Hitbox>(); h.owner = health; h.part = part;
        }

        void FillLoot(System.Random rng)
        {
            float luck = ItemDB.Luck;
            loot.AutoPlace(weaponItem);
            int mags = rng.Next(1, 3) + (luck > 1.5f ? 1 : 0);
            for (int i = 0; i < mags; i++) { var m = ItemInstance.Create(_wdef.magId); m.ammo = rng.Next(_magCap / 2, _magCap + 1); m.ammoId = _ammo.id; loot.AutoPlace(m); }
            if (rng.NextDouble() < 0.8) loot.AutoPlace(ItemInstance.Create(_ammo.id, rng.Next(20, 60)));
            int extra = rng.Next(1, 3) + (luck > 1.5f ? 1 : 0);
            for (int i = 0; i < extra; i++)
            {
                var d = ItemDB.PickFrom(ItemDB.LootTable.Body, rng);
                var it = ItemInstance.Create(d.id); if (d.type == ItemType.Ammo) it.count = rng.Next(15, 45);
                loot.AutoPlace(it);
            }
            // sometimes something really worth the fight
            if (rng.NextDouble() < 0.18 * luck) loot.AutoPlace(ItemInstance.Create(ItemDB.PickFrom(ItemDB.LootTable.Safe, rng).id));
            if (rng.NextDouble() < 0.12 * luck) loot.AutoPlace(ItemInstance.Create(ItemDB.PickFrom(ItemDB.LootTable.Medbox, rng).id));
        }

        // ------------------------------------------------------------------ lifecycle
        void Start()
        {
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            if (health == null) health = GetComponent<Health>();
            _home = transform.position;
            _lastPos = transform.position;
            health.Damaged += OnDamaged;
            health.Died += OnDied;
            _nextThink = Time.time + Random.value * 0.3f;
        }

        void OnEnable() { All.Add(this); NoiseSystem.OnNoise += OnNoise; }
        void OnDisable() { All.Remove(this); NoiseSystem.OnNoise -= OnNoise; }

        float _nextHurtSnd;

        void OnDamaged(DamageInfo d)
        {
            if (state == S.Dead) return;
            if (Time.time >= _nextHurtSnd && d.damage > 4f && SFX.Hurts != null && SFX.Hurts.Length > 0)
            {
                _nextHurtSnd = Time.time + Random.Range(0.7f, 1.3f);
                SFX.PlayAt(SFX.Pick(SFX.Hurts, null), transform.position + Vector3.up * 1.6f, 0.85f, 3f, 55f, 0.12f);
            }
            var p = BotTargets.Of(d.attacker);
            if (p != null && !p.Dead)
            {
                _target = p;
                _lastKnown = p.Root.position; _lastSeen = Time.time; _awareness = 1f;
                if (state != S.Combat) { state = S.Combat; _reactAt = Time.time + Random.Range(0.25f, 0.6f) / difficulty; }
                if (health.Total < health.TotalMax * 0.45f) _coverUntil = 0f; // re-evaluate cover
            }
        }

        void OnNoise(NoiseSystem.Noise n)
        {
            if (state == S.Dead || state == S.Combat) return;
            if (n.source == gameObject) return;
            bool fromPlayer = BotTargets.Of(n.source) != null;
            float d = Vector3.Distance(n.pos, transform.position);
            if (d > n.radius * (fromPlayer ? 1f : 0.6f)) return;
            float err = Mathf.Clamp(d * 0.12f, 1f, 15f);
            _investigate = n.pos + new Vector3(Random.Range(-err, err), 0, Random.Range(-err, err));
            if (n.gunshot && fromPlayer) _awareness = Mathf.Max(_awareness, 0.5f);
            state = S.Investigate;
            Go(_investigate, n.gunshot ? 3.6f : 2.4f);
        }

        void OnDied(DamageInfo d)
        {
            state = S.Dead;
            if (_agent != null && _agent.enabled) { _agent.isStopped = true; _agent.enabled = false; }
            if (_bb != null) _bb.Die(d);
            else foreach (var hb in GetComponentsInChildren<Hitbox>()) { var c = hb.GetComponent<Collider>(); if (c != null) c.enabled = false; }
            _deadFrom = transform.rotation;
            SFX.PlayAt(SFX.Thud, transform.position + Vector3.up, 0.8f, 2f, 40f);
            if (SFX.Death != null) SFX.PlayAt(SFX.Death, transform.position + Vector3.up * 1.5f, 0.9f, 3f, 60f, 0.1f);
            // corpse loot
            weaponItem.ammo = Mathf.Max(0, _magAmmo - 1); weaponItem.chamber = _magAmmo > 0 ? 1 : 0;
            if (health.armor != null) loot.AutoPlace(health.armor);
            if (health.helmet != null) loot.AutoPlace(health.helmet);
            var lc = gameObject.AddComponent<LootContainer>();
            lc.isCorpse = true; lc.title = botName; lc.grid = loot;
            if (_bb == null)
            {
                var col = gameObject.AddComponent<BoxCollider>();
                col.isTrigger = true; col.center = new Vector3(0, 0.3f, -0.9f); col.size = new Vector3(0.8f, 0.6f, 1.9f);
            }
            bool byPlayer = PlayerController.Local != null && d.attacker == PlayerController.Local.gameObject;
            RaidManager.I?.OnBotKilled(this, byPlayer);
            AnyDied?.Invoke(this, d);
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            float dt = Time.deltaTime;
            if (state == S.Dead) { DeadAnim(dt); return; }
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
            if (Time.time >= _nextThink) { _nextThink = Time.time + 0.18f; Think(); }
            switch (state)
            {
                case S.Patrol: Patrol(); break;
                case S.Investigate: Investigate(); break;
                case S.Combat: Combat(dt); break;
                case S.Search: Search(); break;
            }
            if (state != S.Combat) FaceMovement();
            Animate(dt);
        }

        void Think()
        {
            _target = BotTargets.Nearest(transform.position, Target);
            var p = Target;
            _canSeeNow = false;
            if (p == null) { if (state == S.Combat) { state = S.Search; _searchUntil = Time.time + 8f; } return; }
            Vector3 eye = _eye.position;
            Vector3 head = p.Head, chest = p.Root.position + Vector3.up * (p.Height * 0.6f);
            Vector3 to = head - eye; float d = to.magnitude;
            float range = visionRange * (RenderSettings.fog ? Mathf.Clamp(1f / (RenderSettings.fogDensity * 75f), 0.55f, 1.2f) : 1f);
            bool night = RaidOptions.Night;
            if (night) range *= p.TorchOn ? 1.25f : 0.42f;          // night: a lit torch gives you away, darkness hides you
            bool inCone = d < 3.5f || (d < range && Vector3.Angle(transform.forward, to) < (state == S.Combat ? 100f : fovHalf));
            if (inCone && (LOS(eye, head, p) || LOS(eye, chest, p)))
            {
                _canSeeNow = true;
                float vis = (p.Crouched ? 0.55f : 1f) * (p.SpeedNow > 0.5f ? 1.25f : 0.85f) * (p.IsSprinting ? 1.3f : 1f);
                float gain = vis * difficulty * 0.18f * Mathf.Clamp(45f / Mathf.Max(d, 4f), 0.25f, 6f);
                if (d < 12f) gain *= 2f;
                if (night) gain *= p.TorchOn ? 1.2f : 0.55f;
                _awareness = Mathf.Min(1.2f, _awareness + gain);
                if (_awareness >= 1f)
                {
                    if (state != S.Combat) { state = S.Combat; _reactAt = Time.time + Random.Range(0.35f, 0.85f) / difficulty; _seenSince = Time.time; }
                    if (_seenSince < 0f) _seenSince = Time.time;
                    _lastKnown = p.Root.position; _lastSeen = Time.time;
                }
                else if (state == S.Patrol && _awareness > 0.45f) { state = S.Investigate; _investigate = p.Root.position; Go(_investigate, 2f); }
            }
            else
            {
                _seenSince = -1f;
                _awareness = Mathf.Max(0f, _awareness - 0.02f);
                if (state == S.Combat && Time.time - _lastSeen > 4.5f) { state = S.Search; _searchUntil = Time.time + 9f; Go(_lastKnown, 3.2f); }
            }
        }

        bool LOS(Vector3 from, Vector3 to, IBotTarget p)
        {
            Vector3 dir = to - from; float d = dir.magnitude;
            int n = Physics.RaycastNonAlloc(from, dir / d, _hits, d + 0.2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool player = false;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform)) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; player = c.transform.IsChildOf(p.Root); }
            }
            return player || n == 0;
        }

        void Go(Vector3 p, float speed)
        {
            if (!_agent.isOnNavMesh) return;
            if (NavMesh.SamplePosition(p, out var h, 6f, NavMesh.AllAreas)) p = h.position;
            _agent.isStopped = false; _agent.speed = speed; _agent.SetDestination(p);
        }

        bool Arrived => !_agent.pathPending && _agent.remainingDistance <= Mathf.Max(0.6f, _agent.stoppingDistance + 0.2f);

        void Patrol()
        {
            if (Arrived)
            {
                if (_waitUntil <= 0f) { _waitUntil = Time.time + Random.Range(2f, 7f); _lookYaw = Random.Range(-120f, 120f); }
                if (Time.time < _waitUntil) { Turn(Quaternion.Euler(0, transform.eulerAngles.y + _lookYaw * Time.deltaTime * 0.3f, 0), 40f); return; }
                _waitUntil = 0f;
                for (int i = 0; i < 6; i++)
                {
                    Vector2 r = Random.insideUnitCircle * patrolRadius;
                    var q = _home + new Vector3(r.x, 0, r.y);
                    if (NavMesh.SamplePosition(q, out var h, 8f, NavMesh.AllAreas) && NavMesh.CalculatePath(transform.position, h.position, NavMesh.AllAreas, _path) && _path.status == NavMeshPathStatus.PathComplete)
                    { Go(h.position, 1.7f); break; }
                }
            }
            Unstick();
        }

        void Investigate()
        {
            if (Arrived) { state = S.Search; _searchUntil = Time.time + 7f; }
            Unstick();
        }

        void Search()
        {
            if (Time.time > _searchUntil) { state = S.Patrol; _home = transform.position; _awareness *= 0.5f; return; }
            if (Arrived)
            {
                Vector2 r = Random.insideUnitCircle * 10f;
                Go(_lastKnown + new Vector3(r.x, 0, r.y), 2.2f);
            }
            Unstick();
        }

        void Unstick()
        {
            if (_agent.hasPath && _agent.velocity.sqrMagnitude < 0.01f && _agent.remainingDistance > 1f) _stuckT += Time.deltaTime; else _stuckT = 0f;
            if (_stuckT > 3f) { _stuckT = 0f; _agent.ResetPath(); _waitUntil = 0f; }
        }

        void Combat(float dt)
        {
            var p = Target;
            if (p == null) { state = S.Search; _searchUntil = Time.time + 6f; return; }
            Vector3 aimPoint = p.Root.position + Vector3.up * (p.Height * 0.72f);
            Vector3 flat = aimPoint - transform.position; flat.y = 0f;
            float d = flat.magnitude;
            bool seen = _canSeeNow && Time.time - _lastSeen < 0.4f;
            bool hurt = health.Total < health.TotalMax * 0.45f;
            // movement
            if (hurt && Time.time > _coverUntil) { _coverUntil = Time.time + 6f; TryCover(p); }
            else if (!seen && Time.time - _lastSeen > 1.2f) { if (Time.time > _strafeAt) { _strafeAt = Time.time + 1.5f; Go(_lastKnown, 3.4f); } }
            else if (d > 55f && Time.time > _strafeAt) { _strafeAt = Time.time + 2f; Go(transform.position + flat.normalized * 15f, 3.2f); }
            else if (Time.time > _strafeAt)
            {
                _strafeAt = Time.time + Random.Range(1.8f, 3.5f);
                if (Random.value < 0.55f) { Vector3 side = Vector3.Cross(Vector3.up, flat.normalized) * Random.Range(-4f, 4f); Go(transform.position + side, 2.4f); }
                else { _agent.isStopped = true; _agent.ResetPath(); }
            }
            // facing + aim
            if (d > 0.1f) Turn(Quaternion.LookRotation(flat), Vector3.Angle(transform.forward, flat) > 60f ? 520f : 300f);
            Vector3 toAim = aimPoint - _aim.position;
            _aim.rotation = Quaternion.Slerp(_aim.rotation, Quaternion.LookRotation(toAim.normalized), 1f - Mathf.Exp(-10f * dt));
            // shooting
            if (Time.time < _reloadUntil) return;
            if (_magAmmo <= 0) { _reloadUntil = Time.time + _wdef.reloadTime + 0.4f; _magAmmo = _magCap; SFX.PlayAt(SFX.MagOut, transform.position + Vector3.up, 0.8f, 2f, 30f); return; }
            if (!seen || Time.time < _reactAt || Time.time < _nextShot) return;
            float ang = Vector3.Angle(transform.forward, flat);
            if (ang > 25f) return;
            Shoot(p, d);
        }

        void Shoot(IBotTarget p, float d)
        {
            float track = _seenSince > 0f ? Time.time - _seenSince : 0f;
            float err = (0.22f + d * 0.03f) / difficulty;
            err *= Mathf.Lerp(2.4f, 1f, Mathf.Clamp01(track / 2.2f));
            if (p.SpeedNow > 0.5f) err *= 1.35f;
            if (p.Crouched) err *= 0.9f;
            if (_agent.velocity.sqrMagnitude > 0.5f) err *= 1.4f;
            if (RaidOptions.Night && !p.TorchOn) err *= 1.2f;
            Vector3 target = p.Root.position + Vector3.up * (p.Height * (Random.value < 0.12f ? 0.93f : 0.68f));
            target += Random.insideUnitSphere * err;
            Vector3 muzzle = _view != null ? _view.muzzle.position : _eye.position;
            Vector3 from = _eye.position;
            Vector3 shotDir = (target - from).normalized;
            Ballistics.Fire(from, shotDir, _ammo, transform, false);
            AnyShot?.Invoke(this, from, shotDir);
            _magAmmo--;
            float camDist = Ballistics.Listener != null ? Vector3.Distance(Ballistics.Listener.position, muzzle) : d;
            SFX.PlayAt(camDist < 70f ? SFX.ShotFor(_wdef) : SFX.DistantFor(_wdef), muzzle, 1f, 10f, 700f, 0.06f);
            if (_view != null) { _view.flash.SetActive(true); _view.light.enabled = true; CancelInvoke(nameof(HideFlash)); Invoke(nameof(HideFlash), 0.05f); }
            NoiseSystem.Emit(transform.position, 120f, gameObject, true);
            float interval = 60f / _wdef.rpm;
            if (_wdef.fullAuto && d < 60f)
            {
                if (_burstLeft <= 0) _burstLeft = Random.Range(2, d < 20f ? 7 : 4);
                _burstLeft--;
                _nextShot = Time.time + (_burstLeft > 0 ? interval : Random.Range(0.35f, 0.9f));
            }
            else _nextShot = Time.time + Mathf.Max(interval, Random.Range(0.3f, 0.75f) + d * 0.004f);
        }

        void HideFlash() { if (_view != null) { _view.flash.SetActive(false); _view.light.enabled = false; } }

        void TryCover(IBotTarget p)
        {
            Vector3 head = p.Head;
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = Random.insideUnitCircle * 10f;
                var q = transform.position + new Vector3(r.x, 0, r.y);
                if (!NavMesh.SamplePosition(q, out var h, 3f, NavMesh.AllAreas)) continue;
                Vector3 e = h.position + Vector3.up * 1.4f;
                if (Physics.Linecast(e, head, out var hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(p.Root))
                { Go(h.position, 3.8f); return; }
            }
        }

        // walking: face where the agent is going (NavMeshAgent.updateRotation is off)
        void FaceMovement()
        {
            if (_agent == null || !_agent.enabled || _agent.isStopped) return;
            Vector3 v = _agent.desiredVelocity; v.y = 0f;
            if (v.sqrMagnitude < 0.04f) { v = _agent.velocity; v.y = 0f; }
            if (v.sqrMagnitude > 0.04f) Turn(Quaternion.LookRotation(v), 280f);
        }

        void Turn(Quaternion target, float degPerSec)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degPerSec * Time.deltaTime);
        }

        void Animate(float dt)
        {
            if (_bb != null) { _bb.velocity = _agent.velocity; _bb.combat = state == S.Combat; }
            else
            {
                float sp = _agent.velocity.magnitude;
                _walkPhase += sp * dt * 3.2f;
                float sw = Mathf.Sin(_walkPhase) * Mathf.Clamp01(sp / 3.5f) * 32f;
                _legL.localRotation = Quaternion.Euler(sw, 0, 0);
                _legR.localRotation = Quaternion.Euler(-sw, 0, 0);
            }
            if (state != S.Combat)
            {
                Quaternion lowered = transform.rotation * Quaternion.Euler(28f, -25f, 0f);
                _aim.rotation = Quaternion.Slerp(_aim.rotation, lowered, 1f - Mathf.Exp(-5f * dt));
            }
        }

        void DeadAnim(float dt)
        {
            if (_bb != null || _deadT >= 1f) return;
            _deadT = Mathf.Min(1f, _deadT + dt * 2.2f);
            float e = _deadT * _deadT;
            transform.rotation = _deadFrom * Quaternion.Euler(-88f * e, 0, 0);
            if (_deadT >= 1f && Physics.Raycast(transform.position + Vector3.up, Vector3.down, out var h, 3f, ~0, QueryTriggerInteraction.Ignore))
                transform.position = h.point + Vector3.up * 0.12f;
        }
    }

    public static class BotSpawner
    {
        public static List<BotController> SpawnBots(int count, Bounds area, Vector3 playerPos, Transform parent, float difficulty, int seed)
        {
            var rng = new System.Random(seed);
            var list = new List<BotController>();
            var path = new NavMeshPath();
            int tries = 0;
            // hand-placed bot posts of the map first (shuffled), then random NavMesh points
            var posts = new List<MapAnchors.Point>(MapAnchors.Bots);
            for (int i = posts.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = posts[i]; posts[i] = posts[j]; posts[j] = t; }
            float minPlayer = Mathf.Min(70f, area.extents.magnitude * 0.35f);
            foreach (var a in posts)
            {
                if (list.Count >= count) break;
                if (!NavMesh.SamplePosition(a.pos, out var nh, 2.5f, NavMesh.AllAreas)) continue;
                var p = nh.position;
                if (Vector3.Distance(p, playerPos) < Mathf.Max(minPlayer, 90f)) continue;
                bool near = false;
                foreach (var b in list) if (Vector3.Distance(b.transform.position, p) < 25f) { near = true; break; }
                if (near || !RuntimeNavMesh.Reachable(p, playerPos, path)) continue;
                list.Add(BotController.Spawn(p, parent, difficulty, rng));
            }
            while (list.Count < count && tries++ < count * 40)
            {
                if (!RuntimeNavMesh.RandomPoint(area, out var p, 8f, 5)) continue;
                if (Vector3.Distance(p, playerPos) < Mathf.Min(70f, area.extents.magnitude * 0.35f)) continue;
                bool close = false;
                foreach (var b in list) if (Vector3.Distance(b.transform.position, p) < 20f) { close = true; break; }
                if (close) continue;
                if (!RuntimeNavMesh.Reachable(p, playerPos, path)) continue;
                list.Add(BotController.Spawn(p, parent, difficulty, rng));
            }
            Debug.Log("[Severny] Bots spawned: " + list.Count + "/" + count);
            return list;
        }
    }
}
