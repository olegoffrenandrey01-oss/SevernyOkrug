// Editor side of the Blender-built map «ТЦ «Волна»» (Resources/PolygonKit):
//  * import settings for its textures (normal maps, linear mask maps, alpha, HDRI sky as a cubemap with specular convolution);
//  * self-check after every import: re-bakes the materials when pk_map.json changed or a material lacks its textures;
//  * texture repair: a kit texture that Unity did not import as Texture2D is reimported, and if that does not help,
//    converted from the file bytes into a native Texture2D asset (Resources/PolygonKit/TexturesBaked, DXT, mipmaps);
//  * bakes URP Lit material assets into Resources/PolygonKit/Materials (keeps shader variants in player builds).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Severny.EditorTools
{
    public class PolygonKitPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 4;

        void OnPreprocessTexture()
        {
            if (!PolygonKitEditor.InKit(assetPath)) return;
            PolygonKitEditor.Configure((TextureImporter)assetImporter, assetPath);
        }

        // Package import order is not guaranteed: materials may get baked before their textures are imported.
        // Re-check the kit whenever any of its source files is (re)imported (not the generated materials / converted textures).
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
            {
                var q = p.Replace('\\', '/');
                if (PolygonKitEditor.InKit(q) && !q.Contains("/Materials/") && !q.Contains("/TexturesBaked/")) { PolygonKitEditor.ScheduleCheck(); return; }
            }
        }
    }

    [InitializeOnLoad]
    public static class PolygonKitEditor
    {
        public const string DefaultRoot = "Assets/SevernyOkrug/Resources/PolygonKit";
        static string _root;

        /// <summary>Kit folder (…/Resources/PolygonKit). Found by pk_map.json, so a moved SevernyOkrug folder still works.</summary>
        public static string Root
        {
            get
            {
                if (_root != null && File.Exists(_root + "/pk_map.json")) return _root;
                _root = DefaultRoot;
                if (!File.Exists(DefaultRoot + "/pk_map.json"))
                    foreach (var g in AssetDatabase.FindAssets("pk_map"))
                    {
                        var p = AssetDatabase.GUIDToAssetPath(g).Replace('\\', '/');
                        if (p.EndsWith("/Resources/PolygonKit/pk_map.json")) { _root = p.Substring(0, p.Length - "/pk_map.json".Length); break; }
                    }
                return _root;
            }
        }
        static string BakedDir => Root + "/TexturesBaked";

        static PolygonKitEditor() { ScheduleCheck(); }

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

        // string check only: called during texture import, where AssetDatabase queries are not allowed
        public static bool InKit(string p) => p.Replace('\\', '/').Contains("/Resources/PolygonKit/");

        /// <summary>Apply the import settings the kit needs; returns true when something changed.</summary>
        public static bool Configure(TextureImporter ti, string path)
        {
            string n = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path).ToLowerInvariant();
            bool ch = false;
            if (ext == ".hdr" || ext == ".exr")
            {
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                if (s.textureShape != TextureImporterShape.TextureCube || s.cubemapConvolution != TextureImporterCubemapConvolution.Specular)
                {
                    s.textureShape = TextureImporterShape.TextureCube;
                    s.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                    s.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
                    s.seamlessCubemap = true;
                    s.mipmapEnabled = true;
                    ti.SetTextureSettings(s);
                    ch = true;
                }
                if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; ch = true; }   // 1024 per cube face
                return ch;
            }
            if (ti.textureShape != TextureImporterShape.Texture2D) { ti.textureShape = TextureImporterShape.Texture2D; ch = true; }
            if (n.EndsWith("_N"))
            {
                if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ch = true; }
            }
            else
            {
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; ch = true; }
                bool linear = PolygonKit.IsLinearTex(n);
                if (ti.sRGBTexture == linear) { ti.sRGBTexture = !linear; ch = true; }
                if (n.EndsWith("_CA"))
                {
                    if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ch = true; }
                    if (!ti.mipMapsPreserveCoverage) { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.45f; ch = true; }
                }
            }
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; ch = true; }
            if (ti.anisoLevel < 8) { ti.anisoLevel = 8; ch = true; }
            if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; ch = true; }
            if (ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; ch = true; }
            return ch;
        }

        static void AutoCheck()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { RetryLater(2.0); return; }   // import still running
            if (!File.Exists(Root + "/pk_map.json")) return;
            _retries = 0;
            int fixedN = FixImports(false);
            var rep = RepairTextures();
            // re-bake when pk_map.json changed (new kit version), when textures were repaired, or a material asset is missing / lacks a texture
            string stamp = MapStamp();
            int broken = CountBrokenMaterials(out string example);
            bool needBake = fixedN > 0 || rep.Changed || broken > 0 || !File.Exists(Root + "/Materials/PK_SkyMat.mat") || !File.Exists(StampPath) || File.ReadAllText(StampPath) != stamp;
            if (!needBake) return;
            int baked = BakeMaterials(out var missing);
            if (missing.Count == 0)
            {
                try { File.WriteAllText(StampPath, stamp); } catch (IOException) { }
                Debug.Log("[Severny] PolygonKit: материалов создано/обновлено " + baked + ", настройки импорта исправлены у " + fixedN + " текстур" +
                          (broken > 0 ? ", было без текстур/отсутствовало " + broken + " (напр. " + example + ")" : "") + (rep.Changed ? "\n" + rep.Text(0) : ""));
            }
            else
            {
                string txt = rep.Text(missing.Count);
                Debug.LogWarning("[Severny] PolygonKit: материалы обновлены (" + baked + "), но " + missing.Count + " текстур не загружаются.\n" + txt);
                if (!SessionState.GetBool("severny_pk_tex_dialog", false))
                {
                    SessionState.SetBool("severny_pk_tex_dialog", true);
                    EditorUtility.DisplayDialog("Северный Округ — текстуры карты", txt, "OK");
                }
            }
        }

        // ------------------------------------------------------------------ textures
        static Texture2D LoadImported(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var ext in new[] { ".jpg", ".png", ".tga", ".jpeg" })
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + name + ext);
                if (t != null) return t;
            }
            return null;
        }

        static Texture2D LoadBaked(string name) => string.IsNullOrEmpty(name) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(BakedDir + "/" + name + ".asset");

        static Texture2D LoadTex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = LoadImported(name) ?? LoadBaked(name);
            if (t != null) return t;
            foreach (var g in AssetDatabase.FindAssets(name + " t:Texture2D", new[] { Root }))   // same name elsewhere in the kit
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) != name) continue;
                t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null) return t;
            }
            return null;
        }

        static List<string> ReferencedTextures(PKMap map)
        {
            var res = new List<string>(); var seen = new HashSet<string>();
            foreach (var d in map.materials)
                foreach (var n in new[] { d.baseMap, d.normalMap, d.maskMap })
                    if (!string.IsNullOrEmpty(n) && seen.Add(n)) res.Add(n);
            return res;
        }

        class TexReport
        {
            public int referenced, importer, already, reimported, converted;
            public readonly List<string> missingOnDisk = new List<string>(), failed = new List<string>();
            public string diag = "", folder = "";
            public bool Changed => reimported > 0 || converted > 0;

            static string Few(List<string> l) => string.Join(", ", l.GetRange(0, Mathf.Min(5, l.Count))) + (l.Count > 5 ? ", …" : "");

            public string Text(int stillMissing)
            {
                var sb = new StringBuilder();
                sb.Append("Текстур в карте: ").Append(referenced).Append('\n');
                sb.Append("• через импорт Unity: ").Append(importer).Append('\n');
                if (reimported > 0) sb.Append("• починено переимпортом: ").Append(reimported).Append('\n');
                if (converted + already > 0) sb.Append("• сконвертировано в .asset в обход импорта: ").Append(converted + already).Append('\n');
                if (failed.Count > 0) sb.Append("• не удалось прочитать файл: ").Append(failed.Count).Append(" (").Append(Few(failed)).Append(")\n");
                if (missingOnDisk.Count > 0)
                    sb.Append("• НЕТ ФАЙЛОВ на диске: ").Append(missingOnDisk.Count).Append(" (").Append(Few(missingOnDisk)).Append(")\n  папка: ").Append(folder)
                      .Append("\n  → импортируйте полный пакет SevernyOkrug ещё раз (Import All)\n");
                if (stillMissing > 0) sb.Append("Материалы без текстур: ").Append(stillMissing).Append('\n');
                if (!string.IsNullOrEmpty(diag)) sb.Append("Диагностика: ").Append(diag).Append('\n');
                sb.Append("Unity ").Append(Application.unityVersion);
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>Make every texture referenced by pk_map.json loadable: reimport, and if Unity still does not give a Texture2D,
        /// convert the file into Resources/PolygonKit/TexturesBaked/NAME.asset (used by the material bake and at runtime).</summary>
        // attempts already made in this editor session (a reimport triggers another check -> never loop)
        static readonly HashSet<string> _reimportTried = new HashSet<string>(), _convertFailed = new HashSet<string>();

        static TexReport RepairTextures(bool force = false)
        {
            if (force) { _reimportTried.Clear(); _convertFailed.Clear(); }
            var r = new TexReport();
            var map = LoadMap();
            if (map == null) return r;
            var names = ReferencedTextures(map);
            r.referenced = names.Count;
            r.folder = Path.GetFullPath(Root + "/Textures");
            var bad = new List<string>();
            foreach (var n in names)
            {
                if (LoadImported(n) != null) r.importer++;
                else if (LoadBaked(n) != null) r.already++;
                else bad.Add(n);
            }
            if (bad.Count == 0) return r;

            // files on disk (by name, anywhere inside the kit folder)
            var files = new Dictionary<string, string>();
            try
            {
                foreach (var f in Directory.GetFiles(Root, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".jpeg" || ext == ".png") files[Path.GetFileNameWithoutExtension(f)] = f.Replace('\\', '/');
                }
            }
            catch (Exception e) { r.diag = "папка не читается: " + e.Message; }
            var present = new List<string>();
            foreach (var n in bad) if (files.ContainsKey(n)) present.Add(n); else r.missingOnDisk.Add(n);
            r.diag = Diagnose(bad[0], files.TryGetValue(bad[0], out var f0) ? f0 : null);
            if (present.Count == 0) return r;

            // 1) forced reimport of one texture; when that fixes it, reimport the rest the same way (once per file and session)
            var fresh = present.FindAll(n => _reimportTried.Add(n));
            if (fresh.Count > 0)
            {
                AssetDatabase.ImportAsset(files[fresh[0]], ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                if (LoadImported(fresh[0]) != null && fresh.Count > 1)
                {
                    AssetDatabase.StartAssetEditing();
                    try { for (int i = 1; i < fresh.Count; i++) AssetDatabase.ImportAsset(files[fresh[i]], ImportAssetOptions.ForceUpdate); }
                    finally { AssetDatabase.StopAssetEditing(); }
                }
            }
            var left = new List<string>();
            foreach (var n in present)
            {
                if (LoadImported(n) != null) r.reimported++;
                else if (_convertFailed.Contains(n)) r.failed.Add(n);
                else left.Add(n);
            }
            if (left.Count == 0) return r;

            // 2) bypass the texture importer: decode the file and save a native Texture2D asset
            if (!AssetDatabase.IsValidFolder(BakedDir)) AssetDatabase.CreateFolder(Root, "TexturesBaked");
            try
            {
                for (int i = 0; i < left.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Северный Округ", "Текстуры карты: " + left[i] + " (" + (i + 1) + "/" + left.Count + ")", (i + 0.5f) / left.Count);
                    if (ConvertFromFile(left[i], files[left[i]]) != null) r.converted++; else { r.failed.Add(left[i]); _convertFailed.Add(left[i]); }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.SaveAssets();
            Debug.Log("[Severny] PolygonKit: Unity не импортировал " + left.Count + " текстур как Texture2D — сконвертировано в " + BakedDir + ": " + r.converted + ". " + r.diag);
            return r;
        }

        static string Diagnose(string name, string file)
        {
            if (file == null) return name + ": файла нет в " + Root + "/Textures";
            var sb = new StringBuilder(Path.GetFileName(file));
            try { sb.Append(" — файл ").Append(new FileInfo(file).Length / 1024).Append(" КБ"); } catch (Exception) { }
            var imp = AssetImporter.GetAtPath(file);
            var type = AssetDatabase.GetMainAssetTypeAtPath(file);
            string guid = AssetDatabase.AssetPathToGUID(file);
            sb.Append(", импортёр: ").Append(imp != null ? imp.GetType().Name : "нет");
            sb.Append(", тип: ").Append(type != null ? type.Name : "нет");
            sb.Append(", guid: ").Append(string.IsNullOrEmpty(guid) ? "нет" : guid.Substring(0, 8));
            try
            {
                if (File.Exists(file + ".meta")) sb.Append(", meta: ").Append(File.ReadAllLines(file + ".meta").Length).Append(" стр.");
                else sb.Append(", meta: нет");
            }
            catch (Exception) { }
            foreach (char c in Path.GetFullPath(file)) if (c > 127) { sb.Append(", в пути проекта есть не-ASCII символы"); break; }
            return sb.ToString();
        }

        static Texture2D ConvertFromFile(string name, string file)
        {
            byte[] data;
            try { data = File.ReadAllBytes(file); } catch (Exception e) { Debug.LogWarning("[Severny] PolygonKit: " + file + ": " + e.Message); return null; }
            bool linear = PolygonKit.IsLinearTex(name);
            var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            try
            {
                if (!ImageConversion.LoadImage(tmp, data, false)) { Debug.LogWarning("[Severny] PolygonKit: не декодируется " + file); return null; }
                var tex = new Texture2D(tmp.width, tmp.height, TextureFormat.RGBA32, true, linear)
                { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                tex.SetPixels32(tmp.GetPixels32());
                tex.Apply(true, false);
                try { EditorUtility.CompressTexture(tex, PolygonKit.TexHasAlpha(name) ? TextureFormat.DXT5 : TextureFormat.DXT1, TextureCompressionQuality.Normal); }
                catch (Exception e) { Debug.LogWarning("[Severny] PolygonKit: сжатие " + name + ": " + e.Message); }
                string dst = BakedDir + "/" + name + ".asset";
                if (File.Exists(dst)) AssetDatabase.DeleteAsset(dst);
                AssetDatabase.CreateAsset(tex, dst);
                return tex;
            }
            catch (Exception e) { Debug.LogWarning("[Severny] PolygonKit: " + name + ": " + e.Message); return null; }
            finally { Object.DestroyImmediate(tmp); }
        }

        // ------------------------------------------------------------------ materials
        /// <summary>Kit materials whose asset is missing, or lacks a map whose texture is loadable now (baked too early).</summary>
        static int CountBrokenMaterials(out string example)
        {
            example = "";
            var map = LoadMap();
            if (map == null) return 0;
            int n = 0;
            foreach (var d in map.materials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + d.name + ".mat");
                bool bad = m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader";
                if (!bad && !PolygonKit.MaterialOk(m, d))
                    bad = LoadTex(d.baseMap) != null || LoadTex(d.normalMap) != null || LoadTex(d.maskMap) != null;
                if (!bad) continue;
                if (n == 0) example = d.name;
                n++;
            }
            return n;
        }

        static PKMap LoadMap()
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/pk_map.json");
            if (ta == null) return null;
            try { var map = JsonUtility.FromJson<PKMap>(ta.text); return map != null && map.materials != null ? map : null; }
            catch (Exception) { return null; }
        }

        const string StampPath = "Library/SevernyPolygonKit.stamp";
        static string MapStamp()
        {
            string p = Root + "/pk_map.json";
            if (!File.Exists(p)) return "";
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return "v3:" + BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(p)));
        }

        /// <summary>Re-apply import settings to all kit textures (reimports only those that differ).</summary>
        public static int FixImports(bool log = true)
        {
            if (!AssetDatabase.IsValidFolder(Root)) return 0;
            int n = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { Root }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (ti == null || !Configure(ti, p)) continue;
                    ti.SaveAndReimport(); n++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            if (log) Debug.Log("[Severny] PolygonKit: переимпортировано текстур: " + n);
            return n;
        }

        /// <summary>Create/update URP Lit material assets for every kit material + the HDRI skybox material.</summary>
        public static int BakeMaterials() => BakeMaterials(out _);

        public static int BakeMaterials(out List<string> missing)
        {
            missing = new List<string>();
            var map = LoadMap();
            if (map == null) return 0;
            if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
            var lit = GameAssets.NewLitMaterial();
            var miss = missing;
            Texture2D Load(string name)
            {
                var t = LoadTex(name);
                if (t == null && !string.IsNullOrEmpty(name) && !miss.Contains(name)) miss.Add(name);
                return t;
            }
            int n = 0;
            foreach (var d in map.materials)
            {
                string path = Root + "/Materials/" + d.name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = m == null;
                if (isNew) m = new Material(lit);
                else if (lit != null && m.shader != lit.shader) m.shader = lit.shader;   // e.g. pink (shader lost) assets
                PolygonKit.Configure(m, d, Load);
                if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
                n++;
            }
            if (lit != null) Object.DestroyImmediate(lit);
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(Root + "/" + (map.env != null && !string.IsNullOrEmpty(map.env.sky) ? map.env.sky : "PK_Sky") + ".hdr");
            var sh = Shader.Find("Skybox/Cubemap");
            if (cube != null && sh != null)
            {
                string sp = Root + "/Materials/PK_SkyMat.mat";
                var sm = AssetDatabase.LoadAssetAtPath<Material>(sp);
                if (sm == null) { sm = new Material(sh) { name = "PK_SkyMat" }; AssetDatabase.CreateAsset(sm, sp); }
                sm.shader = sh;
                sm.SetTexture("_Tex", cube);
                EditorUtility.SetDirty(sm);
                n++;
            }
            else Debug.LogWarning("[Severny] PolygonKit: небо PK_Sky.hdr не импортировано как Cubemap — будет процедурное небо.");
            AssetDatabase.SaveAssets();
            return n;
        }

        /// <summary>Called from Severny > 1 Setup Project and the menu below.</summary>
        public static string SetupKit()
        {
            if (!File.Exists(Root + "/pk_map.json")) return "набор PolygonKit не найден (нет Resources/PolygonKit/pk_map.json) — карта «ТЦ «Волна»» будет процедурной";
            int f = FixImports(false);
            var rep = RepairTextures(true);
            int b = BakeMaterials(out var missing);
            if (missing.Count == 0) { try { File.WriteAllText(StampPath, MapStamp()); } catch (IOException) { } }
            string txt = "Материалов: " + b + ", настройки импорта исправлены у " + f + " текстур.\n" + rep.Text(missing.Count) +
                         (missing.Count == 0 ? "\n\nВсе текстуры на месте." : "");
            Debug.Log("[Severny] PolygonKit:\n" + txt);
            return txt;
        }

        [MenuItem("Severny/Polygon: Reimport Kit Textures + Materials", false, 30)]
        static void MenuFix()
        {
            string r = SetupKit();
            EditorUtility.DisplayDialog("Северный Округ — текстуры карты", r, "OK");
        }
    }
}
