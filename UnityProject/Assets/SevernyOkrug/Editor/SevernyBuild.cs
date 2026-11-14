// Player builds in one step.
//   Menu  Severny > 6 Build Windows (.exe)  — asks for a folder, runs setup if the project is not set up yet, builds.
//   Batch Unity -batchmode -projectPath <p> -executeMethod Severny.EditorTools.SevernyBuild.BuildWindowsBatch [-severnyOut <dir/SevernyOkrug.exe>]
//         (BuildLinuxBatch for a Linux player; PrepareBatch only sets the project up). Exit code 0 = success.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny.EditorTools
{
    public static class SevernyBuild
    {
        public const string ExeName = "SevernyOkrug.exe";
        static bool _snowBakedNow;
        const string VariantsDir = SevernyMenu.Root + "/Resources/SevernyVariants";

        [MenuItem("Severny/6 Build Windows (.exe)", false, 7)]
        public static void BuildWindowsMenu()
        {
            string dir = EditorUtility.SaveFolderPanel("Папка для сборки игры (Windows)", Path.GetFullPath("Builds"), "SevernyOkrug_Windows");
            if (string.IsNullOrEmpty(dir)) return;
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!Prepare(false, true)) return;
            var rep = Build(BuildTarget.StandaloneWindows64, Path.Combine(dir, ExeName));
            bool ok = rep != null && rep.summary.result == BuildResult.Succeeded;
            EditorUtility.DisplayDialog("Северный Округ", ok
                ? "Сборка готова:\n" + dir + "\n\nЗапуск: " + ExeName + "\nРазмер: " + (rep.summary.totalSize / (1024f * 1024f)).ToString("F0") + " МБ" +
                  (_snowBakedNow ? "\n\nСнег для зимнего города запечён автоматически (Severny > 10)." : "") +
                  "\n\nЗима в игре: В РЕЙД > Сезон: Зима."
                : "Сборка не удалась — подробности в Console.", "OK");
            if (ok) EditorUtility.RevealInFinder(Path.Combine(dir, ExeName));
        }

        public static void BuildWindowsBatch() { Batch(BuildTarget.StandaloneWindows64, "Builds/Windows/" + ExeName); }
        public static void BuildLinuxBatch() { Batch(BuildTarget.StandaloneLinux64, "Builds/Linux/SevernyOkrug.x86_64"); }
        public static void PrepareBatch()
        {
            int code = 1;
            try { code = Prepare(true, false) ? 0 : 1; }
            catch (Exception e) { Debug.LogException(e); code = 3; }
            EditorApplication.Exit(code);
        }

        static void Batch(BuildTarget target, string defaultOut)
        {
            int code = 1;
            try
            {
                string outPath = Arg("-severnyOut") ?? defaultOut;
                if (Prepare(true, false))
                {
                    var r = Build(target, outPath);
                    code = r != null && r.summary.result == BuildResult.Succeeded ? 0 : 2;
                }
            }
            catch (Exception e) { Debug.LogException(e); code = 3; }
            Debug.Log("[Severny] Batch build finished, exit code " + code);
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        /// <summary>Scenes that go into the player: the menu/boot scene first, the FBX-city raid scene only when the city was placed.</summary>
        public static List<string> BuildScenes()
        {
            var l = new List<string>();
            if (File.Exists(SevernyMenu.MainScene)) l.Add(SevernyMenu.MainScene);
            var so = AssetDatabase.LoadAssetAtPath<SevernyGameAssets>(SevernyMenu.AssetsSO);
            if (so != null && so.severnyMapPlaced && File.Exists(SevernyMenu.RaidScene)) l.Add(SevernyMenu.RaidScene);
            return l;
        }

        /// <summary>Project setup (materials, scenes) + variant materials + player settings. force = always run setup (batch).</summary>
        public static bool Prepare(bool force, bool interactive)
        {
            if (force || !File.Exists(SevernyMenu.AssetsSO) || !File.Exists(SevernyMenu.MainScene))
            {
                if (!SevernyMenu.SetupCore(interactive)) return false;
                SevernyMenu.CreateScenesCore(false);
            }
            else PolygonKitEditor.SetupKit();
            V14Setup.EnsureSettings();
            EnsureVariantMaterials();
            SevernyGraphicsEditor.ApplyAll(false);   // v12: Forward+, SSAO, shadows of the street lamps, sky / FX materials (variants kept in the build)
            try { VolnaEditor.EnsureAssets(); }                 // v13: import settings + sky materials of Resources/VolnaLook
            catch (Exception e) { Debug.LogWarning("[Severny] v13: " + e.Message); }
            { int miss; SevernyModelImport.EnsureMaterials(false, out miss); }   // v07 model materials (Resources/SevernyModels)
            // v08 winter: the city's roof/car snow goes into the build (baked once; re-baked after Severny > 3)
            _snowBakedNow = false;
            try { _snowBakedNow = SevernyWinterBake.EnsureForBuild(interactive); }
            catch (Exception e) { Debug.LogWarning("[Severny] Winter bake before build failed (the build goes on, city roofs without snow): " + e); }
            SevernyIcon.Apply(false);
            var scenes = BuildScenes();
            if (scenes.Count == 0 || scenes[0] != SevernyMenu.MainScene)
            {
                Debug.LogError("[Severny] " + SevernyMenu.MainScene + " not found — run Severny > 2 Create Scenes.");
                if (interactive) EditorUtility.DisplayDialog("Северный Округ", "Нет сцены " + SevernyMenu.MainScene + ".\nВыполните Severny > 2 Create Scenes.", "OK");
                return false;
            }
            if (force)
            {
                // batch build: Build Settings = exactly our scenes (the menu build passes its scene list directly)
                var list = new List<EditorBuildSettingsScene>();
                foreach (var s in scenes) list.Add(new EditorBuildSettingsScene(s, true));
                EditorBuildSettings.scenes = list.ToArray();
                PlayerSettings.productName = "Severny Okrug";
            }
            else if (string.IsNullOrEmpty(PlayerSettings.productName) || PlayerSettings.productName == "My project") PlayerSettings.productName = "Severny Okrug";
            if (PlayerSettings.companyName == "DefaultCompany") PlayerSettings.companyName = "Severny";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            // Mono: no IL2CPP module / Visual Studio C++ tools needed, faster build
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Disabled);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
            AssetDatabase.SaveAssets();
            Debug.Log("[Severny] Prepared for build. Scenes: " + string.Join(", ", scenes));
            return true;
        }

        /// <summary>Materials in Resources so shader variants used by runtime-created materials survive build stripping.</summary>
        static void EnsureVariantMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (lit == null || unlit == null) return;
            if (!AssetDatabase.IsValidFolder(VariantsDir)) AssetDatabase.CreateFolder(SevernyMenu.Root + "/Resources", "SevernyVariants");
            Variant("V_LitEmissive", lit, m => { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; });
            Variant("V_LitTransparent", lit, Transparent);
            Variant("V_UnlitTransparent", unlit, Transparent);
            Variant("V_LitInstanced", lit, m => { m.enableInstancing = true; });
            // v08 winter / weather materials are created at runtime: keep their exact keyword sets in the player
            var flat = FlatNormal();
            Variant("V_LitCutout", lit, m => { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; });
            Variant("V_LitNormal", lit, m => { if (flat != null) m.SetTexture("_BumpMap", flat); m.EnableKeyword("_NORMALMAP"); });
            Variant("V_LitNormalInstanced", lit, m => { if (flat != null) m.SetTexture("_BumpMap", flat); m.EnableKeyword("_NORMALMAP"); m.enableInstancing = true; });
            Variant("V_LitTransparentNoShadows", lit, m => { Transparent(m); m.SetFloat("_ReceiveShadows", 0f); m.EnableKeyword("_RECEIVE_SHADOWS_OFF"); });
        }

        /// <summary>4x4 flat normal map asset (so the _NORMALMAP variant materials keep a real texture and their keyword).</summary>
        static Texture2D FlatNormal()
        {
            string path = VariantsDir + "/V_FlatNormal.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null) return t;
            try
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(128, 128, 255, 255);
                tex.SetPixels32(px); tex.Apply();
                Directory.CreateDirectory(VariantsDir);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null) { ti.textureType = TextureImporterType.NormalMap; ti.mipmapEnabled = false; ti.SaveAndReimport(); }
            }
            catch (Exception e) { Debug.LogWarning("[Severny] Flat normal map: " + e.Message); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Transparent(Material m)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static void Variant(string name, Shader sh, Action<Material> setup)
        {
            string path = VariantsDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh) { name = name }; setup(m); AssetDatabase.CreateAsset(m, path); }
        }

        public static BuildReport Build(BuildTarget target, string outPath)
        {
            var scenes = BuildScenes();
            outPath = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var opt = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(), locationPathName = outPath, target = target, options = BuildOptions.None
            };
            Debug.Log("[Severny] Building " + target + " -> " + outPath + " (" + scenes.Count + " scenes)");
            var rep = BuildPipeline.BuildPlayer(opt);
            var sm = rep.summary;
            Debug.Log("[Severny] Build " + sm.result + ": " + outPath + ", " + (sm.totalSize / (1024f * 1024f)).ToString("F0") + " MB, errors " + sm.totalErrors +
                      ", warnings " + sm.totalWarnings + ", " + sm.totalTime.TotalMinutes.ToString("F1") + " min");
            return rep;
        }
    }
}
