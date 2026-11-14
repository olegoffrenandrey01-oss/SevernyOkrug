// Game icon (the .exe / window / taskbar icon): Assets/SevernyOkrug/Icon/SevernyOkrug_Icon.png (1024 px)
// + SevernyOkrug_Icon_Small.png (simplified, used for the 16–48 px sizes).
// Applied automatically after import (unless the project already has another icon) and before every build
// from Severny > 6; menu Severny > Apply Game Icon forces it.
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Severny.EditorTools
{
    public class SevernyIcon : AssetPostprocessor
    {
        public const string Dir = SevernyMenu.Root + "/Icon";
        public const string IconPath = Dir + "/SevernyOkrug_Icon.png";
        public const string SmallPath = Dir + "/SevernyOkrug_Icon_Small.png";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Dir + "/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.isReadable = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 1024;
            ti.wrapMode = TextureWrapMode.Clamp;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p == IconPath) { EditorApplication.delayCall += () => Apply(false); break; }
        }

        [MenuItem("Severny/Apply Game Icon", false, 50)]
        static void ApplyMenu()
        {
            bool ok = Apply(true);
            EditorUtility.DisplayDialog("Северный Округ", ok ? "Иконка игры установлена (Project Settings > Player > Icon)." : "Не найден файл " + IconPath, "OK");
        }

        /// <summary>Sets the default and PC standalone icons. force = false keeps an icon the user has set himself
        /// and does nothing when ours is already applied.</summary>
        public static bool Apply(bool force)
        {
            var big = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (big == null) return false;
            var cur = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            bool empty = cur == null || cur.Length == 0 || cur[0] == null;
            bool ours = !empty && cur[0] == big;
            if (!force && !empty && !ours) return false;   // the project has its own icon — keep it
            if (!force && ours) return true;               // already applied
            EnsureImport(IconPath); EnsureImport(SmallPath);
            big = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var small = AssetDatabase.LoadAssetAtPath<Texture2D>(SmallPath);
            if (small == null) small = big;
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { big }, IconKind.Any);
            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
            if (sizes != null && sizes.Length > 0)
            {
                var arr = new Texture2D[sizes.Length];
                for (int i = 0; i < sizes.Length; i++) arr[i] = sizes[i] <= 48 ? small : big;
                PlayerSettings.SetIcons(NamedBuildTarget.Standalone, arr, IconKind.Any);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Severny] Game icon applied: " + IconPath);
            return true;
        }

        /// <summary>Re-imports the icon texture when it was imported before this postprocessor existed (compressed / mipmapped).</summary>
        static void EnsureImport(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            if (ti.textureCompression != TextureImporterCompression.Uncompressed || ti.mipmapEnabled || !ti.alphaIsTransparency ||
                ti.npotScale != TextureImporterNPOTScale.None || ti.maxTextureSize < 1024)
                ti.SaveAndReimport();   // OnPreprocessTexture applies the icon settings
        }
    }

    /// <summary>Sets the icon once after the package is imported / scripts are compiled, if the project has no icon yet.</summary>
    [InitializeOnLoad]
    static class SevernyIconAutoApply
    {
        static SevernyIconAutoApply()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () => SevernyIcon.Apply(false);
        }
    }
}
