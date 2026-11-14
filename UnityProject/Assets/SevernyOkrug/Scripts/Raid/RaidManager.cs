// Raid lifecycle: spawns (player, exits, loot, bots), timer, extraction, death/MIA, results + profile update.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Severny
{
    public class RaidResult
    {
        public RaidManager.Outcome outcome;
        public string map, cause, exitName;
        public float seconds;
        public int kills, xp, foundValue, lostValue, shots, hits;
    }

    public class RaidManager : MonoBehaviour
    {
        public enum Outcome { None, Survived, Killed, MIA, Left }
        public static RaidManager I;

        public MapDef map;
        public Bounds bounds;
        public Transform runtimeRoot;
        public float timeLeft, raidLength;
        public bool Running { get; private set; }
        public readonly List<ExtractionPoint> exits = new List<ExtractionPoint>();
        public PlayerController player;
        public int kills;
        public string extractName;
        public float extractLeft;
        float _startTime;
        int _startValue, _startShots, _startHits;
        bool _ended;
        /// <summary>Training raid: separate empty loadout, no losses, one-time reward.</summary>
        public bool IsTutorial { get; private set; }
        public const int TutorialReward = 50000;
        /// <summary>Where the local player started (LAN: the clients spawn next to the host).</summary>
        public Vector3 spawnPos;
        public float spawnYaw;
        public bool IsNetClient { get; private set; }
        /// <summary>The local raid result is decided (before the losses are applied). LAN: tells the others.</summary>
        public static event System.Action<Outcome> LocalRaidEnding;

        static readonly string[] ExitNames = { "Блокпост", "Гаражи у насыпи", "Пролом в заборе", "Ж/д переезд", "Автобусная остановка", "Старая котельная", "КПП завода", "Мост через канал" };

        void Awake()
        {
            I = this;
            runtimeRoot = new GameObject("RAID_RUNTIME").transform;
            runtimeRoot.SetParent(transform, false);
        }

        void OnDestroy() { if (I == this) I = null; }

        public bool Begin(MapDef m, Bounds b, int botCount, float difficulty, int seed)
        {
            map = m; bounds = b;
            if (RaidOptions.Tutorial) return BeginTutorial(m, b);
            var rng = new System.Random(seed);
            // 1) player spawn near a random edge
            int side = rng.Next(4);
            Vector3 spawn = b.center;
            float spawnYaw = float.NaN;
            bool haveSpawn = false;
            if (MapAnchors.Has)
            {
                // hand-placed spawn points (PolygonKit map)
                foreach (int i in Shuffled(MapAnchors.Spawns.Count, rng))
                {
                    var a = MapAnchors.Spawns[i];
                    if (!NavMesh.SamplePosition(a.pos, out var nh, 3f, NavMesh.AllAreas)) continue;
                    spawn = nh.position; spawnYaw = a.yaw; haveSpawn = true; break;
                }
            }
            if (!haveSpawn && !EdgePoint(b, side, 18f, 70f, rng, out spawn) && !RuntimeNavMesh.RandomPoint(b, out spawn, 10f, 60))
            {
                Debug.LogError("[Severny] No NavMesh point for player spawn");
                return false;
            }
            // 2) exits on the other sides (+ a far point on the same side), 2-3 active
            var names = new List<string>(ExitNames);
            var path = new NavMeshPath();
            var candidates = new List<(Vector3, string)>();
            if (MapAnchors.Has)
            {
                // big maps: exits well away from the spawn (relaxed step by step if too few qualify)
                float far = Mathf.Clamp(new Vector2(b.extents.x, b.extents.z).magnitude * 0.45f, 55f, 150f);
                foreach (float minD in new[] { far, far * 0.65f, 55f })
                {
                    candidates.Clear();
                    foreach (int i in Shuffled(MapAnchors.Exits.Count, rng))
                    {
                        var e = MapAnchors.Exits[i];
                        if (!NavMesh.SamplePosition(e.pos, out var nh, 5f, NavMesh.AllAreas)) continue;
                        if (Vector3.Distance(nh.position, spawn) < minD) continue;
                        if (!RuntimeNavMesh.Reachable(spawn, nh.position, path)) continue;
                        candidates.Add((nh.position, e.title));
                    }
                    if (candidates.Count >= 2) break;
                }
                if (candidates.Count < 2) Debug.LogWarning("[Severny] Map exits: only " + candidates.Count + " anchor exit(s) reachable, adding random ones");
            }
            for (int k = 1; k <= 4 && (!MapAnchors.Has || candidates.Count < 2); k++)
            {
                int s = (side + k) % 4;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    if (!EdgePoint(b, s, 12f, 30f, rng, out var ep)) continue;
                    if (Vector3.Distance(ep, spawn) < b.extents.magnitude * 0.45f) continue;
                    if (!RuntimeNavMesh.Reachable(spawn, ep, path)) continue;
                    int ni = rng.Next(names.Count);
                    candidates.Add((ep, names[ni])); names.RemoveAt(ni);
                    break;
                }
            }
            if (candidates.Count == 0)
            {
                for (int attempt = 0; attempt < 40 && candidates.Count < 2; attempt++)
                    if (RuntimeNavMesh.RandomPoint(b, out var ep, 10f, 5) && Vector3.Distance(ep, spawn) > 60f && RuntimeNavMesh.Reachable(spawn, ep, path))
                    { int ni = rng.Next(names.Count); candidates.Add((ep, names[ni])); names.RemoveAt(ni); }
            }
            int activeCount = Mathf.Min(candidates.Count, rng.NextDouble() < 0.5 ? 2 : 3);
            for (int i = candidates.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = candidates[i]; candidates[i] = candidates[j]; candidates[j] = t; }
            for (int i = 0; i < activeCount; i++) exits.Add(ExtractionPoint.Create(candidates[i].Item2, candidates[i].Item1, runtimeRoot));
            // 3) player
            var prof = SaveSystem.Current;
            Vector3 look = b.center - spawn; look.y = 0f;
            float yaw = !float.IsNaN(spawnYaw) ? spawnYaw : Quaternion.LookRotation(look.sqrMagnitude > 1f ? look : Vector3.forward).eulerAngles.y;
            player = PlayerController.Spawn(spawn + Vector3.up * 0.05f, yaw, prof.loadout, runtimeRoot);
            player.MinSafeY = b.min.y - 25f;
            spawnPos = spawn; spawnYaw = yaw;
            // 4) loot + bots
            float area = b.size.x * b.size.z;
            if (MapAnchors.HasLoot)
            {
                LootSpawner.SpawnFromAnchors(runtimeRoot, rng.Next());
                LootSpawner.Spawn(b, 12, 10, runtimeRoot, rng.Next());       // a few random extras
            }
            else LootSpawner.Spawn(b, Mathf.Clamp(Mathf.RoundToInt(area / 2200f), 18, 90), Mathf.Clamp(Mathf.RoundToInt(area / 6000f), 8, 40), runtimeRoot, rng.Next());
            BotSpawner.SpawnBots(botCount, b, spawn, runtimeRoot, difficulty, rng.Next());
            // 5) timer
            raidLength = timeLeft = m.minutes * 60f;
            _startTime = Time.time;
            _startValue = prof.loadout.TotalValue();
            _startShots = Ballistics.ShotsFired; _startHits = Ballistics.Hits;
            kills = 0; _ended = false;
            Running = true;
            Debug.Log("[Severny] Raid started on " + m.title + ": exits " + exits.Count + ", bots " + BotController.All.Count + ", bounds " + b.size);
            return true;
        }

        /// <summary>LAN client: the world comes from the host (exits, loot, copies of the bots); we spawn next to the host.</summary>
        public bool BeginClient(MapDef m, Bounds b, NetWorld w)
        {
            map = m; bounds = b;
            IsNetClient = true;
            int me = NetSession.I != null ? Mathf.Max(1, NetSession.I.MyId) : 1;
            float ang = me * 1.9f;
            Vector3 spawn = w.spawnPos;
            Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (1.4f + 0.3f * me);
            if (NavMesh.SamplePosition(w.spawnPos + off, out var nh, 2.5f, NavMesh.AllAreas) && RuntimeNavMesh.Reachable(w.spawnPos, nh.position, new NavMeshPath())) spawn = nh.position;
            else if (NavMesh.SamplePosition(w.spawnPos, out nh, 5f, NavMesh.AllAreas)) spawn = nh.position;
            else { Debug.LogError("[Severny] LAN: host spawn point is not on this NavMesh"); return false; }
            foreach (var e in w.exits) exits.Add(ExtractionPoint.Create(e.Key, e.Value, runtimeRoot));
            var prof = SaveSystem.Current;
            player = PlayerController.Spawn(spawn + Vector3.up * 0.05f, w.spawnYaw, prof.loadout, runtimeRoot);
            player.MinSafeY = b.min.y - 25f;
            spawnPos = spawn; spawnYaw = w.spawnYaw;
            w.Build(runtimeRoot);
            raidLength = Mathf.Max(60f, w.raidLength);
            timeLeft = Mathf.Clamp(w.timeLeft, 30f, raidLength);
            _startTime = Time.time;
            _startValue = prof.loadout.TotalValue();
            _startShots = Ballistics.ShotsFired; _startHits = Ballistics.Hits;
            kills = 0; _ended = false;
            Running = true;
            Debug.Log("[Severny] LAN raid joined on " + m.title + ": exits " + exits.Count + ", bots " + w.bots.Count + ", containers " + w.containers.Count);
            return true;
        }

        /// <summary>LAN client: the host closed the world (or the connection dropped) — we keep our gear.</summary>
        public void EndByHost(string reason)
        {
            if (Running) End(Outcome.Survived, reason, null);
        }

        /// <summary>Tutorial: the player starts with nothing (training kit comes from the crate on the course).</summary>
        bool BeginTutorial(MapDef m, Bounds b)
        {
            IsTutorial = true;
            Vector3 spawn = TutorialMap.SpawnPos;
            if (NavMesh.SamplePosition(spawn + Vector3.up, out var nh, 4f, NavMesh.AllAreas)) spawn = nh.position;
            player = PlayerController.Spawn(spawn + Vector3.up * 0.05f, 0f, new Loadout(), runtimeRoot);
            player.MinSafeY = b.min.y - 25f;
            raidLength = timeLeft = m.minutes * 60f;
            _startTime = Time.time;
            _startValue = 0;
            _startShots = Ballistics.ShotsFired; _startHits = Ballistics.Hits;
            kills = 0; _ended = false;
            Running = true;
            Debug.Log("[Severny] Tutorial started");
            return true;
        }

        void EndTutorial(Outcome o, string cause)
        {
            var prof = SaveSystem.Current;
            prof.tutorialOffered = true;
            string title, msg;
            if (o == Outcome.Survived)
            {
                title = "ОБУЧЕНИЕ ПРОЙДЕНО";
                if (!prof.tutorialDone)
                {
                    prof.tutorialDone = true;
                    prof.roubles += TutorialReward;
                    bool all = GiveTutorialKit(prof.stash);
                    msg = "Отлично, основы освоены!\n\nНаграда: " + UIFactory.Money(TutorialReward) +
                          " и набор в схрон: бронежилет, шлем, магазины, патроны, бинты и аптечки." +
                          (all ? "" : "\n(Часть набора не поместилась в схрон.)") +
                          "\n\nУчебное снаряжение сдано. Соберите комплект в «Снаряжении» и отправляйтесь в рейд. Удачи!";
                }
                else msg = "Повторное прохождение — без награды. Учебное снаряжение сдано.";
            }
            else if (o == Outcome.Killed)
            {
                title = "ОБУЧЕНИЕ: ПОГИБ";
                msg = (string.IsNullOrEmpty(cause) ? "" : cause + "\n\n") + "В обучении ничего не теряется. Попробуйте ещё раз: главное меню → «Обучение».";
            }
            else
            {
                title = "ОБУЧЕНИЕ ПРЕРВАНО";
                msg = "Ничего не потеряно. Вернуться к обучению можно из главного меню.";
            }
            SaveSystem.Save();
            Debug.Log("[Severny] Tutorial ended: " + o);
            if (GameBootstrap.I != null) GameBootstrap.I.ReturnToMenu(title, msg);
        }

        /// <summary>One-time tutorial reward kit (goes to the stash). Returns false if something did not fit.</summary>
        static bool GiveTutorialKit(InventoryGrid stash)
        {
            bool ok = true;
            void Add(ItemInstance it) { if (it.Def != null && !stash.AutoPlace(it)) ok = false; }
            Add(ItemInstance.Create("vest3"));
            Add(ItemInstance.Create("helm2"));
            for (int i = 0; i < 2; i++) { var m = ItemInstance.Create("mag_av74"); m.ammo = 30; m.ammoId = "a545"; Add(m); }
            Add(ItemInstance.Create("a545", 60));
            Add(ItemInstance.Create("a918", 30));
            for (int i = 0; i < 3; i++) Add(ItemInstance.Create("bandage"));
            Add(ItemInstance.Create("medkit"));
            Add(ItemInstance.Create("medkit"));
            Add(ItemInstance.Create("painkill"));
            return ok;
        }

        static int[] Shuffled(int n, System.Random rng)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;
            for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = a[i]; a[i] = a[j]; a[j] = t; }
            return a;
        }

        /// <summary>NavMesh point inside the band [inset, inset+depth] from the given side (0=+Z,1=+X,2=-Z,3=-X).</summary>
        static bool EdgePoint(Bounds b, int side, float inset, float depth, System.Random rng, out Vector3 p)
        {
            for (int i = 0; i < 25; i++)
            {
                float u = (float)rng.NextDouble(), d = inset + (float)rng.NextDouble() * depth;
                Vector3 q;
                switch (side)
                {
                    case 0: q = new Vector3(Mathf.Lerp(b.min.x + inset, b.max.x - inset, u), b.max.y, b.max.z - d); break;
                    case 1: q = new Vector3(b.max.x - d, b.max.y, Mathf.Lerp(b.min.z + inset, b.max.z - inset, u)); break;
                    case 2: q = new Vector3(Mathf.Lerp(b.min.x + inset, b.max.x - inset, u), b.max.y, b.min.z + d); break;
                    default: q = new Vector3(b.min.x + d, b.max.y, Mathf.Lerp(b.min.z + inset, b.max.z - inset, u)); break;
                }
                if (Physics.Raycast(q + Vector3.up * 5f, Vector3.down, out var hit, b.size.y + 60f, ~0, QueryTriggerInteraction.Ignore)) q = hit.point;
                if (NavMesh.SamplePosition(q, out var nh, 5f, NavMesh.AllAreas)) { p = nh.position; return true; }
            }
            p = b.center; return false;
        }

        void Update()
        {
            if (!Running) return;
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f) { timeLeft = 0f; End(Outcome.MIA, "Время рейда истекло"); }
            if (GameInput.Down(GKey.NavTest)) NavTester.Toggle(bounds, runtimeRoot);
        }

        public void SetExtractInfo(string name, float left) { extractName = name; extractLeft = left; }

        public void Extract(string name) { if (Running) End(Outcome.Survived, null, name); }

        public void LeaveRaid() { if (Running) End(Outcome.Left, "Покинул рейд"); }

        public void OnBotKilled(BotController b, bool byPlayer) { if (byPlayer) kills++; if (byPlayer) Hud.Notify("Противник нейтрализован", 1.5f); }

        public void OnItemPicked(ItemInstance it) { }

        public void OnPlayerDied(DamageInfo di)
        {
            if (!Running) return;
            string cause = di.cause == "bleed" ? "Кровопотеря" : di.cause == "fall" ? "Падение" :
                           di.attacker != null ? "Убит: " + (di.attacker.GetComponent<BotController>() != null ? "Мародёр" : di.attacker.name) + " (" + Health.PartNames[(int)di.part] + ")" : "Погиб";
            Running = false;
            StartCoroutine(EndAfter(Outcome.Killed, cause, 3f));
        }

        IEnumerator EndAfter(Outcome o, string cause, float delay)
        {
            yield return new WaitForSeconds(delay);
            End(o, cause);
        }

        void End(Outcome o, string cause, string exitName = null)
        {
            if (_ended) return;
            _ended = true;
            Running = false;
            GameInput.GameplayEnabled = false;
            GameInput.SetCursorLocked(false);
            if (IsTutorial) { EndTutorial(o, cause); return; }
            try { LocalRaidEnding?.Invoke(o); } catch (System.Exception e) { Debug.LogException(e); }
            var prof = SaveSystem.Current;
            int endValue = prof.loadout.TotalValue();
            var r = new RaidResult
            {
                outcome = o, map = map != null ? map.title : "?", cause = cause, exitName = exitName,
                seconds = Time.time - _startTime, kills = kills,
                shots = Ballistics.ShotsFired - _startShots, hits = Ballistics.Hits - _startHits
            };
            r.xp = Mathf.RoundToInt(r.seconds / 60f * 20f) + kills * 300;
            prof.raids++;
            prof.kills += kills;
            if (o == Outcome.Survived)
            {
                prof.survived++;
                r.foundValue = Mathf.Max(0, endValue - _startValue);
                r.xp += 600 + r.foundValue / 200;
            }
            else
            {
                if (o == Outcome.Killed) prof.kia++; else prof.mia++;
                r.lostValue = endValue - prof.loadout.secure.Value();
                prof.loadout.LoseAllExceptSecure();
            }
            prof.xp += r.xp;
            SaveSystem.Save();
            Debug.Log("[Severny] Raid ended: " + o + " (" + cause + "), " + r.seconds.ToString("F0") + "s, kills " + kills);
            UIManager.I?.ShowResults(r);
        }
    }
}
