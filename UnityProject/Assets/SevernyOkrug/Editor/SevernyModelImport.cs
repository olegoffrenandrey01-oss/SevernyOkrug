// v07: import settings for the Blender-made models in Resources/SevernyModels (characters CH_*, first-person arms FP_*, weapons W_*)
// and the material generator: ModelSpecs -> Resources/SevernyModels/Materials/M_<key>.mat (URP Lit), remapped onto the FBX slots.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Severny.EditorTools
{
    public class SevernyModelPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 2;

        void OnPreprocessModel()
        {
            if (!SevernyModelImport.In(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath);
            bool rigged = n.StartsWith("CH_") || n.StartsWith("FP_");
            mi.importCameras = false; mi.importLights = false;
            mi.importAnimation = false; mi.importBlendShapes = false; mi.importConstraints = false;
            mi.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (rigged) { mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar; mi.optimizeGameObjects = false; }
            mi.optimizeBones = false;                       // keep the whole skeleton (bone names are looked up at runtime)
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;   // replaced by M_<key>.mat via remap (Severny > 9)
            mi.addCollider = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.useFileScale = true; mi.globalScale = 1f;
            mi.bakeAxisConversion = true;
            mi.generateSecondaryUV = false;
        }

        void OnPreprocessTexture()
        {
            if (!SevernyModelImport.In(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath);
            ti.textureShape = TextureImporterShape.Texture2D;
            if (n.EndsWith("_N")) ti.textureType = TextureImporterType.NormalMap;
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = !n.EndsWith("_MS");            // R metal, G AO, A smoothness: linear data
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                if (n.EndsWith("_BC")) { ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f; }
            }
            ti.mipmapEnabled = true; ti.anisoLevel = 4;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
            {
                var q = p.Replace('\\', '/');
                if (SevernyModelImport.In(q) && !q.Contains("/Materials/")) { SevernyModelImport.ScheduleCheck(); return; }
            }
        }
    }

    [InitializeOnLoad]
    public static class SevernyModelImport
    {
        public const string Dir = SevernyMenu.Root + "/Resources/SevernyModels";
        const string MatDir = Dir + "/Materials";

        // string check only (called inside imports)
        public static bool In(string p) => p.Replace('\\', '/').Contains("/Resources/SevernyModels/");

        static SevernyModelImport() { ScheduleCheck(); }

        static bool _scheduled;
        static int _retries;
        public static void ScheduleCheck()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += () => { _scheduled = false; AutoCheck(); };
        }

        static void RetryLater(double seconds)
        {
            if (_retries >= 30) return;
            _retries++;
            double at = EditorApplication.timeSinceStartup + seconds;
            EditorApplication.CallbackFunction f = null;
            f = () => { if (EditorApplication.timeSinceStartup < at) return; EditorApplication.update -= f; ScheduleCheck(); };
            EditorApplication.update += f;
        }

        static void AutoCheck()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { RetryLater(2.0); return; }
            if (!AssetDatabase.IsValidFolder(Dir)) return;
            int missingTex;
            int n = EnsureMaterials(false, out missingTex);
            if (missingTex > 0) RetryLater(3.0);
            if (n > 0) Debug.Log("[Severny] Models: " + n + " materials/remaps updated (Resources/SevernyModels)");
        }

        [MenuItem("Severny/9 Models: Rebuild Materials", false, 10)]
        static void Menu()
        {
            int miss;
            int n = EnsureMaterials(true, out miss);
            EditorUtility.DisplayDialog("Северный Округ", "Модели v07: обновлено материалов/привязок: " + n
                + (miss > 0 ? "\nНе найдено текстур: " + miss + " (см. Console)" : ""), "OK");
        }

        /// <summary>Create/refresh M_key.mat for every ModelSpecs entry and remap the FBX material slots onto them.</summary>
        public static int EnsureMaterials(bool force, out int missingTex)
        {
            missingTex = 0;
            if (!AssetDatabase.IsValidFolder(Dir)) return 0;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) { Debug.LogWarning("[Severny] URP Lit shader not found - materials not generated"); return 0; }
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Dir, "Materials");
            int changed = 0;
            var mats = new Dictionary<string, Material>();
            foreach (var s in ModelSpecs.Mats)
            {
                string path = MatDir + "/M_" + s.key + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool texOk = ModelLib.Tex(s.bc) != null || string.IsNullOrEmpty(s.bc);
                if (!texOk) { missingTex++; if (force) Debug.LogWarning("[Severny] texture missing: " + s.bc); }
                if (m == null)
                {
                    m = new Material(lit) { name = "M_" + s.key };
                    ModelLib.Setup(m, s);
                    AssetDatabase.CreateAsset(m, path); changed++;
                }
                else if (force || m.shader != lit || (texOk && m.GetTexture("_BaseMap") == null))
                {
                    if (m.shader != lit) m.shader = lit;
                    ModelLib.Setup(m, s); EditorUtility.SetDirty(m); changed++;
                }
                mats[s.key] = m;
            }
            if (changed > 0) AssetDatabase.SaveAssets();
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { Dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null) continue;
                var map = mi.GetExternalObjectMap();
                bool ch = false;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                {
                    var em = o as Material;
                    if (em == null) continue;
                    if (!mats.TryGetValue(ModelLib.KeyOf(em.name), out var target) || target == null) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name);
                    if (map.TryGetValue(id, out var cur) && cur == target) continue;
                    mi.AddRemap(id, target); ch = true;
                }
                if (ch) { mi.SaveAndReimport(); changed++; }
            }
            return changed;
        }
    }
}
