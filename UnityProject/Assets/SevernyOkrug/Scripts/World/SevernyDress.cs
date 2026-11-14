// "Dress" of the Severny city (v09): props, decals, garages, checkpoints, interiors and loot / bot / spawn / exit anchors
// generated in Blender space over the placed city and loaded at raid start from Resources/SevernyDress
// (sd_geo.bytes = PKG1 meshes: prototypes + baked decal chunks, sd_map.json = materials, chunks, instances, colliders, anchors).
// Instances are merged per chunk (class × cell) and culled by distance with an LODGroup: small debris ~40 m,
// weeds ~45 m, medium props ~90 m, decals 70–120 m, large props / garages ~240 m.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Severny
{
    [Serializable] public class SDProto { public string name, cls; public int mesh; }
    [Serializable] public class SDChunk { public string cls; public float cull; public int shadow, mesh; }
    [Serializable] public class SDInstance { public int p, c; public float[] m; }
    [Serializable] public class SDBox { public float[] c, s; public float yaw; }
    [Serializable] public class SDRef { public string name; public float[] c, s; }
    [Serializable] public class SDMap
    {
        public int version; public string build, source, title; public float[] boundsMin, boundsMax;
        public PKMaterial[] materials; public SDProto[] protos; public SDChunk[] chunks; public SDInstance[] instances; public SDBox[] boxes; public PKAnchor[] anchors; public SDRef[] refs;
    }

    public static class SevernyDress
    {
        public const string Res = "SevernyDress/";
        static SDMap _map;
        static bool _tried;
        static readonly List<Mesh> _meshes = new List<Mesh>();

        public static Transform Root { get; private set; }
        /// <summary>Colliders of the dress (containers, garages, fences, crates…): NavMesh "Not Walkable" sources.</summary>
        public static Transform NoWalkRoot { get; private set; }
        public static string LastAlign { get; private set; } = "";

        public static SDMap Map
        {
            get
            {
                if (_tried) return _map;
                _tried = true;
                var t = Resources.Load<TextAsset>(Res + "sd_map");
                if (t == null) return null;
                try { _map = JsonUtility.FromJson<SDMap>(t.text); }
                catch (Exception e) { Debug.LogError("[Severny] SevernyDress: sd_map.json не читается: " + e.Message); _map = null; }
                if (_map != null && (_map.materials == null || _map.protos == null || _map.chunks == null || _map.instances == null)) { Debug.LogError("[Severny] SevernyDress: sd_map.json неполный"); _map = null; }
                Resources.UnloadAsset(t);
                return _map;
            }
        }

        public static bool Available => Map != null && Map.version >= 1;
        /// <summary>Build id of the dress; stored on SevernyMapRoot by Severny > 4 Bake NavMesh (then no runtime carving is needed).</summary>
        public static string Version => Map != null ? (Map.build ?? "") : "";

        static Vector3 V3(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        static Transform Child(Transform p, string n) { var t = new GameObject(n).transform; t.SetParent(p, false); return t; }

        // ------------------------------------------------------------------ alignment to the placed city
        /// <summary>Rigid fit (yaw + translation) of the dress frame onto the placed SM_Bld_* buildings of the city
        /// (their renderer bounds centres vs the reference centres exported with the dress).</summary>
        public static void Align(Transform cityRoot, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f; LastAlign = "default";
            var map = Map;
            if (cityRoot != null) { pos = cityRoot.position; yaw = cityRoot.eulerAngles.y; }
            if (map == null || map.refs == null || map.refs.Length == 0 || cityRoot == null) return;
            var want = new Dictionary<string, SDRef>();
            foreach (var r in map.refs) if (r != null && !string.IsNullOrEmpty(r.name)) want[r.name] = r;
            var found = new Dictionary<string, Bounds>();
            foreach (var mr in cityRoot.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.enabled) continue;
                string n = mr.gameObject.name;
                int cut = n.IndexOf('.'); if (cut > 0) n = n.Substring(0, cut);
                if (n.EndsWith(" (1)")) n = n.Substring(0, n.Length - 4);
                if (!want.ContainsKey(n)) continue;
                if (found.TryGetValue(n, out var b)) { b.Encapsulate(mr.bounds); found[n] = b; } else found[n] = mr.bounds;
            }
            var E = new List<Vector3>(); var A = new List<Vector3>();
            foreach (var kv in found)
            {
                var r = want[kv.Key]; var s = V3(r.s); var bs = kv.Value.size;
                // same footprint (either orientation) and height, otherwise it is a different object with the same name
                float a1 = Mathf.Max(s.x, s.z), a2 = Mathf.Min(s.x, s.z), b1 = Mathf.Max(bs.x, bs.z), b2 = Mathf.Min(bs.x, bs.z);
                if (Mathf.Abs(a1 - b1) > 1.5f + a1 * 0.1f || Mathf.Abs(a2 - b2) > 1.5f + a2 * 0.1f || Mathf.Abs(s.y - bs.y) > 2f + s.y * 0.15f) continue;
                E.Add(V3(r.c)); A.Add(kv.Value.center);
            }
            if (E.Count < 3) { LastAlign = "default (найдено зданий: " + E.Count + ")"; return; }
            Vector3 ce = Vector3.zero, ca = Vector3.zero;
            for (int i = 0; i < E.Count; i++) { ce += E[i]; ca += A[i]; }
            ce /= E.Count; ca /= E.Count;
            // 2D Kabsch in the XZ plane
            double sxx = 0, sxz = 0, szx = 0, szz = 0;
            for (int i = 0; i < E.Count; i++)
            {
                var e = E[i] - ce; var a = A[i] - ca;
                sxx += e.x * a.x; sxz += e.x * a.z; szx += e.z * a.x; szz += e.z * a.z;
            }
            double th = Math.Atan2(sxz - szx, sxx + szz);                 // CCW angle in (x,z) taking e onto a
            float psi = (float)(-th * Mathf.Rad2Deg);                     // Unity yaw rotates (x,z) clockwise
            if (Mathf.Abs(Mathf.DeltaAngle(0f, psi)) < 0.05f) psi = 0f;
            var q = Quaternion.Euler(0f, psi, 0f);
            var dys = new List<float>();
            for (int i = 0; i < E.Count; i++) dys.Add(A[i].y - E[i].y);
            dys.Sort();
            float dy = dys[dys.Count / 2];
            var p = ca - q * ce; p.y = dy;
            double err = 0;
            for (int i = 0; i < E.Count; i++) { var d = q * E[i] + p - A[i]; d.y = 0f; err += d.sqrMagnitude; }
            err = Math.Sqrt(err / E.Count);
            if (p.sqrMagnitude < 0.0025f) p = Vector3.zero;
            pos = p; yaw = psi;
            LastAlign = "по " + E.Count + " зданиям: сдвиг " + p.ToString("F2") + ", поворот " + psi.ToString("F2") + "°, ошибка " + err.ToString("F2") + " м";
            if (err > 2.0) Debug.LogWarning("[Severny] SevernyDress: детализация плохо совпадает с картой (" + LastAlign + ") — карта переэкспортирована или сдвинута?");
        }

        // ------------------------------------------------------------------ build
        static Matrix4x4 M(float[] m)
        {
            var r = Matrix4x4.identity;
            if (m == null || m.Length < 12) return r;
            r.m00 = m[0]; r.m01 = m[1]; r.m02 = m[2]; r.m03 = m[3];
            r.m10 = m[4]; r.m11 = m[5]; r.m12 = m[6]; r.m13 = m[7];
            r.m20 = m[8]; r.m21 = m[9]; r.m22 = m[10]; r.m23 = m[11];
            return r;
        }

        /// <param name="carve">add carving NavMeshObstacles to the colliders (the baked NavMesh does not know the dress yet)</param>
        public static Transform Build(Transform parent, Transform cityRoot, bool carve)
        {
            var map = Map;
            if (map == null) throw new Exception("SevernyDress: нет Resources/SevernyDress/sd_map.json");
            var ta = Resources.Load<TextAsset>(Res + "sd_geo");
            if (ta == null) throw new Exception("SevernyDress: нет Resources/SevernyDress/sd_geo.bytes");
            float t0 = Time.realtimeSinceStartup;
            Unload();
            var geo = PolygonKit.ParseGeo(ta.bytes);
            Resources.UnloadAsset(ta);
            var mats = new Material[map.materials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = PolygonKit.MaterialFor(map.materials[i]);

            Align(cityRoot, out var apos, out var ayaw);
            var root = new GameObject("SevernyDress").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(apos, Quaternion.Euler(0f, ayaw, 0f));
            Root = root;
            var chunksRoot = Child(root, "Chunks");

            var byChunk = new List<int>[map.chunks.Length];
            for (int i = 0; i < map.instances.Length; i++)
            {
                var it = map.instances[i];
                if (it == null || it.c < 0 || it.c >= byChunk.Length || it.p < 0 || it.p >= map.protos.Length) continue;
                (byChunk[it.c] ?? (byChunk[it.c] = new List<int>())).Add(i);
            }
            float tanHalf = Mathf.Tan(30f * Mathf.Deg2Rad);
            int renderers = 0; long tris = 0;
            var groups = new Dictionary<int, List<CombineInstance>>();
            for (int ci = 0; ci < map.chunks.Length; ci++)
            {
                var ch = map.chunks[ci];
                groups.Clear();
                void Add(PolygonKit.GeoMesh g, Matrix4x4 m)
                {
                    for (int s = 0; s < g.mats.Length && s < g.mesh.subMeshCount; s++)
                    {
                        if (!groups.TryGetValue(g.mats[s], out var l)) groups[g.mats[s]] = l = new List<CombineInstance>();
                        l.Add(new CombineInstance { mesh = g.mesh, subMeshIndex = s, transform = m });
                    }
                }
                if (byChunk[ci] != null)
                    foreach (int ii in byChunk[ci])
                    {
                        var it = map.instances[ii]; int gi = map.protos[it.p].mesh;
                        if (gi >= 0 && gi < geo.Count) Add(geo[gi], M(it.m));
                    }
                if (ch.mesh >= 0 && ch.mesh < geo.Count) Add(geo[ch.mesh], Matrix4x4.identity);
                if (groups.Count == 0) continue;
                var parts = new CombineInstance[groups.Count]; var pm = new Material[groups.Count];
                int k = 0, verts = 0;
                foreach (var kv in groups)
                {
                    var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                    part.CombineMeshes(kv.Value.ToArray(), true, true, false);
                    verts += part.vertexCount;
                    parts[k] = new CombineInstance { mesh = part, subMeshIndex = 0, transform = Matrix4x4.identity };
                    pm[k] = mats[Mathf.Clamp(kv.Key, 0, mats.Length - 1)];
                    k++;
                }
                var mesh = new Mesh { name = "SD_" + ch.cls + "_" + ci, indexFormat = verts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.CombineMeshes(parts, false, false, false);
                foreach (var p in parts) UnityEngine.Object.Destroy(p.mesh);
                for (int s = 0; s < mesh.subMeshCount; s++) tris += mesh.GetIndexCount(s) / 3;
                mesh.UploadMeshData(true);
                _meshes.Add(mesh);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(chunksRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = pm;
                mr.shadowCastingMode = ch.shadow != 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                var lg = go.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.01f, new Renderer[] { mr }) });
                lg.RecalculateBounds();
                float dist = Mathf.Max(10f, ch.cull);
                float hgt = Mathf.Clamp(lg.size / (2f * dist * tanHalf), 0.001f, 0.9f);
                lg.SetLODs(new[] { new LOD(hgt, new Renderer[] { mr }) });
                lg.fadeMode = LODFadeMode.CrossFade; lg.animateCrossFading = true;   // v12: dithered fade instead of popping (URP LOD cross-fade)
                renderers++;
            }
            foreach (var g in geo) if (g.mesh != null) UnityEngine.Object.Destroy(g.mesh);

            int cols = BuildBoxes(root, carve);
            FillAnchors(root);
            Debug.Log("[Severny] SevernyDress " + map.build + ": " + renderers + " чанков, " + (tris / 1000) + " тыс. треугольников, " + cols + " коллайдеров" +
                      (carve ? " (NavMeshObstacle carve — для точного NavMesh выполните Severny > 4 Bake NavMesh)" : "") + ", якоря spawn/exit/loot/item/bot/zone " +
                      MapAnchors.Spawns.Count + "/" + MapAnchors.Exits.Count + "/" + MapAnchors.Loot.Count + "/" + MapAnchors.Items.Count + "/" + MapAnchors.Bots.Count + "/" + MapAnchors.Zones.Count +
                      ", выравнивание " + LastAlign + ", " + Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f) + " мс");
            return root;
        }

        static int BuildBoxes(Transform root, bool carve)
        {
            var map = Map;
            var nowalk = Child(root, "NoWalk");
            NoWalkRoot = nowalk;
            if (map.boxes == null) return 0;
            int n = 0;
            foreach (var b in map.boxes)
            {
                if (b == null) continue;
                var go = new GameObject("col");
                go.transform.SetParent(nowalk, false);
                go.transform.localPosition = V3(b.c);
                go.transform.localRotation = Quaternion.Euler(0f, b.yaw, 0f);
                var s = V3(b.s); s = new Vector3(Mathf.Max(0.05f, s.x), Mathf.Max(0.05f, s.y), Mathf.Max(0.05f, s.z));
                go.AddComponent<BoxCollider>().size = s;
                if (carve)
                {
                    var ob = go.AddComponent<NavMeshObstacle>();
                    ob.shape = NavMeshObstacleShape.Box; ob.center = Vector3.zero; ob.size = s;
                    ob.carving = true; ob.carveOnlyStationary = true; ob.carvingTimeToStationary = 0f;   // carve right away (spawns are picked a moment later)
                }
                n++;
            }
            return n;
        }

        static void FillAnchors(Transform root)
        {
            var map = Map;
            MapAnchors.Clear();
            if (map.anchors == null) return;
            float ry = root.eulerAngles.y;
            foreach (var a in map.anchors)
            {
                if (a == null) continue;
                var pos = root.TransformPoint(V3(a.pos)); float yaw = a.yaw + ry;
                var pt = new MapAnchors.Point { name = a.name, table = a.title, pos = pos, yaw = yaw };
                switch (a.kind)
                {
                    case "spawn": MapAnchors.Spawns.Add(new MapAnchors.Spawn { name = a.name, pos = pos, yaw = yaw }); break;
                    case "exit": MapAnchors.Exits.Add(new MapAnchors.Exit { name = a.name, title = string.IsNullOrEmpty(a.title) ? a.name : a.title, pos = pos }); break;
                    case "loot": MapAnchors.Loot.Add(pt); break;
                    case "item": MapAnchors.Items.Add(pt); break;
                    case "bot": MapAnchors.Bots.Add(pt); break;
                    case "zone":
                        if (a.size != null && a.size.Length >= 3)
                            MapAnchors.Zones.Add(new MapAnchors.Zone { name = a.name, center = pos, size = V3(a.size), yaw = yaw });
                        break;
                }
            }
        }

        /// <summary>Editor (Severny > 4 Bake NavMesh): only the aligned colliders of the dress, as Not Walkable NavMesh sources.
        /// The caller destroys the returned object.</summary>
        public static Transform BuildColliders(Transform parent, Transform cityRoot)
        {
            if (Map == null) return null;
            Align(cityRoot, out var apos, out var ayaw);
            var root = new GameObject("SevernyDress_Colliders (temp)").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(apos, Quaternion.Euler(0f, ayaw, 0f));
            BuildBoxes(root, false);
            NoWalkRoot = null;
            return root;
        }

        /// <summary>Free the merged meshes (the GameObjects are children of the RaidManager and go with it).</summary>
        public static void Unload()
        {
            foreach (var m in _meshes) if (m != null) UnityEngine.Object.Destroy(m);
            _meshes.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            Root = null; NoWalkRoot = null;
        }
    }
}
