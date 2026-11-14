// Menu Severny > 10 Winter: Bake Snow — builds the snow caps (roofs, ledges, car roofs, containers…) for the FBX city once
// in the editor and stores them in the raid scene (inactive child SEV_WinterSnow of MAP_ROOT + mesh asset).
// Winter raids switch the child on. Severny > 6 Build bakes automatically when the bake is missing (EnsureForBuild),
// so every .exe has winter snow. Runtime maps (ТЦ «Волна», greybox, tutorial) build their caps while loading.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny.EditorTools
{
    public static class SevernyWinterBake
    {
        const string Dir = SevernyMenu.Root + "/Map/Winter";
        public const string MeshAsset = Dir + "/SEV_SnowCaps.asset";
        const string Title = "Северный Округ — зима";

        [MenuItem("Severny/10 Winter: Bake Snow", false, 11)]
        public static void Bake()
        {
            BakeCore(true, true, out _);
        }

        /// <summary>Builds and saves the caps. interactive: dialogs (else Console only). Returns true when the bake was saved.</summary>
        public static bool BakeCore(bool interactive, bool askSave, out string msg)
        {
            msg = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { msg = "Остановите Play Mode и повторите."; Report(interactive, msg, true); return false; }
            var root = FindRoot(interactive, askSave, out msg, out var opened);
            if (root == null) { CloseOpened(opened); return false; }
            try { return BakeInto(root, interactive, out msg); }
            finally { CloseOpened(opened); }
        }

        /// <summary>The raid scene opened additively for a build-time bake is closed again (nothing of the user's work is touched).</summary>
        static void CloseOpened(UnityEngine.SceneManagement.Scene s)
        {
            if (s.IsValid() && s.isLoaded && EditorSceneManager.sceneCount > 1) EditorSceneManager.CloseScene(s, true);
        }

        static bool BakeInto(SevernyMapRoot root, bool interactive, out string msg)
        {
            msg = null;
            var t0 = System.DateTime.Now;
            RemoveBaked(root);

            var res = Run(root.transform, out bool cancelled);
            if (cancelled) { msg = "Запекание снега отменено."; Report(interactive, msg, true); return false; }

            // meshes the editor could not read: offer to enable Read/Write on their models for the bake (restored afterwards)
            if (res.unreadable.Count > 0 && interactive)
            {
                var paths = res.unreadable.Where(m => m != null).Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
                if (paths.Count > 0 && EditorUtility.DisplayDialog(Title, "Не удалось прочитать " + res.unreadable.Count + " мешей из " + paths.Count + " моделей (Read/Write выключен).\n\n" +
                        "Временно включить Read/Write для этих моделей и повторить? Это переимпорт моделей (может занять несколько минут), после запекания настройка вернётся обратно.", "Да", "Нет, оставить так"))
                {
                    var changed = SetReadable(paths, true);
                    try
                    {
                        foreach (var m in res.meshes) Object.DestroyImmediate(m);
                        res = Run(root.transform, out cancelled);
                    }
                    finally { SetReadable(changed, false); }
                    if (cancelled) { msg = "Запекание снега отменено."; Report(interactive, msg, true); return false; }
                }
            }
            if (res.meshes.Count == 0)
            {
                msg = "Не найдено ни одной поверхности под снег (рендереров: " + res.renderers + ", нечитаемых мешей: " + res.unreadable.Count + ").\nПроверьте, что город размещён (Severny > 3).";
                Report(interactive, msg, true);
                return false;
            }

            // save meshes into one asset
            Directory.CreateDirectory(Dir);
            if (AssetDatabase.LoadMainAssetAtPath(MeshAsset) != null) AssetDatabase.DeleteAsset(MeshAsset);
            AssetDatabase.CreateAsset(res.meshes[0], MeshAsset);
            for (int i = 1; i < res.meshes.Count; i++) AssetDatabase.AddObjectToAsset(res.meshes[i], MeshAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MeshAsset);

            // the caps were built in world space: the container keeps a world identity pose under MAP_ROOT
            var go = new GameObject(Winter.BakedName);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.SetParent(root.transform, true);
            go.layer = root.gameObject.layer;
            foreach (var m in res.meshes)
            {
                var c = new GameObject(m.name);
                c.layer = go.layer;
                c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = m;
                var r = c.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Simple;
                r.allowOcclusionWhenDynamic = true;
            }
            go.SetActive(false);                                                    // switched on by winter raids only
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            EditorSceneManager.SaveScene(root.gameObject.scene);

            msg = "Снег запечён за " + (System.DateTime.Now - t0).TotalSeconds.ToString("0") + " с.\n\n" +
                  "Ячеек: " + res.meshes.Count + " · треугольников снега: " + res.tris.ToString("N0") + "\n" +
                  "Поверхностей: " + res.renderers + " · кусочков под крышей/навесом (без снега): " + res.covered + " из " + res.candidates + "\n" +
                  (res.unreadable.Count > 0 ? "Не прочитано мешей: " + res.unreadable.Count + "\n" : "") +
                  "\nФайл: " + MeshAsset + "\nОбъект: MAP_ROOT/" + Winter.BakedName + " (выключен; включается в зимних рейдах).\n\n" +
                  "Если город перестроили (Severny > 3) — снег удалится, и сборка (Severny > 6) запечёт его заново сама.";
            Report(interactive, msg, false);
            return true;
        }

        [MenuItem("Severny/10b Winter: Remove Baked Snow", false, 12)]
        public static void Remove()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var root = FindRoot(true, true, out _, out _);
            if (root == null) return;
            RemoveBaked(root);
            if (AssetDatabase.LoadMainAssetAtPath(MeshAsset) != null) AssetDatabase.DeleteAsset(MeshAsset);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            EditorSceneManager.SaveScene(root.gameObject.scene);
            EditorUtility.DisplayDialog(Title, "Запечённый снег удалён.", "OK");
        }

        // ------------------------------------------------------------------ build hook
        /// <summary>Before a player build: bakes the city snow when the city goes into the build and the bake is missing.
        /// Returns true when it baked now. The previously open scene is reopened afterwards.</summary>
        public static bool EnsureForBuild(bool interactive)
        {
            var so = AssetDatabase.LoadAssetAtPath<SevernyGameAssets>(SevernyMenu.AssetsSO);
            if (so == null || !so.severnyMapPlaced || !File.Exists(SevernyMenu.RaidScene)) return false;    // no city in the build
            if (IsBaked()) return false;
            Debug.Log("[Severny] Winter snow for the city is not baked yet — baking it before the build (1–5 min).");
            bool ok = BakeCore(false, false, out var msg);
            Debug.Log("[Severny] Winter bake before build: " + (ok ? "done" : "skipped") + (msg != null ? " — " + msg.Replace("\n", " | ") : ""));
            return ok;
        }

        /// <summary>The raid scene has MAP_ROOT/SEV_WinterSnow and the mesh asset exists.</summary>
        public static bool IsBaked()
        {
            if (!File.Exists(MeshAsset) || !File.Exists(SevernyMenu.RaidScene)) return false;
            try
            {
                using (var sr = new StreamReader(SevernyMenu.RaidScene))
                {
                    string first = sr.ReadLine();
                    if (first != null && first.StartsWith("%YAML"))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null) if (line.Contains("m_Name: " + Winter.BakedName) && !line.Contains(Winter.BakedName + "_")) return true;
                        return false;
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Severny] Winter bake check: " + e.Message); }
            // binary scene: look inside an opened copy
            var open = EditorSceneManager.GetSceneByPath(SevernyMenu.RaidScene);
            bool wasOpen = open.IsValid() && open.isLoaded;
            var s = wasOpen ? open : EditorSceneManager.OpenScene(SevernyMenu.RaidScene, OpenSceneMode.Additive);
            bool baked = false;
            foreach (var g in s.GetRootGameObjects())
            {
                var r = g.GetComponent<SevernyMapRoot>();
                if (r != null) { var t = r.transform.Find(Winter.BakedName); baked = t != null && t.childCount > 0; break; }
            }
            if (!wasOpen) EditorSceneManager.CloseScene(s, true);
            return baked;
        }

        // ------------------------------------------------------------------ helpers
        static void Report(bool interactive, string msg, bool warn)
        {
            if (warn) Debug.LogWarning("[Severny] " + msg.Replace("\n", " | ")); else Debug.Log("[Severny] " + msg.Replace("\n", " | "));
            if (interactive) EditorUtility.DisplayDialog(Title, msg, "OK");
        }

        /// <summary>askSave: menu use — offer to save, then open the raid scene in place of the current one.
        /// Otherwise (build) the raid scene is opened additively and returned in `opened` to be closed afterwards.</summary>
        static SevernyMapRoot FindRoot(bool interactive, bool askSave, out string msg, out UnityEngine.SceneManagement.Scene opened)
        {
            msg = null; opened = default;
            var root = Object.FindObjectsByType<SevernyMapRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(r => r.mapId == "severny");
            if (root == null)
            {
                if (!File.Exists(SevernyMenu.RaidScene)) { msg = "Нет сцены рейда. Выполните Severny > 2 и Severny > 3."; Report(interactive, msg, true); return null; }
                if (askSave)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
                    EditorSceneManager.OpenScene(SevernyMenu.RaidScene, OpenSceneMode.Single);
                }
                else opened = EditorSceneManager.OpenScene(SevernyMenu.RaidScene, OpenSceneMode.Additive);
                root = Object.FindObjectsByType<SevernyMapRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(r => r.mapId == "severny");
            }
            if (root == null || root.GetComponentInChildren<MeshRenderer>(true) == null)
            {
                msg = "Город ещё не размещён (нет MAP_ROOT с моделями). Выполните Severny > 3 Place Map.\n\n" +
                      "ТЦ «Волна» и учебная карта строят снег сами при загрузке — для них запекать не нужно.";
                Report(interactive, msg, true);
                return null;
            }
            return root;
        }

        public static void RemoveBaked(SevernyMapRoot root)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                var old = root.transform.Find(Winter.BakedName);
                if (old == null) break;
                Object.DestroyImmediate(old.gameObject);
            }
        }

        static SnowCapBuilder.Result Run(Transform root, out bool cancelled)
        {
            cancelled = false;
            var res = new SnowCapBuilder.Result();
            float prog = 0f;
            var it = SnowCapBuilder.Build(root, false, res, t => prog = t, 60f);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (it.MoveNext())
                {
                    if (sw.ElapsedMilliseconds < 100) continue;
                    sw.Restart();
                    if (EditorUtility.DisplayCancelableProgressBar(Title, "Запекание снега на крышах и машинах… " + Mathf.RoundToInt(prog * 100f) + "%  (поверхностей: " + res.renderers + ")", prog))
                    {
                        cancelled = true;
                        foreach (var m in res.meshes) Object.DestroyImmediate(m);
                        res.meshes.Clear();
                        break;
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            return res;
        }

        /// <summary>Sets Read/Write on the given models; returns the paths that were actually changed.</summary>
        static List<string> SetReadable(List<string> paths, bool on)
        {
            var changed = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < paths.Count; i++)
                {
                    var mi = AssetImporter.GetAtPath(paths[i]) as ModelImporter;
                    if (mi == null || mi.isReadable == on) continue;
                    EditorUtility.DisplayProgressBar(Title, (on ? "Включение" : "Возврат") + " Read/Write: " + Path.GetFileName(paths[i]), (float)i / paths.Count);
                    mi.isReadable = on;
                    mi.SaveAndReimport();
                    changed.Add(paths[i]);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }
            return changed;
        }
    }
}
