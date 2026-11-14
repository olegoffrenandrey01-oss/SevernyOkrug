// v10: import settings for the city export in Assets/SevernyOkrug/Map (SEV_*.fbx + Textures).
// A unitypackage brings its own .meta files, so SevernyFbxPostprocessor (importSettingsMissing) skips them;
// this postprocessor applies the same settings once per asset (marker in importer.userData) and, after a
// script reload, re-imports map assets that were imported before this script was compiled.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Severny.EditorTools
{
    public class SevernyMapImportV10 : AssetPostprocessor
    {
        public const string MapDir = "Assets/SevernyOkrug/Map";
        public const string Mark = "severny-map-v10";

        static bool InMap(string p) => p.Replace('\\', '/').StartsWith(MapDir + "/");

        public override int GetPostprocessOrder() => 10;   // after SevernyFbxPostprocessor

        void OnPreprocessModel()
        {
            if (!InMap(assetPath)) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null || mi.userData == Mark) return;
            Apply(mi);
        }

        void OnPreprocessTexture()
        {
            if (!InMap(assetPath)) return;
            var ti = assetImporter as TextureImporter;
            if (ti == null || ti.userData == Mark) return;
            Apply(ti, assetPath);
        }

        public static void Apply(ModelImporter mi)
        {
            mi.importCameras = false; mi.importLights = false;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.External;     // shared .mat files in Map/Materials (Severny > 3 configures them)
            mi.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            mi.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
            mi.addCollider = false;                                           // colliders: Severny > 3 (per manifest)
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.indexFormat = ModelImporterIndexFormat.Auto;
            mi.generateSecondaryUV = false;
            mi.useFileScale = true; mi.globalScale = 1f;
            mi.userData = Mark;
        }

        public static bool IsNormal(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            return n.EndsWith("_N") || n.EndsWith("_Normal") || n.EndsWith("_NRM") || n.Contains("_Normal");
        }

        public static void Apply(TextureImporter ti, string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            bool normal = IsNormal(path);
            bool data = n.EndsWith("_ORM") || n.EndsWith("_R") || n.EndsWith("_AO");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal && !data;
            if (normal) ti.flipGreenChannel = true;    // DirectX-style maps (Blender inverts G), = normal_flip_g in severny_materials.json
            ti.anisoLevel = 4;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.userData = Mark;
        }

        static bool ModelOk(ModelImporter mi) =>
            mi.materialLocation == ModelImporterMaterialLocation.External && mi.materialName == ModelImporterMaterialName.BasedOnMaterialName &&
            mi.materialImportMode != ModelImporterMaterialImportMode.None && !mi.addCollider;

        static bool TextureOk(TextureImporter ti, string path) =>
            !IsNormal(path) || (ti.textureType == TextureImporterType.NormalMap && ti.flipGreenChannel);

        /// <summary>Fixes map assets that were imported with default settings (e.g. before this script was compiled).</summary>
        public static int FixImported(bool log)
        {
            if (!AssetDatabase.IsValidFolder(MapDir)) return 0;
            var fix = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { MapDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (AssetImporter.GetAtPath(p) is ModelImporter mi && !ModelOk(mi)) { Apply(mi); fix.Add(p); }
            }
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { MapDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (AssetImporter.GetAtPath(p) is TextureImporter ti && !TextureOk(ti, p)) { Apply(ti, p); fix.Add(p); }
            }
            if (fix.Count == 0) return 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var p in fix) { AssetDatabase.WriteImportSettingsIfDirty(p); AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            if (log) Debug.Log("[Severny] Map import settings fixed for " + fix.Count + " assets (FBX: external materials, textures: normal maps).");
            return fix.Count;
        }

        [MenuItem("Severny/3a Map: Fix Import Settings", false, 3)]
        static void FixMenu()
        {
            int n = FixImported(true);
            EditorUtility.DisplayDialog("Северный Округ", n > 0 ? "Переимпортировано файлов карты: " + n + "\n\nДальше: Severny > 3 Place Map." : "Настройки импорта карты уже в порядке.", "OK");
        }
    }

    [InitializeOnLoad]
    static class SevernyMapImportV10Check
    {
        static SevernyMapImportV10Check() { EditorApplication.delayCall += Run; }

        static int tries;

        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { if (++tries < 3000) EditorApplication.delayCall += Run; return; }
            SevernyMapImportV10.FixImported(true);
        }
    }
}
