// v12 «Графика и карта»: render pipeline settings and the v12 graphics assets.
//   Severny > 7 Graphics: Apply Render Settings (v12) — every URP asset of the project: soft shadows (medium), per-pixel
//     additional lights with shadows (street lamps), reflection probe blending + box projection, depth texture (soft particles),
//     64-bit HDR buffer (no banding in the night fog), LOD cross-fade, SRP batcher, cascade splits; every Universal renderer:
//     Forward+ (per-pixel reflection probes, many lights) and a configured SSAO feature (the in-game preset switches it on/off).
//     Also run by Severny > 1 Setup and before every build (Severny > 6 / batch).
//   Severny > 7b Graphics: Report — what is set now.
// After the package import a one-time dialog offers the step. Import settings of Resources/SevernyGfx (sky panoramas,
// reflection cubemaps, FX textures) and the sky / particle materials are ensured silently on every editor load.
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Severny.EditorTools
{
    [InitializeOnLoad]
    public static class SevernyGraphicsEditor
    {
        public const string GfxDir = SevernyMenu.Root + "/Resources/SevernyGfx";
        const string MatDir = GfxDir + "/Materials";
        static readonly string[] Skies = { "Day", "Morning", "Evening", "Night" };
        static string AskedKey => "severny_v12_render_asked_" + Application.dataPath;

        static SevernyGraphicsEditor()
        {
            if (Application.isBatchMode) return;      // batch builds: SevernyBuild.Prepare calls ApplyAll
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                try { EnsureAssets(); }
                catch (Exception e) { Debug.LogWarning("[Severny] v12: ресурсы графики не подготовлены: " + e.Message); }
                if (EditorPrefs.GetBool(AskedKey, false)) return;
                if (!File.Exists(SevernyMenu.AssetsSO)) return;          // not set up yet: Severny > 1 Setup applies the settings itself
                EditorPrefs.SetBool(AskedKey, true);
                if (EditorUtility.DisplayDialog("Северный Округ v12",
                        "Обновление «Графика и карта».\n\nПрименить настройки рендера v12 к URP-ассетам проекта?\n" +
                        "Forward+, SSAO, мягкие тени, тени фонарей, смешивание отражений, LOD cross-fade, 64-битный HDR.\n\n" +
                        "Можно и позже: меню Severny > 7 Graphics.", "Применить", "Позже"))
                    ApplyAll(true);
            };
        }

        [MenuItem("Severny/7 Graphics: Apply Render Settings (v12)", false, 8)]
        static void ApplyMenu() { ApplyAll(true); }

        [MenuItem("Severny/7b Graphics: Report", false, 9)]
        static void ReportMenu() { EditorUtility.DisplayDialog("Северный Округ — графика", Report(), "OK"); }

        /// <summary>Import settings + materials + URP settings. interactive = show the result dialog.</summary>
        public static bool ApplyAll(bool interactive)
        {
            string assets, render;
            try { assets = EnsureAssets(); }
            catch (Exception e) { assets = "ошибка: " + e.Message; Debug.LogWarning("[Severny] v12 assets: " + e); }
            try { render = ApplyRenderSettings(); }
            catch (Exception e) { render = "ошибка: " + e.Message; Debug.LogError("[Severny] v12 render settings: " + e); }
            EditorPrefs.SetBool(AskedKey, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[Severny] Графика v12: " + assets + "\n" + render);
            if (interactive) EditorUtility.DisplayDialog("Северный Округ — графика v12", "Ресурсы: " + assets + "\n\n" + render + "\n\nНастройка в игре: Настройки > «Качество графики» и «Сглаживание».", "OK");
            return !render.StartsWith("ошибка");
        }

        // ------------------------------------------------------------------ assets
        /// <summary>Texture import settings and the sky / particle materials of Resources/SevernyGfx (idempotent).</summary>
        public static string EnsureAssets()
        {
            if (!AssetDatabase.IsValidFolder(GfxDir)) return "нет папки " + GfxDir + " (пакет v12 не импортирован?)";
            int fixedTex = 0, mats = 0;
            foreach (var n in Skies)
            {
                if (Panorama(GfxDir + "/Sky/SEV_Sky_" + n + ".jpg")) fixedTex++;
                if (CubeFromLatLong(GfxDir + "/Sky/SEV_SkyR_" + n + ".jpg")) fixedTex++;
            }
            if (Fx(GfxDir + "/Textures/SEV_LensDirt.png", false, 1024)) fixedTex++;
            foreach (var t in new[] { "SEV_Smoke", "SEV_Mist", "SEV_Glow", "SEV_Splash", "SEV_Crow" })
                if (Fx(GfxDir + "/Textures/" + t + ".png", true, 512)) fixedTex++;
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(GfxDir, "Materials");
            var pano = Shader.Find("Skybox/Panoramic");
            foreach (var n in Skies)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GfxDir + "/Sky/SEV_Sky_" + n + ".jpg");
                if (pano == null || tex == null) continue;
                string mp = MatDir + "/SEV_SkyMat_" + n + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m != null && m.shader == pano && m.GetTexture("_MainTex") == tex) continue;
                bool create = m == null;
                if (create) m = new Material(pano) { name = "SEV_SkyMat_" + n };
                else m.shader = pano;
                m.SetTexture("_MainTex", tex);
                SetF(m, "_Mapping", 1f); SetF(m, "_ImageType", 0f); SetF(m, "_MirrorOnBack", 0f); SetF(m, "_Layout", 0f);
                SetF(m, "_Exposure", 1f); SetF(m, "_Rotation", 0f);
                if (m.HasProperty("_Tint")) m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                m.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
                if (create) AssetDatabase.CreateAsset(m, mp); else EditorUtility.SetDirty(m);
                mats++;
            }
            foreach (var d in GfxRes.FX)
            {
                string mp = MatDir + "/SEV_FX_" + d.name + ".mat";
                var old = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (old != null && old.shader != null && old.shader.name == d.shader) continue;
                var sh = Shader.Find(d.shader);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GfxDir + "/Textures/" + d.tex + ".png");
                if (sh == null || tex == null) continue;
                var m = GfxRes.MakeParticleMaterial(sh, tex, d);
                m.name = "SEV_FX_" + d.name;
                if (old != null) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(m, mp);
                mats++;
            }
            if (mats > 0) AssetDatabase.SaveAssets();
            return "текстур перенастроено " + fixedTex + ", материалов создано " + mats;
        }

        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        /// <summary>Sky panorama: no mips (no seam at the wrap), repeat around, clamp at the poles, 4K, high-quality compression.</summary>
        static bool Panorama(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return false;
            bool ch = false;
            if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; ch = true; }
            if (ti.textureShape != TextureImporterShape.Texture2D) { ti.textureShape = TextureImporterShape.Texture2D; ch = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; ch = true; }
            if (ti.wrapModeU != TextureWrapMode.Repeat) { ti.wrapModeU = TextureWrapMode.Repeat; ch = true; }
            if (ti.wrapModeV != TextureWrapMode.Clamp) { ti.wrapModeV = TextureWrapMode.Clamp; ch = true; }
            if (ti.maxTextureSize != 4096) { ti.maxTextureSize = 4096; ch = true; }
            if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; ch = true; }
            if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; ch = true; }
            if (!ti.sRGBTexture) { ti.sRGBTexture = true; ch = true; }
            if (ch) ti.SaveAndReimport();
            return ch;
        }

        /// <summary>Reflection sky: lat-long image → cubemap with specular (GGX) convolution of the mips.</summary>
        static bool CubeFromLatLong(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return false;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            bool ch = false;
            if (s.textureShape != TextureImporterShape.TextureCube) { s.textureShape = TextureImporterShape.TextureCube; ch = true; }
            if (s.generateCubemap != TextureImporterGenerateCubemap.Cylindrical) { s.generateCubemap = TextureImporterGenerateCubemap.Cylindrical; ch = true; }
            if (s.cubemapConvolution != TextureImporterCubemapConvolution.Specular) { s.cubemapConvolution = TextureImporterCubemapConvolution.Specular; ch = true; }
            if (!s.seamlessCubemap) { s.seamlessCubemap = true; ch = true; }
            if (!s.mipmapEnabled) { s.mipmapEnabled = true; ch = true; }
            if (ch) ti.SetTextureSettings(s);
            if (ti.maxTextureSize != 256) { ti.maxTextureSize = 256; ch = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; ch = true; }
            if (ch) ti.SaveAndReimport();
            return ch;
        }

        /// <summary>Particle / lens textures: clamp, mips, alpha as transparency.</summary>
        static bool Fx(string path, bool alpha, int max)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return false;
            bool ch = false;
            if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; ch = true; }
            if (ti.textureShape != TextureImporterShape.Texture2D) { ti.textureShape = TextureImporterShape.Texture2D; ch = true; }
            if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; ch = true; }
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; ch = true; }
            if (alpha && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ch = true; }
            if (ti.maxTextureSize != max) { ti.maxTextureSize = max; ch = true; }
            if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; ch = true; }
            if (ch) ti.SaveAndReimport();
            return ch;
        }

        // ------------------------------------------------------------------ URP
        static void SetB(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null && p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v; }
        static void SetI(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null && (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum)) p.intValue = v; }
        static void SetFl(SerializedObject so, string name, float v) { var p = so.FindProperty(name); if (p != null && p.propertyType == SerializedPropertyType.Float) p.floatValue = v; }
        static void SetEnum(SerializedObject so, string name, string value)
        {
            var p = so.FindProperty(name);
            if (p == null || p.propertyType != SerializedPropertyType.Enum) return;
            var names = p.enumNames;
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i].Replace(" ", ""), value, StringComparison.OrdinalIgnoreCase)) { p.enumValueIndex = i; return; }
        }

        /// <summary>All URP assets + their Universal renderers. Returns a short report.</summary>
        public static string ApplyRenderSettings()
        {
            var sb = new StringBuilder();
            int assets = 0, renderers = 0, ssaoAdded = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null) continue;
                var so = new SerializedObject(a);
                SetB(so, "m_SupportsHDR", true);
                SetI(so, "m_HDRColorBufferPrecision", 1);              // 64-bit
                SetB(so, "m_RequireDepthTexture", true);              // soft particles
                SetB(so, "m_SoftShadowsSupported", true);
                SetI(so, "m_SoftShadowQuality", 2);                   // medium
                SetI(so, "m_AdditionalLightsRenderingMode", 1);       // per pixel
                SetI(so, "m_AdditionalLightsPerObjectLimit", 8);
                SetB(so, "m_AdditionalLightShadowsSupported", true);
                SetI(so, "m_AdditionalLightsShadowmapResolution", 2048);
                SetB(so, "m_ReflectionProbeBlending", true);
                SetB(so, "m_ReflectionProbeBoxProjection", true);
                SetB(so, "m_EnableLODCrossFade", true);
                SetB(so, "m_UseSRPBatcher", true);
                SetB(so, "m_SupportDataDrivenLensFlare", true);      // v13: sun flare of «ТЦ «Волна»»
                SetB(so, "m_SupportScreenSpaceLensFlare", true);
                var c4 = so.FindProperty("m_Cascade4Split");
                if (c4 != null && c4.propertyType == SerializedPropertyType.Vector3) c4.vector3Value = new Vector3(0.05f, 0.15f, 0.4f);
                so.ApplyModifiedPropertiesWithoutUndo();
                a.shadowDistance = 150f; a.shadowCascadeCount = 4; a.mainLightShadowmapResolution = 2048; a.conservativeEnclosingSphere = true;
                EditorUtility.SetDirty(a);
                assets++;
                foreach (var rd in a.rendererDataList)
                {
                    var u = rd as UniversalRendererData;
                    if (u == null) continue;
                    if (u.renderingMode != RenderingMode.ForwardPlus) u.renderingMode = RenderingMode.ForwardPlus;
                    if (EnsureSsao(u)) ssaoAdded++;
                    EditorUtility.SetDirty(u);
                    renderers++;
                }
            }
            if (assets == 0) return "ошибка: в проекте нет UniversalRenderPipelineAsset (проект не URP?)";
            sb.Append("URP-ассетов: ").Append(assets).Append(", рендереров: ").Append(renderers).Append(" (Forward+, SSAO");
            if (ssaoAdded > 0) sb.Append(" добавлен в ").Append(ssaoAdded);
            sb.Append(")\nМягкие тени, тени фонарей, смешивание отражений + box projection, depth texture, HDR 64 бит, LOD cross-fade.");
            return sb.ToString();
        }

        /// <summary>SSAO renderer feature (added like the renderer inspector does it: sub-asset + feature list + id map).</summary>
        static bool EnsureSsao(UniversalRendererData u)
        {
            ScreenSpaceAmbientOcclusion f = null;
            foreach (var x in u.rendererFeatures) if (x is ScreenSpaceAmbientOcclusion s) { f = s; break; }
            bool added = false;
            if (f == null && EditorUtility.IsPersistent(u))
            {
                f = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                f.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(f, u);
                var so = new SerializedObject(u);
                var list = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                if (list == null || !list.isArray) { UnityEngine.Object.DestroyImmediate(f, true); return false; }
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = f;
                if (map != null && map.isArray && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out string g, out long lid))
                {
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = lid;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                added = true;
            }
            if (f == null) return false;
            var fs = new SerializedObject(f);
            SetEnum(fs, "m_Settings.AOMethod", "BlueNoise");
            SetB(fs, "m_Settings.Downsample", true);
            SetB(fs, "m_Settings.AfterOpaque", false);
            SetEnum(fs, "m_Settings.Source", "DepthNormals");
            SetEnum(fs, "m_Settings.NormalSamples", "Medium");
            SetFl(fs, "m_Settings.Intensity", 1.3f);
            SetFl(fs, "m_Settings.Radius", 0.35f);
            SetFl(fs, "m_Settings.DirectLightingStrength", 0.2f);
            SetEnum(fs, "m_Settings.Samples", "Medium");
            SetEnum(fs, "m_Settings.BlurQuality", "Medium");
            SetFl(fs, "m_Settings.Falloff", 60f);
            fs.ApplyModifiedPropertiesWithoutUndo();
            f.SetActive(true);
            EditorUtility.SetDirty(f);
            u.SetDirty();
            return added;
        }

        /// <summary>Severny > 7b: current state of the URP assets and the v12 resources.</summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (a == null) continue;
                var so = new SerializedObject(a);
                sb.Append(Path.GetFileNameWithoutExtension(path)).Append(a == GraphicsSettings.defaultRenderPipeline ? " (по умолчанию)" : "").Append(": тени ")
                  .Append(a.shadowDistance.ToString("0")).Append(" м / ").Append(a.shadowCascadeCount).Append(" каск., мягкие ").Append(B(so, "m_SoftShadowsSupported"))
                  .Append(", тени доп. источников ").Append(B(so, "m_AdditionalLightShadowsSupported")).Append(", отражения: смешивание ").Append(B(so, "m_ReflectionProbeBlending"))
                  .Append(" / box ").Append(B(so, "m_ReflectionProbeBoxProjection")).Append(", LOD cross-fade ").Append(B(so, "m_EnableLODCrossFade")).Append('\n');
                foreach (var rd in a.rendererDataList)
                {
                    if (!(rd is UniversalRendererData u)) continue;
                    bool ssao = false;
                    foreach (var f in u.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion) ssao = true;
                    sb.Append("   ").Append(u.name).Append(": ").Append(u.renderingMode).Append(ssao ? ", SSAO" : ", без SSAO").Append('\n');
                }
            }
            if (sb.Length == 0) sb.Append("URP-ассеты не найдены.\n");
            int skies = 0, cubes = 0, mats = 0;
            foreach (var n in Skies)
            {
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(GfxDir + "/Sky/SEV_Sky_" + n + ".jpg") != null) skies++;
                if (AssetDatabase.LoadAssetAtPath<Cubemap>(GfxDir + "/Sky/SEV_SkyR_" + n + ".jpg") != null) cubes++;
                if (AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/SEV_SkyMat_" + n + ".mat") != null) mats++;
            }
            int fx = 0;
            foreach (var d in GfxRes.FX) if (AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/SEV_FX_" + d.name + ".mat") != null) fx++;
            sb.Append("\nНебо HDRI: ").Append(skies).Append("/4, кубкарты отражений: ").Append(cubes).Append("/4, материалы неба: ").Append(mats)
              .Append("/4, материалы эффектов: ").Append(fx).Append('/').Append(GfxRes.FX.Length);
            sb.Append("\nДанные огней: ").Append(File.Exists(GfxDir + "/severny_lights.json") ? "есть" : "нет");
            return sb.ToString();
        }

        static string B(SerializedObject so, string name) { var p = so.FindProperty(name); return p == null ? "?" : p.boolValue ? "да" : "нет"; }
    }

    /// <summary>First import of the v12 textures: the right settings straight away (EnsureAssets repeats it if needed).</summary>
    public class SevernyGfxTexturePostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (VolnaEditor.InLook(assetPath)) { VolnaEditor.Configure((TextureImporter)assetImporter, assetPath); return; }   // v13 «Волна»
            if (assetPath.IndexOf("/Resources/SevernyGfx/", StringComparison.Ordinal) < 0) return;
            var ti = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            if (file.StartsWith("SEV_SkyR_"))
            {
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.textureShape = TextureImporterShape.TextureCube; s.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                s.cubemapConvolution = TextureImporterCubemapConvolution.Specular; s.seamlessCubemap = true; s.mipmapEnabled = true;
                ti.SetTextureSettings(s);
                ti.maxTextureSize = 256; ti.textureCompression = TextureImporterCompression.Uncompressed;
            }
            else if (file.StartsWith("SEV_Sky_"))
            {
                ti.textureShape = TextureImporterShape.Texture2D; ti.mipmapEnabled = false; ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
                ti.maxTextureSize = 4096; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.npotScale = TextureImporterNPOTScale.None;
            }
            else
            {
                ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                if (file != "SEV_LensDirt") ti.alphaIsTransparency = true;
                ti.maxTextureSize = file == "SEV_LensDirt" ? 1024 : 512;
            }
        }
    }
}
