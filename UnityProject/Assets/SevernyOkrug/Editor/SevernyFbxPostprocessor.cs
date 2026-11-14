// Import settings for everything copied into Assets/SevernyOkrug/Map (FBX from s08_export_unity_fbx.py + textures).
using UnityEditor;
using UnityEngine;

namespace Severny.EditorTools
{
    public class SevernyFbxPostprocessor : AssetPostprocessor
    {
        public const string MapDir = "Assets/SevernyOkrug/Map";

        static bool InMap(string p) => p.Replace('\\', '/').StartsWith(MapDir + "/");

        void OnPreprocessModel()
        {
            if (!InMap(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            if (!mi.importSettingsMissing) return; // only on first import: keep any manual changes
            mi.importCameras = false; mi.importLights = false;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.External;     // shared .mat files in Map/Materials
            mi.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            mi.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
            mi.addCollider = false;                                           // colliders: menu Severny > 3 (per manifest)
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.indexFormat = ModelImporterIndexFormat.Auto;
            mi.generateSecondaryUV = false;
            mi.useFileScale = true; mi.globalScale = 1f;
        }

        void OnPreprocessTexture()
        {
            if (!InMap(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            if (!ti.importSettingsMissing) return;
            string n = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool normal = n.EndsWith("_N") || n.EndsWith("_Normal") || n.EndsWith("_NRM") || n.Contains("_Normal");
            bool data = n.EndsWith("_ORM") || n.EndsWith("_R") || n.EndsWith("_AO");
            if (normal) ti.textureType = TextureImporterType.NormalMap;
            ti.sRGBTexture = !normal && !data;
            ti.anisoLevel = 4;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
