// Entry point of the game (lives in scene Severny_Main, persists between scenes).
// Owns the menu camera, starts/ends raids (map scene additive or the procedural "Полигон"), command line benchmark.
//   Severny.exe -severnyBenchmark [mapId]   -> loads the map, flies a fixed route, writes CSV + summary, quits.
//   Severny.exe -severnyShots [all]         -> v13: «ТЦ «Волна»» screenshot points (ShotTool), writes PNG + summary next to the build, quits.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Severny
{
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap I;

        [Tooltip("Set automatically when a map scene is played directly: a quick raid starts on that map.")]
        public string quickRaidMapId;

        public bool InRaid { get; private set; }
        public bool Busy { get; private set; }
        public MapDef CurrentMap { get; private set; }
        public Camera MenuCamera => _menuCam;

        Camera _menuCam;
        RaidManager _raid;
        Transform _greybox, _kit, _dress;
        SevernyMapRoot _sceneRoot;
        string _sceneLoadedByUs;
        bool _cmdBenchmark;
        float _menuYaw;

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }

        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            Application.runInBackground = true;
            SFX.Init();
            GameSettings.Apply();
            var prof = SaveSystem.Current;
            prof.GiveInsuranceKitIfBroke();
            EnsureMenuCamera();
            EnvironmentSetup.Apply(EnvironmentSetup.Preset.Menu);
            UIManager.Create();
            if (GetComponent<DebugOverlay>() == null) gameObject.AddComponent<DebugOverlay>();

            string benchMap = null, shots = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-severnyBenchmark", StringComparison.OrdinalIgnoreCase))
                    benchMap = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "severny";
                if (args[i].Equals("-severnyShots", StringComparison.OrdinalIgnoreCase))
                    shots = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "v13";
            }
#if UNITY_EDITOR
            // menu «Severny > Volna: Screenshots» sets this key and enters Play mode
            if (shots == null && UnityEditor.EditorPrefs.HasKey("severny_shots"))
            {
                shots = UnityEditor.EditorPrefs.GetString("severny_shots", "v13");
                UnityEditor.EditorPrefs.DeleteKey("severny_shots");
            }
#endif

            if (shots != null) { UIManager.I.ShowMainMenu(); StartShots(shots.Equals("all", StringComparison.OrdinalIgnoreCase), true); }
            else if (benchMap != null) { _cmdBenchmark = true; StartBenchmark(MapCatalog.Get(benchMap)); }
            else if (!string.IsNullOrEmpty(quickRaidMapId)) StartRaid(MapCatalog.Get(quickRaidMapId), GameSettings.BotAmount, GameSettings.TimeOfDay, GameSettings.Winter, GameSettings.Weather);
            else UIManager.I.ShowMainMenu();
        }

        void EnsureMenuCamera()
        {
            if (_menuCam != null) return;
            var go = new GameObject("MenuCamera");
            go.transform.SetParent(transform, false);
            _menuCam = go.AddComponent<Camera>();
            _menuCam.nearClipPlane = 0.1f; _menuCam.farClipPlane = 1500f; _menuCam.fieldOfView = 60f;
            go.AddComponent<AudioListener>();
            EnvironmentSetup.ConfigureCamera(_menuCam);
            go.transform.SetPositionAndRotation(new Vector3(0f, 14f, -40f), Quaternion.Euler(-6f, 0f, 0f));
            // Any other camera tagged MainCamera in the bootstrap scene is disabled to avoid double rendering.
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c != _menuCam && c.GetComponentInParent<PlayerController>() == null) c.gameObject.SetActive(false);
        }

        void SetMenuCamera(bool on)
        {
            EnsureMenuCamera();
            _menuCam.gameObject.SetActive(on);
        }

        void Update()
        {
            if (_menuCam != null && _menuCam.gameObject.activeSelf)
            {
                _menuYaw += Time.unscaledDeltaTime * 1.5f;
                _menuCam.transform.rotation = Quaternion.Euler(-4f, _menuYaw, 0f);
            }
        }

        // ------------------------------------------------------------------ raid start
        /// <summary>timeOfDay 0 morning,1 day,2 evening,3 night; weather 0 random,1 clear,2 rain/snow,3 fog; seed 0 = random.</summary>
        public void StartRaid(MapDef map, int botsLevel, int timeOfDay, bool winter = false, int weather = 0, int seed = 0)
        {
            if (Busy || InRaid) return;
            _netClient = false;
            StartCoroutine(RaidRoutine(map, botsLevel, timeOfDay, winter, weather, seed, false));
        }

        bool _netClient;

        /// <summary>LAN client: the host started a raid (same map, options and seed); the world comes from the host.</summary>
        public void StartRaidNet(MapDef map, int botsLevel, int timeOfDay, bool winter, int weather, int seed)
        {
            if (Busy || InRaid) return;
            _netClient = true;
            StartCoroutine(RaidRoutine(map, botsLevel, timeOfDay, winter, weather, seed, false));
        }

        public void StartTutorial()
        {
            if (Busy || InRaid) return;
            if (NetSession.Active) { UIManager.I.ShowMessage("Обучение", "Обучение недоступно в сетевой игре. Покиньте лобби."); return; }
            _netClient = false;
            StartCoroutine(RaidRoutine(MapCatalog.Tutorial, 0, RaidOptions.TodDay, false, RaidOptions.WeatherClear, 0, false));
        }

        public void StartBenchmark(MapDef map)
        {
            if (Busy || InRaid) return;
            _netClient = false;
            StartCoroutine(RaidRoutine(map, 0, 1, false, RaidOptions.WeatherClear, 0, true));
        }

        /// <summary>v13: screenshot points of «ТЦ «Волна»» (F7 in the main menu, -severnyShots, menu Severny > Volna: Screenshots).</summary>
        public void StartShots(bool all, bool quit)
        {
            if (Busy || InRaid) return;
            if (NetSession.Active) { UIManager.I.ShowMessage("Фототочки", "Фототочки недоступны в сетевой игре. Покиньте лобби."); return; }
            _netClient = false;
            StartCoroutine(ShotTool.Run(this, all, quit));
        }

        /// <summary>Loads «Полигон» (ТЦ «Волна») for ShotTool: no bots, no HUD, no gameplay; fixed seed.</summary>
        internal IEnumerator RaidForShots(int timeOfDay, int weather)
        {
            if (Busy || InRaid) yield break;
            _netClient = false;
            yield return RaidRoutine(MapCatalog.Polygon, 0, timeOfDay, false, weather, 1337, false, true);
        }

        internal IEnumerator EndRaidForShots()
        {
            Busy = true;
            Cleanup();
            yield return Resources.UnloadUnusedAssets();
            Busy = false;
        }

        /// <summary>Raid start parameters actually used (the LAN host sends them to the clients).</summary>
        public int LastBotsLevel { get; private set; }

        IEnumerator RaidRoutine(MapDef map, int botsLevel, int timeOfDay, bool winter, int weather, int seed, bool benchmark, bool shots = false)
        {
            Busy = true;
            CurrentMap = map;
            LastBotsLevel = botsLevel;
            if (seed == 0) seed = Environment.TickCount & 0x7FFFFFFF; if (seed == 0) seed = 1;
            RaidOptions.Tutorial = map == MapCatalog.Tutorial;
            RaidOptions.Resolve(timeOfDay, winter && !benchmark, benchmark ? RaidOptions.WeatherClear : weather, seed, map.id == MapCatalog.Polygon.id);
            GameInput.GameplayEnabled = false;
            GameInput.SetCursorLocked(false);
            Time.timeScale = 1f;
            var ui = UIManager.I;
            ui.ShowLoading(map.title);
            ui.SetLoading(0.02f, "Подготовка…");
            yield return null;

            _raid = new GameObject("RaidManager").AddComponent<RaidManager>();
            Bounds bounds;
            Transform navRoot;
            bool needNav;

            // ---- 1) map
            MapAnchors.Clear();
            if (RaidOptions.Tutorial)
            {
                ui.SetLoading(0.08f, "Учебный полигон…");
                yield return null;
                _greybox = TutorialMap.Build(_raid.transform, out bounds);
                navRoot = _greybox;
                needNav = true;
            }
            else if (string.IsNullOrEmpty(map.scene))
            {
                bounds = default;
                _kit = null;
                if (map.id == MapCatalog.Polygon.id && PolygonKit.Available)
                {
                    // Blender-built plaza (Resources/PolygonKit); on any error fall back to the procedural greybox
                    ui.SetLoading(0.08f, "Загрузка карты…");
                    yield return null;
                    try { _kit = PolygonKit.Build(_raid.transform, out bounds); }
                    catch (Exception e) { Debug.LogError("[Severny] PolygonKit не загрузился, используется процедурный полигон: " + e); _kit = null; MapAnchors.Clear(); Hud.Notify("ТЦ «Волна» не загрузился — запущен простой полигон (см. Console)", 8f); }
                }
                if (_kit != null) navRoot = _kit;
                else
                {
                    ui.SetLoading(0.08f, "Генерация полигона…");
                    yield return null;
                    _greybox = GreyboxMap.Build(_raid.transform, seed, out bounds);
                    navRoot = _greybox;
                }
                needNav = true;
            }
            else
            {
                _sceneRoot = FindRoot(map.id);
                if (_sceneRoot == null)
                {
                    if (!map.SceneAvailable) { Fail("Сцена карты «" + map.scene + "» не добавлена в Build Settings. Выполните меню Severny > 2."); yield break; }
                    ui.SetLoading(0.05f, "Загрузка карты…");
                    var op = SceneManager.LoadSceneAsync(map.scene, LoadSceneMode.Additive);
                    while (!op.isDone) { ui.SetLoading(0.05f + op.progress * 0.45f, "Загрузка карты… " + Mathf.RoundToInt(op.progress * 100f) + "%"); yield return null; }
                    _sceneLoadedByUs = map.scene;
                    _sceneRoot = FindRoot(map.id);
                    if (_sceneRoot == null) { Fail("В сцене «" + map.scene + "» нет объекта с компонентом SevernyMapRoot (меню Severny > 3)."); yield break; }
                }
                else _sceneRoot.gameObject.SetActive(true);
                if (_sceneRoot.GetComponentInChildren<Renderer>(true) == null)
                {
                    Fail("Карта «" + map.title + "» ещё не размещена: сцена " + map.scene + " пустая.\n\n" +
                         "Скопируйте экспорт из Blender в Assets/SevernyOkrug/Map и выполните меню Severny > 3 Place Map и Severny > 4 Bake NavMesh.\n\n" +
                         "А пока выберите карту «Полигон» — она работает без экспорта.");
                    yield break;
                }
                bounds = _sceneRoot.ComputeBounds();
                navRoot = _sceneRoot.transform;
                needNav = _sceneRoot.navMesh == null;
                // city dress (v09): props, decals, garages, checkpoints, interiors + loot / bot / spawn / exit anchors (Resources/SevernyDress)
                if (map.id == MapCatalog.Severny.id && SevernyDress.Available)
                {
                    ui.SetLoading(0.5f, "Детализация города…");
                    yield return null;
                    try { _dress = SevernyDress.Build(_raid.transform, _sceneRoot.transform, needNav || _sceneRoot.dressVersion != SevernyDress.Version); }
                    catch (Exception e) { Debug.LogError("[Severny] Детализация города не загрузилась: " + e); SevernyDress.Unload(); _dress = null; MapAnchors.Clear(); }
                }
            }

            // ---- 2) navmesh
            if (needNav)
            {
                ui.SetLoading(0.5f, "Построение NavMesh…");
                yield return RuntimeNavMesh.Build(navRoot, bounds, t => ui.SetLoading(0.5f + t * 0.4f, "Построение NavMesh… " + Mathf.RoundToInt(t * 100f) + "%"),
                                                  _kit != null ? PolygonKit.NoWalkRoot : null);
            }
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length < 3) { Fail("NavMesh пуст: ботам и спавну негде ходить. Для карты выполните меню Severny > 4 (Bake NavMesh)."); yield break; }

            // ---- 2b) winter: snow on the ground, roofs, cars; frosted trees
            if (RaidOptions.Winter)
            {
                Transform snowRoot = _kit != null ? _kit : _greybox != null ? _greybox : _sceneRoot != null ? _sceneRoot.transform : null;
                yield return Winter.Apply(snowRoot, _kit != null || _greybox != null, t => ui.SetLoading(0.9f + t * 0.02f, "Снег… " + Mathf.RoundToInt(t * 100f) + "%"));
                if (_dress != null) Winter.ApplyExtra(_dress);
            }

            // ---- 3) environment + spawns
            ui.SetLoading(0.92f, "Расстановка…");
            yield return null;
            var tod = RaidOptions.TimeOfDay;
            var preset = tod == 0 ? EnvironmentSetup.Preset.Morning : tod == 2 ? EnvironmentSetup.Preset.Evening : tod == 3 ? EnvironmentSetup.Preset.Night : EnvironmentSetup.Preset.Day;
            EnvironmentSetup.Apply(preset, map.fog);
            if (_kit != null) PolygonKit.ApplyEnvironment(preset);
            EnvironmentSetup.ApplyModifiers(RaidOptions.Night, RaidOptions.Winter, RaidOptions.ResolvedWeather);
            // ---- 3a) v13 «ТЦ «Волна»»: calibrated sky / sun / SH ambient / grade (Resources/VolnaLook); v12 lighting stays the fallback
            if (_kit != null)
            {
                bool look = false;
                try { look = VolnaLook.Apply(preset, RaidOptions.Night, RaidOptions.Winter, RaidOptions.ResolvedWeather); }
                catch (Exception e) { Debug.LogError("[Severny] VolnaLook (v13) не применён, остаётся свет v12: " + e); VolnaLook.Restore(); look = false; }
                if (look) { ui.SetLoading(0.925f, "Калибровка света…"); yield return VolnaLook.Calibrate(); }
                else Debug.Log("[Severny] ТЦ «Волна»: свет v12 (" + VolnaLook.BalanceLine() + ")");
            }
            if ((_kit != null || _dress != null) && MapAnchors.Zones.Count > 0) _raid.gameObject.AddComponent<InteriorZones>();
            // ---- 3b) v12 graphics: street lights + lit windows (city), smoke / mist / wet streets / crows, reflection probes
            CityLights.LastInfo = AtmosphereFX.LastInfo = ReflectionGrid.LastInfo = "нет";
            if (map.id == MapCatalog.Severny.id && _sceneRoot != null)
            {
                try { CityLights.Create(_raid.transform, _sceneRoot.transform); }
                catch (Exception e) { Debug.LogError("[Severny] Огни города (v12) не созданы: " + e); }
            }
            if (!RaidOptions.Tutorial)
            {
                try
                {
                    var nb = new Bounds(tri.vertices[0], Vector3.zero);
                    foreach (var v in tri.vertices) nb.Encapsulate(v);
                    Transform world = _kit != null ? _kit : _greybox != null ? _greybox : _sceneRoot != null ? _sceneRoot.transform : null;
                    AtmosphereFX.Create(_raid.transform, nb, world, _dress);
                }
                catch (Exception e) { Debug.LogError("[Severny] Атмосфера (v12) не создана: " + e); }
                ui.SetLoading(0.93f, "Детализация поверхностей всей карты…");
                Transform detailWorld = _kit != null ? _kit : _greybox != null ? _greybox : _sceneRoot != null ? _sceneRoot.transform : null;
                yield return V14SurfaceDetail.Build(_raid.transform, detailWorld);
                ui.SetLoading(0.93f, "Отражения…");
                yield return ReflectionGrid.Build(_raid.transform, tri.vertices, t => ui.SetLoading(0.93f + t * 0.02f, "Отражения… " + Mathf.RoundToInt(t * 100f) + "%"));
            }
            int botCount = benchmark || shots ? 0 : Mathf.RoundToInt(map.bots[Mathf.Clamp(botsLevel, 0, 2)] * RaidOptions.BotMultiplier);
            if (_netClient)
            {
                // LAN client: tell the host we are ready, wait for the world snapshot
                NetSession.I?.SendLoaded();
                float t0 = Time.realtimeSinceStartup;
                while (NetSession.IsClient && NetSession.I.PendingWorld == null && !NetSession.I.RaidAborted && Time.realtimeSinceStartup - t0 < 180f)
                {
                    ui.SetLoading(0.95f, "Ожидание хоста… " + Mathf.RoundToInt(Time.realtimeSinceStartup - t0) + " с");
                    yield return null;
                }
                var w = NetSession.IsClient && !NetSession.I.RaidAborted ? NetSession.I.TakeWorld() : null;
                if (w == null)
                {
                    Fail(NetSession.IsClient && NetSession.I.RaidAborted ? NetSession.I.AbortReason :
                         NetSession.IsClient ? "Хост не прислал мир рейда за 3 минуты." : "Соединение с хостом потеряно.");
                    yield break;
                }
                if (!_raid.BeginClient(map, bounds, w)) { Fail("Не удалось войти в рейд хоста: точка появления не найдена на NavMesh (разные версии карты?)."); yield break; }
            }
            else if (!_raid.Begin(map, bounds, botCount, 1f, seed)) { Fail("Не удалось разместить игрока на NavMesh (см. Console)."); yield break; }
            if (_greybox != null) StaticBatchingUtility.Combine(_greybox.gameObject);
            if (_kit != null) StaticBatchingUtility.Combine(_kit.gameObject);      // fewer draw calls (props share materials)
            if (!benchmark) WeatherFX.Create(_raid.transform);
            if (RaidOptions.Tutorial) _raid.gameObject.AddComponent<TutorialDirector>();
            if (NetSession.Active && !benchmark && !shots && !RaidOptions.Tutorial) NetSession.I.OnLocalRaidBegun();

            SetMenuCamera(false);
            InRaid = true; Busy = false;
            if (shots) yield break;          // ShotTool drives the camera; no HUD, no gameplay input
            ui.ShowHud();
            if (benchmark)
            {
                var b = _raid.gameObject.AddComponent<AutoBenchmark>();
                b.Begin(bounds, map, _cmdBenchmark);
            }
            else
            {
                GameInput.GameplayEnabled = true;
                GameInput.SetCursorLocked(true);
                if (!RaidOptions.Tutorial)
                    Hud.Notify(map.title + " · " + RaidOptions.Describe() + (NetSession.Active ? " · по сети: игроков " + NetSession.I.Peers.Count : "") + " — найдите выход (O — список выходов)" +
                               (RaidOptions.Night ? "\nНочь: врагов больше, лут богаче. Фонарь — L." : ""), 5f);
            }
        }

        static SevernyMapRoot FindRoot(string id)
        {
            foreach (var r in FindObjectsByType<SevernyMapRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.mapId == id) return r;
            return null;
        }

        void Fail(string msg)
        {
            Debug.LogError("[Severny] " + msg);
            if (NetSession.Active) NetSession.I.OnLocalRaidFailed();
            Cleanup();
            Busy = false;
            UIManager.I.ShowMessage("Рейд не запущен", msg);
        }

        // ------------------------------------------------------------------ back to menu
        public void ReturnToMenu(string msgTitle = null, string msg = null)
        {
            if (Busy) return;
            StartCoroutine(ReturnRoutine(msgTitle, msg));
        }

        IEnumerator ReturnRoutine(string msgTitle, string msg)
        {
            Busy = true;
            Cleanup();
            if (_sceneLoadedByUs != null)
            {
                var op = SceneManager.UnloadSceneAsync(_sceneLoadedByUs);
                _sceneLoadedByUs = null;
                if (op != null) while (!op.isDone) yield return null;
                _sceneRoot = null;
            }
            yield return Resources.UnloadUnusedAssets();
            Busy = false;
            if (_cmdBenchmark) { Application.Quit(); yield break; }
            UIManager.I.ShowMainMenu();
            if (msg != null) UIManager.I.ShowMessage(msgTitle, msg);
        }

        void Cleanup()
        {
            if (NetSession.I != null) NetSession.I.OnLocalRaidCleanup();
            InRaid = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            GameInput.GameplayEnabled = false;
            GameInput.SetCursorLocked(false);
            NavTester.Stop();
            if (_raid != null) { _raid.gameObject.SetActive(false); Destroy(_raid.gameObject); _raid = null; }
            if (_kit != null) PolygonKit.Unload();
            if (_dress != null) SevernyDress.Unload();
            _greybox = null; _kit = null; _dress = null;   // children of the RaidManager object
            GfxSwap.RestoreAll();                          // v12 lamp / wet material swaps (before Winter: reverse order)
            Winter.Restore();
            RaidOptions.Reset();
            SFX.SnowFootsteps = false;
            MapAnchors.Clear();
            if (_sceneRoot != null && _sceneLoadedByUs == null) _sceneRoot.gameObject.SetActive(false); // scene opened in the editor: keep, hide
            RuntimeNavMesh.Clear();
            Ballistics.ClearAll();
            PlayerController.Local = null;
            Hud.Prompt(null); Hud.Progress(null, 0f);
            SetMenuCamera(true);
            VolnaLook.Restore();                           // v13 sky / ambient / reflection / grade overrides
            EnvironmentSetup.Apply(EnvironmentSetup.Preset.Menu);
        }

        void OnApplicationQuit() { if (SaveSystem.Loaded) SaveSystem.Save(); }
    }
}
