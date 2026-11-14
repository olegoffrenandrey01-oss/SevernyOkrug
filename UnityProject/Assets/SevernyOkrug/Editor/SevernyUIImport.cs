// Import settings for the UI art in Resources/SevernyUI (item icon atlas, UI glyph atlas, logo, menu background, location art):
//  * no power-of-two rescale — the logo (1229x277), the menu background (1920x1080) and the location art (1024x576)
//    keep their real size and aspect (the UI computes sizes from texture.width / height);
//  * clamp wrap (no bleeding at the edges), alpha is transparency for PNGs, never readable, never mip-streamed;
//  * mipmaps + trilinear for everything drawn smaller than its source (atlases, logo, location art), none for the
//    full-screen background;
//  * glyph atlas and logo uncompressed (thin white shapes, soft edges), the rest high-quality compressed (BC7 / DXT).
// Applied by a check after scripts compile (package import) and whenever one of these files is (re)imported;
// menu Severny > UI: Fix Texture Import forces it. Deliberately not an OnPreprocessTexture hook: adding a texture
// postprocessor would make Unity reimport every texture of the project once.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Severny.EditorTools
{
    public class SevernyUIImport : AssetPostprocessor
    {
        public const string Dir = SevernyMenu.Root + "/Resources/SevernyUI";

        static bool InDir(string path) => !string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith(Dir + "/");

        static bool IsImage(string path)
        {
            var e = Path.GetExtension(path).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".tga" || e == ".psd";
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported) if (InDir(p) && IsImage(p)) { Schedule(); return; }
            foreach (var p in moved) if (InDir(p) && IsImage(p)) { Schedule(); return; }
        }

        static bool _scheduled;
        static int _tries;

        public static void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { _scheduled = false; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (++_tries < 3000) EditorApplication.delayCall += Run; else _scheduled = false;
                return;
            }
            _scheduled = false; _tries = 0;
            Fix(true);
        }

        // Safety net against a reimport loop (a setting the platform refuses would otherwise be "fixed" forever).
        static readonly Dictionary<string, int> _fixCount = new Dictionary<string, int>();

        /// <summary>Applies the settings to every texture in Resources/SevernyUI; returns how many were reimported.</summary>
        public static int Fix(bool log)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) return 0;
            var fix = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsImage(p)) continue;
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                _fixCount.TryGetValue(p, out int n);
                if (n >= 2) continue;
                if (!Apply(ti, p)) continue;
                _fixCount[p] = n + 1;
                fix.Add(p);
            }
            if (fix.Count == 0) return 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var p in fix) { AssetDatabase.WriteImportSettingsIfDirty(p); AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            if (log) Debug.Log("[Severny] UI texture import settings fixed (Resources/SevernyUI): " + string.Join(", ", fix.ConvertAll(x => Path.GetFileName(x)).ToArray()));
            return fix.Count;
        }

        /// <summary>Sets the UI import settings on the importer; returns true when anything changed (caller reimports).</summary>
        public static bool Apply(TextureImporter ti, string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool png = Path.GetExtension(path).ToLowerInvariant() == ".png";
            bool background = name == "menu_bg";
            bool crisp = name == "ui_glyphs" || name == "logo";
            bool mips = !background;
            var filter = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            var comp = crisp ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            bool ch = false;

            if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; ch = true; }
            if (ti.textureShape != TextureImporterShape.Texture2D) { ti.textureShape = TextureImporterShape.Texture2D; ch = true; }
            if (!ti.sRGBTexture) { ti.sRGBTexture = true; ch = true; }
            if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; ch = true; }
            if (ti.wrapModeU != TextureWrapMode.Clamp || ti.wrapModeV != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; ch = true; }
            if (ti.mipmapEnabled != mips) { ti.mipmapEnabled = mips; ch = true; }
            if (ti.filterMode != filter) { ti.filterMode = filter; ch = true; }
            if (png && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ch = true; }
            if (ti.textureCompression != comp) { ti.textureCompression = comp; ch = true; }
            if (ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; ch = true; }
            if (ti.isReadable) { ti.isReadable = false; ch = true; }
            if (ti.streamingMipmaps) { ti.streamingMipmaps = false; ch = true; }
            return ch;
        }

        [MenuItem("Severny/UI: Fix Texture Import", false, 52)]
        static void FixMenu()
        {
            _fixCount.Clear();
            int n = Fix(true);
            EditorUtility.DisplayDialog("Северный Округ",
                n > 0 ? "Переимпортировано UI-текстур: " + n + " (Resources/SevernyUI)." : "Настройки импорта UI-текстур уже в порядке.", "OK");
        }
    }

    /// <summary>Checks the UI textures once after the package is imported / scripts are compiled.</summary>
    [InitializeOnLoad]
    static class SevernyUIImportCheck
    {
        static SevernyUIImportCheck() { SevernyUIImport.Schedule(); }
    }
}
