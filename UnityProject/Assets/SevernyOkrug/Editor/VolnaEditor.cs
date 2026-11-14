// v13.0 «ТЦ «Волна»: свет, небо, пост» — editor side of Resources/VolnaLook:
//  * import settings: 8K sky panoramas VOL_Sky_* (no mips → no seam, repeat around, clamp at the poles, BC7),
//    reflection skies VOL_SkyR_* (lat-long → cubemap with specular convolution), wet-patch masks VOL_Wet_* (tiling, mips);
//    applied on the first import (SevernyGfxTexturePostprocessor calls Configure — no new texture postprocessor, so the
//    project's textures are not reimported) and re-checked on every editor load and before every build;
//  * sky materials Resources/VolnaLook/Materials/VOL_SkyMat_* (Skybox/Panoramic, keeps the shader variant in builds);
//  * menu Severny > Volna: Screenshots (v13) / Volna: Screenshots + проверки v12 — opens Severny_Main, enters Play mode and
//    runs the screenshot points (ShotTool); the folder with the PNGs opens at the end, Play mode stops.
// Deliberately outside Resources/PolygonKit: the kit preprocessor clamps every texture there to 2048.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Severny.EditorTools
{
    [InitializeOnLoad]
    public static class VolnaEditor
    {
        public const string Dir = SevernyMenu.Root + "/Resources/VolnaLook";
        const string MatDir = Dir + "/Materials";
        public const string ShotsKey = "severny_shots";
        static readonly string[] Skies = { "Morning", "Day", "Evening", "Overcast" };

        static VolnaEditor()
        {
            if (Application.isBatchMode) return;      // batch builds: SevernyBuild.Prepare calls EnsureAssets
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                try { EnsureAssets(); }
                catch (Exception e) { Debug.LogWarning("[Severny] v13: ресурсы «Волны» не подготовлены: " + e.Message); }
            };
        }

        public static bool InLook(string path) => path.Replace('\\', '/').IndexOf("/Resources/VolnaLook/", StringComparison.Ordinal) >= 0;

        /// <summary>Sets the import settings of a Resources/VolnaLook texture; true = something changed.</summary>
        public static bool Configure(TextureImporter ti, string path)
        {
            if (ti == null) return false;
            string file = Path.GetFileNameWithoutExtension(path);
            bool ch = false;
            if (file.StartsWith("VOL_SkyR_", StringComparison.Ordinal))
            {
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                bool sc = false;
                if (s.textureType != TextureImporterType.Default) { s.textureType = TextureImporterType.Default; sc = true; }
                if (s.textureShape != TextureImporterShape.TextureCube) { s.textureShape = TextureImporterShape.TextureCube; sc = true; }
                if (s.generateCubemap != TextureImporterGenerateCubemap.Cylindrical) { s.generateCubemap = TextureImporterGenerateCubemap.Cylindrical; sc = true; }
                if (s.cubemapConvolution != TextureImporterCubemapConvolution.Specular) { s.cubemapConvolution = TextureImporterCubemapConvolution.Specular; sc = true; }
                if (!s.seamlessCubemap) { s.seamlessCubemap = true; sc = true; }
                if (!s.mipmapEnabled) { s.mipmapEnabled = true; sc = true; }
                if (!s.sRGBTexture) { s.sRGBTexture = true; sc = true; }
                if (sc) { ti.SetTextureSettings(s); ch = true; }
                if (ti.maxTextureSize != 256) { ti.maxTextureSize = 256; ch = true; }
                if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; ch = true; }
            }
            else if (file.StartsWith("VOL_Sky_", StringComparison.Ordinal))
            {
                int max = file == "VOL_Sky_Overcast" ? 4096 : 8192;   // overcast: soft, mostly behind rain / fog
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; ch = true; }
                if (ti.textureShape != TextureImporterShape.Texture2D) { ti.textureShape = TextureImporterShape.Texture2D; ch = true; }
                if (ti.mipmapEnabled) { ti.mipmapEnabled = false; ch = true; }
                if (ti.wrapModeU != TextureWrapMode.Repeat) { ti.wrapModeU = TextureWrapMode.Repeat; ch = true; }
                if (ti.wrapModeV != TextureWrapMode.Clamp) { ti.wrapModeV = TextureWrapMode.Clamp; ch = true; }
                if (ti.filterMode != FilterMode.Bilinear) { ti.filterMode = FilterMode.Bilinear; ch = true; }
                if (ti.maxTextureSize != max) { ti.maxTextureSize = max; ch = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; ch = true; }
                if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; ch = true; }
                if (!ti.sRGBTexture) { ti.sRGBTexture = true; ch = true; }
                if (ti.isReadable) { ti.isReadable = false; ch = true; }
            }
            else if (file.StartsWith("VOL_Skyline", StringComparison.Ordinal))      // v13.1: distant skyline band 8192x256 (RGB relative to fog, A coverage)
            {
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; ch = true; }
                if (ti.textureShape != TextureImporterShape.Texture2D) { ti.textureShape = TextureImporterShape.Texture2D; ch = true; }
                if (ti.mipmapEnabled) { ti.mipmapEnabled = false; ch = true; }
                if (ti.wrapModeU != TextureWrapMode.Repeat) { ti.wrapModeU = TextureWrapMode.Repeat; ch = true; }
                if (ti.wrapModeV != TextureWrapMode.Clamp) { ti.wrapModeV = TextureWrapMode.Clamp; ch = true; }
                if (ti.filterMode != FilterMode.Bilinear) { ti.filterMode = FilterMode.Bilinear; ch = true; }
                if (ti.maxTextureSize != 8192) { ti.maxTextureSize = 8192; ch = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; ch = true; }
                if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; ch = true; }
                if (!ti.sRGBTexture) { ti.sRGBTexture = true; ch = true; }
                if (ti.alphaSource != TextureImporterAlphaSource.FromInput) { ti.alphaSource = TextureImporterAlphaSource.FromInput; ch = true; }
                if (ti.alphaIsTransparency) { ti.alphaIsTransparency = false; ch = true; }
                if (ti.isReadable) { ti.isReadable = false; ch = true; }
            }
            else if (file.StartsWith("VOL_Wet_", StringComparison.Ordinal))
            {
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; ch = true; }
                if (ti.textureShape != TextureImporterShape.Texture2D) { ti.textureShape = TextureImporterShape.Texture2D; ch = true; }
                if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; ch = true; }
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; ch = true; }
                if (ti.alphaSource != TextureImporterAlphaSource.FromInput) { ti.alphaSource = TextureImporterAlphaSource.FromInput; ch = true; }
                if (ti.alphaIsTransparency) { ti.alphaIsTransparency = false; ch = true; }
                if (ti.maxTextureSize != 512) { ti.maxTextureSize = 512; ch = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; ch = true; }
                if (!ti.sRGBTexture) { ti.sRGBTexture = true; ch = true; }
                if (ti.anisoLevel < 4) { ti.anisoLevel = 4; ch = true; }
            }
            return ch;
        }

        /// <summary>Import settings + sky materials (idempotent). Returns a short report.</summary>
        public static string EnsureAssets()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) return "нет папки " + Dir + " (пакет v13.0 не импортирован?)";
            int fixedTex = 0, mats = 0;
            foreach (var f in Directory.GetFiles(Dir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".jpg" && ext != ".png") continue;
                string p = f.Replace('\\', '/');
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti != null && Configure(ti, p)) { ti.SaveAndReimport(); fixedTex++; }
            }
            var pano = Shader.Find("Skybox/Panoramic");
            foreach (var n in Skies)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/VOL_Sky_" + n + ".jpg");
                if (pano == null || tex == null) continue;
                if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Dir, "Materials");
                string mp = MatDir + "/VOL_SkyMat_" + n + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m != null && m.shader == pano && m.GetTexture("_MainTex") == tex) continue;
                bool create = m == null;
                if (create) m = new Material(pano) { name = "VOL_SkyMat_" + n };
                else m.shader = pano;
                m.SetTexture("_MainTex", tex);
                SetF(m, "_Mapping", 1f); SetF(m, "_ImageType", 0f); SetF(m, "_MirrorOnBack", 0f); SetF(m, "_Layout", 0f);
                SetF(m, "_Exposure", 1f); SetF(m, "_Rotation", 0f);
                if (m.HasProperty("_Tint")) m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                m.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
                if (create) AssetDatabase.CreateAsset(m, mp); else EditorUtility.SetDirty(m);
                mats++;
            }
            if (mats > 0) AssetDatabase.SaveAssets();
            string r = "«Волна» v13: текстур перенастроено " + fixedTex + ", материалов неба создано " + mats;
            if (fixedTex > 0 || mats > 0) Debug.Log("[Severny] " + r);
            return r;
        }

        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        // ------------------------------------------------------------------ screenshot points
        [MenuItem("Severny/Volna: Screenshots (v13)", false, 33)]
        static void ShotsMenu() { StartShots("v13"); }

        [MenuItem("Severny/Volna: Screenshots + проверки v12", false, 34)]
        static void ShotsAllMenu() { StartShots("all"); }

        static void StartShots(string mode)
        {
            const string T = "Фототочки «ТЦ «Волна»»";
            if (EditorApplication.isPlaying)
            {
                var gb = GameBootstrap.I;
                if (gb != null && !gb.InRaid && !gb.Busy && !ShotTool.Running) { gb.StartShots(mode == "all", false); return; }
                EditorUtility.DisplayDialog(T, "Сейчас идёт рейд или загрузка. Выйдите в главное меню (или остановите Play) и повторите.", "OK");
                return;
            }
            if (!File.Exists(SevernyMenu.MainScene)) { EditorUtility.DisplayDialog(T, "Нет сцены " + SevernyMenu.MainScene + " — выполните Severny > 2 Create Scenes.", "OK"); return; }
            try { EnsureAssets(); } catch (Exception e) { Debug.LogWarning("[Severny] v13: " + e.Message); }
            if (!EditorUtility.DisplayDialog(T,
                    (mode == "all" ? "Около 130 кадров: v13 утро / день / вечер после дождя, «до» (v12) и проверки v12 на точках R1–R8 (без проб, без дымки, грейд v11.1, дождь), v13 ясно / дождь / туман.\n\n"
                                   : "Около 64 кадров: v13 утро / день / вечер после дождя и «до» — утро v12.\n\n") +
                    "Займёт несколько минут; окно Game лучше развернуть. В конце откроется папка с PNG и Play остановится.", "Начать", "Отмена")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (EditorSceneManager.GetActiveScene().path != SevernyMenu.MainScene) EditorSceneManager.OpenScene(SevernyMenu.MainScene, OpenSceneMode.Single);
            EditorPrefs.SetString(ShotsKey, mode);
            EditorApplication.isPlaying = true;
        }
    }
}
