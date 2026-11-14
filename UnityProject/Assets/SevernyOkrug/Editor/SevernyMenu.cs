// Menu "Severny": 1 Setup project, 2 Create scenes (+ Build Settings). Map steps are in SevernyMapTools.cs.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Severny.EditorTools
{
    [InitializeOnLoad]
    public static class SevernyMenu
    {
        public const string Root = "Assets/SevernyOkrug";
        public const string AssetsSO = Root + "/Resources/SevernyGameAssets.asset";
        public const string MainScene = Root + "/Scenes/Severny_Main.unity";
        public const string RaidScene = Root + "/Scenes/Raid_Severny.unity";

        static SevernyMenu()
        {
            if (Application.isBatchMode) return;   // batch builds call SevernyBuild.Prepare explicitly
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool("severny_first_run_asked", false)) return;
                SessionState.SetBool("severny_first_run_asked", true);
                if (File.Exists(AssetsSO) && File.Exists(MainScene)) return;
                if (EditorUtility.DisplayDialog("Северный Округ",
                        "Пакет SevernyOkrug найден, но проект ещё не настроен.\n\nВыполнить настройку сейчас?\n(Severny > 1 Setup Project и Severny > 2 Create Scenes)",
                        "Да, настроить", "Позже"))
                { Setup(); CreateScenes(); }
            };
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Material MatAsset(string path, Shader sh)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        [MenuItem("Severny/1 Setup Project", false, 1)]
        public static void Setup() { SetupCore(true); }

        /// <summary>Project setup; interactive = show dialogs (false for batch builds). Returns false when URP is missing.</summary>
        public static bool SetupCore(bool interactive)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var sky = Shader.Find("Skybox/Procedural");
            if (lit == null || unlit == null)
            {
                Debug.LogError("[Severny] URP (Universal Render Pipeline/Lit) not found — create the project from the Universal 3D (URP) template.");
                if (interactive) EditorUtility.DisplayDialog("Северный Округ", "Не найден URP (Universal Render Pipeline/Lit).\nСоздайте проект по шаблону «Universal 3D» (URP) в Unity 6 и импортируйте пакет туда.", "OK");
                return false;
            }
            EnsureFolder(Root + "/Materials"); EnsureFolder(Root + "/Resources"); EnsureFolder(Root + "/Scenes"); EnsureFolder(Root + "/Map");
            var so = AssetDatabase.LoadAssetAtPath<SevernyGameAssets>(AssetsSO);
            if (so == null) { so = ScriptableObject.CreateInstance<SevernyGameAssets>(); AssetDatabase.CreateAsset(so, AssetsSO); }
            so.litTemplate = MatAsset(Root + "/Materials/M_Template_Lit.mat", lit);
            so.unlitTemplate = MatAsset(Root + "/Materials/M_Template_Unlit.mat", unlit);
            if (sky != null) so.skyboxTemplate = MatAsset(Root + "/Materials/M_Template_Sky.mat", sky);
            so.uiFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Resources/SevernyUI/Fonts/LiberationSans-Regular.ttf");
            so.uiFontBold = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Resources/SevernyUI/Fonts/LiberationSans-Bold.ttf");
            foreach (var p in new[] { Root + "/Resources/SevernyUI/logo.png", Root + "/Resources/SevernyUI/menu_bg.jpg" })
            {
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                bool ch = false;
                if (ti.mipmapEnabled) { ti.mipmapEnabled = false; ch = true; }
                if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; ch = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; ch = true; }
                if (ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; ch = true; }
                if (p.EndsWith(".png") && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ch = true; }
                if (ch) ti.SaveAndReimport();
            }
            so.logo = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/SevernyUI/logo.png");
            so.menuBackground = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/SevernyUI/menu_bg.jpg");
            EditorUtility.SetDirty(so);

            // player / quality
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.bakeCollisionMeshes = true;   // cook MeshColliders at build time (faster map load in builds)
            if (PlayerSettings.productName == "My project" || string.IsNullOrEmpty(PlayerSettings.productName)) PlayerSettings.productName = "Severny Okrug";
            int tweaked = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null) continue;
                a.shadowDistance = 150f; a.shadowCascadeCount = 4;
                EditorUtility.SetDirty(a); tweaked++;
            }
            AssetDatabase.SaveAssets();
            string gfx = SevernyGraphicsEditor.ApplyAll(false) ? "графика v12: Forward+, SSAO, мягкие тени, отражения" : "графика v12: см. Console";   // v12
            string kit = PolygonKitEditor.SetupKit();
            Debug.Log("[Severny] Setup done: SevernyGameAssets, templates, Linear colour space, frame timing stats, URP assets tweaked: " + tweaked + "; " + kit);
            if (interactive) EditorUtility.DisplayDialog("Северный Округ", "Настройка выполнена.\n\n• SevernyGameAssets + шаблоны материалов URP\n• Linear color space, Frame Timing Stats\n• URP: тени 150 м, 4 каскада (" + tweaked + " ассета)\n• " + gfx + "\n• " + kit + "\n\nДальше: Severny > 2 Create Scenes.", "OK");
            return true;
        }

        [MenuItem("Severny/2 Create Scenes", false, 2)]
        public static void CreateScenes() { CreateScenesCore(true); }

        public static void CreateScenesCore(bool interactive)
        {
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(Root + "/Scenes");
            if (!File.Exists(MainScene))
            {
                var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
                EditorSceneManager.SaveScene(s, MainScene);
            }
            if (!File.Exists(RaidScene))
            {
                var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("MAP_ROOT");
                root.AddComponent<SevernyMapRoot>().mapId = "severny";
                new GameObject("BACKDROP").transform.SetParent(root.transform, false);
                EditorSceneManager.SaveScene(s, RaidScene);
            }
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(x => x.path == MainScene || x.path == RaidScene);
            list.Insert(0, new EditorBuildSettingsScene(MainScene, true));
            list.Insert(1, new EditorBuildSettingsScene(RaidScene, true));
            EditorBuildSettings.scenes = list.ToArray();
            EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            Debug.Log("[Severny] Scenes ready: " + MainScene + ", " + RaidScene);
            if (interactive) EditorUtility.DisplayDialog("Северный Округ", "Сцены созданы и добавлены в Build Settings:\n• Severny_Main (меню, запуск игры)\n• Raid_Severny (карта)\n\nУже можно нажать Play: карта «Полигон» работает без экспорта.\nДля города: скопируйте экспорт в Assets/SevernyOkrug/Map и выполните Severny > 3.", "OK");
        }

        [MenuItem("Severny/Open Main Scene", false, 40)]
        public static void OpenMain()
        {
            if (!File.Exists(MainScene)) { CreateScenes(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
        }

        [MenuItem("Severny/Open Raid Scene", false, 41)]
        public static void OpenRaid()
        {
            if (!File.Exists(RaidScene)) { CreateScenes(); }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(RaidScene, OpenSceneMode.Single);
        }

        [MenuItem("Severny/Show Save + Logs Folder", false, 60)]
        public static void ShowData() { EditorUtility.RevealInFinder(Application.persistentDataPath); }
    }
}
