// Map pipeline in the editor:
//   Severny > 3 Place Map        : materials from severny_materials.json, FBX -> Raid_Severny/MAP_ROOT, static flags, colliders, bounds
//   Severny > 3b Re-apply Materials
//   Severny > 4 Bake NavMesh     : NavMeshData asset from MAP_ROOT colliders, assigned to SevernyMapRoot
//   Severny > 5 Validate Map     : report (tris, draw calls, materials, colliders, NavMesh coverage/reachability)
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Severny.EditorTools
{
    public static class SevernyMapTools
    {
        const string MapDir = SevernyFbxPostprocessor.MapDir;
        const string NavAsset = MapDir + "/NavMesh_Severny.asset";

        class FileInfoM { public string file, group, chunk, collider; public bool backdrop; public int tris; }

        static List<FileInfoM> ReadManifest()
        {
            var res = new List<FileInfoM>();
            string p = MapDir + "/severny_map_manifest.json";
            if (!File.Exists(p)) return res;
            var root = SevernyJson.Obj(SevernyJson.Parse(File.ReadAllText(p)));
            var files = SevernyJson.Arr(root != null && root.TryGetValue("files", out var f) ? f : null);
            if (files == null) return res;
            foreach (var o in files)
            {
                var d = SevernyJson.Obj(o); if (d == null) continue;
                res.Add(new FileInfoM
                {
                    file = SevernyJson.S(d, "file"), group = SevernyJson.S(d, "group", "other"), chunk = SevernyJson.S(d, "chunk", ""),
                    collider = SevernyJson.S(d, "collider", "mesh"), backdrop = SevernyJson.B(d, "backdrop"), tris = (int)SevernyJson.N(d, "tris")
                });
            }
            return res;
        }

        static Texture2D Tex(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(MapDir + "/Textures/" + file);
            if (t == null)
            {
                var guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(file) + " t:Texture2D", new[] { MapDir });
                if (guids.Length > 0) t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            return t;
        }

        static Color Lin(float[] c, float a = 1f) => c == null ? Color.white : new Color(c[0], c[1], c[2], a);

        // ------------------------------------------------------------------ materials
        [MenuItem("Severny/3b Re-apply Materials", false, 4)]
        public static void ApplyMaterialsMenu() { int n = ApplyMaterials(out string log); EditorUtility.DisplayDialog("Северный Округ", "Материалов обновлено: " + n + "\n" + log, "OK"); }

        public static int ApplyMaterials(out string log)
        {
            log = "";
            string jp = MapDir + "/severny_materials.json";
            if (!File.Exists(jp)) { log = "Нет файла " + jp; return 0; }
            var all = SevernyJson.Obj(SevernyJson.Parse(File.ReadAllText(jp)));
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (all == null || lit == null) { log = "JSON или URP Lit не найден"; return 0; }
            int n = 0, missingTex = 0;
            var flipNormals = new HashSet<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MapDir }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null || !all.TryGetValue(m.name, out var eo)) continue;
                    var e = SevernyJson.Obj(eo); if (e == null) continue;
                    if (m.shader != lit) m.shader = lit;
                    string bt = SevernyJson.S(e, "base_tex");
                    var baseTex = Tex(bt);
                    if (bt != null && baseTex == null) missingTex++;
                    Color c = baseTex != null ? Lin(SevernyJson.F3(e, "tint")) : Lin(SevernyJson.F3(e, "color"));
                    m.SetTexture("_BaseMap", baseTex); m.SetTexture("_MainTex", baseTex);
                    // material colours are sRGB in Unity (converted to linear for the shader) -> pass gamma of the linear value
                    var g = c.gamma; g.a = 1f;
                    m.SetColor("_BaseColor", g); m.SetColor("_Color", g);
                    m.SetFloat("_Smoothness", Mathf.Clamp01(1f - (float)SevernyJson.N(e, "roughness", 0.7)) * 0.85f);
                    m.SetFloat("_Metallic", Mathf.Clamp01((float)SevernyJson.N(e, "metallic", 0)));
                    m.SetFloat("_WorkflowMode", 1f); m.DisableKeyword("_METALLICSPECGLOSSMAP");
                    var nt = Tex(SevernyJson.S(e, "normal_tex"));
                    if (nt != null)
                    {
                        m.SetTexture("_BumpMap", nt); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
                        if (SevernyJson.B(e, "normal_flip_g")) flipNormals.Add(AssetDatabase.GetAssetPath(nt));
                    }
                    else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
                    bool clip = SevernyJson.B(e, "alpha_clip");
                    m.SetFloat("_AlphaClip", clip ? 1f : 0f); m.SetFloat("_Cutoff", 0.5f);
                    if (clip) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Surface", 0f);
                    m.renderQueue = clip ? (int)RenderQueue.AlphaTest : -1;
                    m.SetOverrideTag("RenderType", clip ? "TransparentCutout" : "Opaque");
                    bool twoSided = SevernyJson.B(e, "two_sided") || clip;
                    m.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
                    m.doubleSidedGI = twoSided;
                    var em = SevernyJson.F3(e, "emission");
                    if (em != null && (em[0] + em[1] + em[2]) > 0.01f)
                    {
                        m.SetColor("_EmissionColor", Lin(em).gamma); m.EnableKeyword("_EMISSION");
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    else { m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); }
                    m.enableInstancing = true;
                    EditorUtility.SetDirty(m); n++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            int flipped = 0;
            foreach (var p in flipNormals)
            {
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                bool ch = false;
                if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ch = true; }
                if (!ti.flipGreenChannel) { ti.flipGreenChannel = true; ch = true; }
                if (ch) { ti.SaveAndReimport(); flipped++; }
            }
            AssetDatabase.SaveAssets();
            log = "без текстуры (не найдена): " + missingTex + ", normal maps с инверсией G: " + flipNormals.Count + " (переимпорт " + flipped + ")";
            Debug.Log("[Severny] Materials applied: " + n + "; " + log);
            return n;
        }

        // ------------------------------------------------------------------ place map
        [MenuItem("Severny/3 Place Map (FBX -> Raid_Severny)", false, 3)]
        public static void PlaceMap()
        {
            var fbx = AssetDatabase.FindAssets("t:Model", new[] { MapDir }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.ToLowerInvariant().EndsWith(".fbx")).OrderBy(p => p).ToList();
            if (fbx.Count == 0)
            {
                EditorUtility.DisplayDialog("Северный Округ", "В " + MapDir + " нет FBX.\n\nВ Blender запустите экспорт (scripts/blender/s08_export_unity_fbx.py) и скопируйте содержимое папки Map (FBX, Textures, severny_*.json) в " + MapDir + ".", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(SevernyMenu.RaidScene)) SevernyMenu.CreateScenes();
            int mats = ApplyMaterials(out string mlog);
            var scene = EditorSceneManager.OpenScene(SevernyMenu.RaidScene, OpenSceneMode.Single);
            var rootGo = scene.GetRootGameObjects().FirstOrDefault(g => g.GetComponent<SevernyMapRoot>() != null) ?? new GameObject("MAP_ROOT");
            var mapRoot = rootGo.GetComponent<SevernyMapRoot>() ?? rootGo.AddComponent<SevernyMapRoot>();
            mapRoot.mapId = "severny";
            Transform Child(Transform p, string name) { var t = p.Find(name); if (t == null) { t = new GameObject(name).transform; t.SetParent(p, false); } return t; }
            var backdrop = Child(rootGo.transform, "BACKDROP");
            // the city is re-placed: old winter snow would hang in the air (the next build / Severny > 10 bakes it again)
            for (var snow = rootGo.transform.Find(Winter.BakedName); snow != null; snow = rootGo.transform.Find(Winter.BakedName)) Object.DestroyImmediate(snow.gameObject);
            // remove previous FBX instances (re-run safe)
            foreach (var r in rootGo.GetComponentsInChildren<Transform>(true).Where(t => t != rootGo.transform && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)).ToList())
                Object.DestroyImmediate(r.gameObject);

            var manifest = ReadManifest().ToDictionary(x => x.file, x => x);
            int placed = 0, colliders = 0, boxes = 0; long tris = 0;
            try
            {
                for (int i = 0; i < fbx.Count; i++)
                {
                    string path = fbx[i], file = Path.GetFileName(path);
                    EditorUtility.DisplayProgressBar("Северный Округ", "Размещение " + file, (float)i / fbx.Count);
                    manifest.TryGetValue(file, out var info);
                    string group = info != null ? info.group : GuessGroup(file);
                    bool isBackdrop = info != null ? info.backdrop : group == "backdrop";
                    string col = info != null ? info.collider : (group == "foliage" || group == "backdrop" ? "none" : "mesh");
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    var parent = isBackdrop ? backdrop : Child(rootGo.transform, group);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    placed++;
                    var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                    if (group == "buildings" || group == "ground" || group == "roads") flags |= StaticEditorFlags.OccluderStatic;
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null) continue;
                        tris += TriCount(mf.sharedMesh);
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mr != null)
                        {
                            mr.shadowCastingMode = isBackdrop || group == "signs" && mf.sharedMesh.bounds.size.magnitude < 0.6f ? ShadowCastingMode.Off : ShadowCastingMode.On;
                            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                        }
                        if (col == "mesh" && !isBackdrop && mf.GetComponent<Collider>() == null)
                        {
                            // single cars (763 separate SM_Car_* objects, ~1.7M tris) get a fitted BoxCollider: far cheaper to cook/raycast
                            if (group == "cars" && LongestSide(mf.sharedMesh) < 12f) { mf.gameObject.AddComponent<BoxCollider>(); boxes++; }
                            else { mf.gameObject.AddComponent<MeshCollider>(); colliders++; }
                        }
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            // play bounds = everything except the backdrop, slightly shrunk
            mapRoot.hasPlayBounds = false;
            var b = mapRoot.ComputeBounds();
            b.Expand(new Vector3(-4f, 0f, -4f));
            mapRoot.playBounds = b; mapRoot.hasPlayBounds = true;
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavAsset) != null) mapRoot.navMesh = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavAsset);
            EditorUtility.SetDirty(mapRoot);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (placed > 0) SetPlacedFlag(true);
            string msg = "FBX размещено: " + placed + "\nТреугольников: " + (tris / 1e6).ToString("0.00") + " млн\nMeshCollider: " + colliders + " · BoxCollider (машины): " + boxes +
                         "\nМатериалов настроено: " + mats + " (" + mlog + ")\nГраницы игровой зоны: " + b.size.ToString("F0") +
                         "\n\nДальше: Severny > 4 Bake NavMesh, затем Severny > 5 Validate.";
            Debug.Log("[Severny] Place map: " + msg.Replace("\n", " | "));
            EditorUtility.DisplayDialog("Северный Округ", msg, "OK");
        }

        // triangle count without reading mesh data (works for meshes with Read/Write disabled)
        static long TriCount(Mesh m) { long n = 0; for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3; return n; }
        static float LongestSide(Mesh m) { var s = m.bounds.size; return Mathf.Max(s.x, Mathf.Max(s.y, s.z)); }

        static string GuessGroup(string file)
        {
            string n = Path.GetFileNameWithoutExtension(file);
            int k = n.LastIndexOf('_');
            return k >= 0 ? n.Substring(k + 1) : "other";
        }

        // ------------------------------------------------------------------ navmesh
        public static NavMeshBuildSettings AgentSettings()
        {
            var s = NavMesh.GetSettingsByID(0);
            s.agentRadius = 0.35f; s.agentHeight = 1.8f; s.agentClimb = 0.45f; s.agentSlope = 40f;
            s.minRegionArea = 6f;
            s.overrideVoxelSize = true; s.voxelSize = 0.12f;
            s.overrideTileSize = true; s.tileSize = 256;
            return s;
        }

        /// <summary>Remembers in SevernyGameAssets whether Raid_Severny contains the city (menu shows/selects the map accordingly).</summary>
        static void SetPlacedFlag(bool placed)
        {
            var so = AssetDatabase.LoadAssetAtPath<SevernyGameAssets>(SevernyMenu.AssetsSO);
            if (so == null || so.severnyMapPlaced == placed) return;
            so.severnyMapPlaced = placed;
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Severny/4 Bake NavMesh", false, 5)]
        public static void BakeNavMesh()
        {
            var root = Object.FindObjectsByType<SevernyMapRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (root == null)
            {
                if (File.Exists(SevernyMenu.RaidScene) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(SevernyMenu.RaidScene, OpenSceneMode.Single);
                    root = Object.FindObjectsByType<SevernyMapRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
                }
                if (root == null) { EditorUtility.DisplayDialog("Северный Округ", "Нет объекта с SevernyMapRoot. Выполните Severny > 3.", "OK"); return; }
            }
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            var bd = root.transform.Find("BACKDROP"); if (bd != null) markups.Add(new NavMeshBuildMarkup { root = bd, ignoreFromBuild = true });
            var fol = root.transform.Find("foliage"); if (fol != null) markups.Add(new NavMeshBuildMarkup { root = fol, ignoreFromBuild = true });
            NavMeshBuilder.CollectSources(root.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            if (sources.Count == 0) { EditorUtility.DisplayDialog("Северный Округ", "Нет коллайдеров под MAP_ROOT. Выполните Severny > 3.", "OK"); return; }
            // city dress (Resources/SevernyDress): its colliders (containers, garages, fences, crates…) become Not Walkable areas
            Transform dress = null; int dressSources = 0;
            try
            {
                if (SevernyDress.Available)
                {
                    dress = SevernyDress.BuildColliders(null, root.transform);
                    if (dress != null)
                    {
                        var ds = new List<NavMeshBuildSource>();
                        NavMeshBuilder.CollectSources(dress, ~0, NavMeshCollectGeometry.PhysicsColliders, 1, new List<NavMeshBuildMarkup>(), ds);
                        dressSources = ds.Count; sources.AddRange(ds);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Severny] Детализация города не учтена в NavMesh: " + e.Message); dressSources = 0;
                if (dress != null) { Object.DestroyImmediate(dress.gameObject); dress = null; }
            }
            var b = root.ComputeBounds(); b.Expand(new Vector3(6f, 30f, 6f));
            var t0 = System.DateTime.Now;
            EditorUtility.DisplayProgressBar("Северный Округ", "Построение NavMesh (" + sources.Count + " источников) — может занять 1–3 минуты…", 0.3f);
            NavMeshData data;
            try { data = NavMeshBuilder.BuildNavMeshData(AgentSettings(), sources, b, Vector3.zero, Quaternion.identity); }
            finally { EditorUtility.ClearProgressBar(); if (dress != null) Object.DestroyImmediate(dress.gameObject); }
            if (data == null) { EditorUtility.DisplayDialog("Северный Округ", "NavMesh не построен (см. Console).", "OK"); return; }
            data.name = "NavMesh_Severny";
            var old = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavAsset);
            if (old != null) AssetDatabase.DeleteAsset(NavAsset);
            AssetDatabase.CreateAsset(data, NavAsset);
            root.navMesh = data;
            root.dressVersion = dressSources > 0 ? SevernyDress.Version : "";
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            EditorSceneManager.SaveScene(root.gameObject.scene);
            SetPlacedFlag(root.GetComponentInChildren<Renderer>(true) != null);
            var inst = NavMesh.AddNavMeshData(data);
            var tri = NavMesh.CalculateTriangulation();
            float area = 0f; for (int i = 0; i + 2 < tri.indices.Length; i += 3) area += Vector3.Cross(tri.vertices[tri.indices[i + 1]] - tri.vertices[tri.indices[i]], tri.vertices[tri.indices[i + 2]] - tri.vertices[tri.indices[i]]).magnitude * 0.5f;
            inst.Remove();
            string msg = "NavMesh построен за " + (System.DateTime.Now - t0).TotalSeconds.ToString("0") + " c\nИсточников: " + sources.Count + "\nТреугольников NavMesh: " + tri.indices.Length / 3 + "\nПлощадь: " + (area / 1e4f).ToString("0.0") + " га\nФайл: " + NavAsset +
                         (dressSources > 0 ? "\nДетализация города " + SevernyDress.Version + ": " + dressSources + " препятствий, выравнивание " + SevernyDress.LastAlign : "");
            Debug.Log("[Severny] " + msg.Replace("\n", " | "));
            EditorUtility.DisplayDialog("Северный Округ", msg + "\n\nДальше: Severny > 5 Validate Map, затем Play в Severny_Main.", "OK");
        }

        // ------------------------------------------------------------------ validate
        [MenuItem("Severny/5 Validate Map", false, 6)]
        public static void Validate()
        {
            var root = Object.FindObjectsByType<SevernyMapRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (root == null) { EditorUtility.DisplayDialog("Северный Округ", "Откройте сцену Raid_Severny (Severny > Open Raid Scene).", "OK"); return; }
            SetPlacedFlag(root.GetComponentInChildren<Renderer>(true) != null);
            var sb = new StringBuilder();
            sb.AppendLine("SEVERNY OKRUG — map validation " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            var bounds = root.ComputeBounds();
            sb.AppendLine("Play bounds: center " + bounds.center.ToString("F0") + " size " + bounds.size.ToString("F0"));
            var groups = new SortedDictionary<string, (int renderers, long tris, int subs, int cols)>();
            var noTex = new HashSet<string>(); var wrongShader = new HashSet<string>(); int missingCollider = 0;
            var manifest = ReadManifest();
            var noColGroups = new HashSet<string>(manifest.Where(m => m.collider != "mesh").Select(m => m.group)) { "foliage", "BACKDROP" };
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                string g = GroupOf(mr.transform, root.transform);
                groups.TryGetValue(g, out var v);
                v.renderers++; v.tris += TriCount(mf.sharedMesh); v.subs += mf.sharedMesh.subMeshCount;
                if (mr.GetComponent<Collider>() != null) v.cols++; else if (!noColGroups.Contains(g)) missingCollider++;
                groups[g] = v;
                foreach (var m in mr.sharedMaterials)
                {
                    if (m == null) { noTex.Add("<null material> on " + mr.name); continue; }
                    if (m.shader == null || !m.shader.name.StartsWith("Universal Render Pipeline")) wrongShader.Add(m.name + " (" + (m.shader != null ? m.shader.name : "null") + ")");
                }
            }
            long totalTris = 0; int totalSubs = 0;
            sb.AppendLine("\nGroup            renderers   tris        draw(submeshes)  colliders");
            foreach (var kv in groups)
            {
                sb.AppendLine(kv.Key.PadRight(16) + kv.Value.renderers.ToString().PadLeft(9) + (kv.Value.tris / 1000f).ToString("0.0").PadLeft(10) + "k" + kv.Value.subs.ToString().PadLeft(16) + kv.Value.cols.ToString().PadLeft(11));
                totalTris += kv.Value.tris; totalSubs += kv.Value.subs;
            }
            sb.AppendLine("TOTAL tris " + (totalTris / 1e6).ToString("0.00") + "M, draw calls before batching ~" + totalSubs + " (SRP Batcher + static batching reduce CPU cost)");
            if (totalTris > 6e6) sb.AppendLine("WARN: > 6M triangles in the scene — for 1080p/60 on RTX 5050 consider LODs / decimating props & signs.");
            if (totalSubs > 4000) sb.AppendLine("WARN: > 4000 draw calls before batching — check SetPass/Batches with F3 in play mode.");
            if (missingCollider > 0) sb.AppendLine("WARN: " + missingCollider + " renderers without collider in collidable groups.");
            if (wrongShader.Count > 0) sb.AppendLine("WARN: non-URP materials: " + string.Join(", ", wrongShader.Take(20)) + " -> Severny > 3b");
            if (noTex.Count > 0) sb.AppendLine("WARN: " + string.Join("; ", noTex.Take(10)));
            // NavMesh
            if (root.navMesh == null) sb.AppendLine("\nNavMesh: NOT BAKED (Severny > 4). In play mode a runtime NavMesh would be built (slow on big maps).");
            else
            {
                var inst = NavMesh.AddNavMeshData(root.navMesh);
                var tri = NavMesh.CalculateTriangulation();
                int samples = 0, onNav = 0, reach = 0, tested = 0;
                var rng = new System.Random(7);
                Vector3 origin = bounds.center;
                if (NavMesh.SamplePosition(bounds.center, out var oh, 60f, NavMesh.AllAreas)) origin = oh.position;
                var path = new NavMeshPath();
                for (int i = 0; i < 400; i++)
                {
                    var q = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (float)rng.NextDouble()), bounds.max.y + 5f, Mathf.Lerp(bounds.min.z, bounds.max.z, (float)rng.NextDouble()));
                    if (!Physics.Raycast(q, Vector3.down, out var hit, bounds.size.y + 40f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    samples++;
                    if (!NavMesh.SamplePosition(hit.point, out var nh, 1.5f, NavMesh.AllAreas)) continue;
                    onNav++;
                    if (tested < 150) { tested++; if (NavMesh.CalculatePath(origin, nh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) reach++; }
                }
                inst.Remove();
                sb.AppendLine("\nNavMesh: " + tri.indices.Length / 3 + " tris; ground samples on NavMesh: " + onNav + "/" + samples + " (" + (samples > 0 ? 100f * onNav / samples : 0f).ToString("0") + "%, roofs/water/props lower this)");
                sb.AppendLine("Reachable from centre: " + reach + "/" + tested + " (" + (tested > 0 ? 100f * reach / tested : 0f).ToString("0") + "%). Low values = closed yards / islands: bots and exits there are unreachable.");
                if (tested > 0 && reach < tested * 0.6f) sb.AppendLine("WARN: less than 60% reachable — check fences/closed areas or raise agentClimb.");
            }
            string report = sb.ToString();
            File.WriteAllText(MapDir + "/validation_report.txt", report);
            AssetDatabase.ImportAsset(MapDir + "/validation_report.txt");
            Debug.Log("[Severny] " + report);
            EditorUtility.DisplayDialog("Северный Округ — проверка карты", report.Length > 1800 ? report.Substring(0, 1800) + "\n…(полный отчёт: " + MapDir + "/validation_report.txt)" : report, "OK");
        }

        static string GroupOf(Transform t, Transform root)
        {
            while (t != null && t.parent != root) t = t.parent;
            return t != null ? t.name : "?";
        }
    }
}
