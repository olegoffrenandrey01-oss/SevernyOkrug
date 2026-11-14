// Surface wear across the whole loaded map. Uses the project's existing licensed textures and shader variants.
// No colliders or random solid obstacles; geometry is copied only from existing visible opaque surfaces.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Severny
{
    public class V14SurfaceDetail : MonoBehaviour
    {
        readonly List<Mesh> _meshes = new List<Mesh>();
        readonly List<Material> _materials = new List<Material>();
        class Batch
        {
            public Material mat;
            public List<Vector3> v = new List<Vector3>();
            public List<Vector3> n = new List<Vector3>();
            public List<Vector2> uv = new List<Vector2>();
            public List<int> indices = new List<int>();
        }
        struct Face { public Vector3 a, b, c, normal; public bool ground, metal; }
        public static IEnumerator Build(Transform owner, Transform world)
        {
            var cfg = V14Settings.Current;
            if (world == null || !cfg.surfaceDetail || GfxQuality.Level == 0 || RaidOptions.Winter) yield break;
            var go = new GameObject("v14_SurfaceDetail"); go.transform.SetParent(owner, false);
            var detail = go.AddComponent<V14SurfaceDetail>();
            var grime = detail.LoadMaterial("M_Grime", new Color(0.5f, 0.47f, 0.4f, cfg.wearOpacity));
            var rust = detail.LoadMaterial("M_RustDrip", new Color(0.65f, 0.5f, 0.4f, cfg.wearOpacity * 0.75f));
            if (grime == null) { Destroy(go); yield break; }
            if (rust == null) rust = grime;
            var batches = new Dictionary<string, Batch>();
            int budget = Mathf.RoundToInt(Mathf.Clamp(cfg.maxSurfacePatches, 0, 4000) * (GfxQuality.Level == 1 ? 0.4f : 1f));
            int added = 0, scanned = 0, eligible = 0;
            var selected = new List<Face>(budget);
            var random = new System.Random(140014);
            if (budget <= 0) { Destroy(go); yield break; }
            // Stable traversal; no gameplay RNG is consumed, so map/loot seeds are unaffected.
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>(false))
            {
                var mesh = mf.sharedMesh; var renderer = mf.GetComponent<MeshRenderer>();
                if (mesh == null || !mesh.isReadable || renderer == null) continue;
                var mats = renderer.sharedMaterials;
                var vertices = mesh.vertices;
                for (int sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
                {
                    var mat = mats[sub]; if (mat == null || mat.renderQueue >= 2450) continue;
                    string name = mat.name.ToLowerInvariant();
                    bool ground = name.Contains("asphalt") || name.Contains("paving") || name.Contains("slabs");
                    bool metal = name.Contains("corr") || name.Contains("garagedoor");
                    bool wall = name.Contains("concrete") || name.Contains("brick") || name.Contains("cladding");
                    if (!ground && !metal && !wall) continue;
                    var triangles = mesh.GetTriangles(sub);
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        Vector3 a = mf.transform.TransformPoint(vertices[triangles[t]]),
                            b = mf.transform.TransformPoint(vertices[triangles[t + 1]]), c = mf.transform.TransformPoint(vertices[triangles[t + 2]]);
                        var normal = Vector3.Cross(b - a, c - a); float twiceArea = normal.magnitude;
                        if (twiceArea < 0.1f) continue;
                        normal /= twiceArea;
                        if (ground ? normal.y < 0.9f : Mathf.Abs(normal.y) > 0.25f) continue;
                        var face = new Face { a = a, b = b, c = c, normal = normal, ground = ground, metal = metal };
                        // Reservoir sampling distributes the budget over the entire map, independent of mesh order.
                        eligible++;
                        if (selected.Count < budget) selected.Add(face);
                        else { int slot = random.Next(eligible); if (slot < budget) selected[slot] = face; }

                    }
                }
                if (++scanned % 24 == 0) yield return null;
            }
            foreach (var face in selected)
            {
                var center = (face.a + face.b + face.c) / 3f;
                string key = (face.metal ? "metal" : "grime") + ":" + Mathf.FloorToInt(center.x / 32f) + ":" + Mathf.FloorToInt(center.y / 16f) + ":" + Mathf.FloorToInt(center.z / 32f);
                if (!batches.TryGetValue(key, out var batch)) { batch = new Batch { mat = face.metal ? rust : grime }; batches.Add(key, batch); }
                int first = batch.v.Count;
                foreach (var p in new[] { face.a, face.b, face.c })
                {
                    batch.v.Add(go.transform.InverseTransformPoint(p + face.normal * 0.006f));
                    batch.n.Add(go.transform.InverseTransformDirection(face.normal).normalized);
                    var uv = face.ground ? new Vector2(p.x, p.z) : Mathf.Abs(face.normal.x) > Mathf.Abs(face.normal.z) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
                    batch.uv.Add(uv / (face.metal ? 4f : face.ground ? 14f : 9f));
                }
                batch.indices.Add(first); batch.indices.Add(first + 1); batch.indices.Add(first + 2);
                added++;
            }
            foreach (var pair in batches)
            {
                var batch = pair.Value;
                var m = new Mesh { name = "v14_Wear_" + pair.Key, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(batch.v); m.SetNormals(batch.n); m.SetUVs(0, batch.uv); m.SetTriangles(batch.indices, 0); m.RecalculateBounds();
                detail._meshes.Add(m);
                var chunk = new GameObject(m.name); chunk.transform.SetParent(go.transform, false);
                chunk.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = chunk.AddComponent<MeshRenderer>(); mr.sharedMaterial = batch.mat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            Debug.Log("[Severny] v14: слой износа всей карты — треугольников " + added + ", пространственных групп " + batches.Count);
        }
        Material LoadMaterial(string name, Color tint)
        {
            Material src = Resources.Load<Material>("PolygonKit/Materials/" + name);
            if (src == null && PolygonKit.Map != null)
                foreach (var def in PolygonKit.Map.materials) if (def.name == name) { src = PolygonKit.MaterialFor(def); break; }
            if (src == null || src.renderQueue < 2450) return null;
            var mat = new Material(src) { name = name + " v14 wear", hideFlags = HideFlags.DontSave };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.14f);
            mat.renderQueue = src.renderQueue + 1;
            _materials.Add(mat); return mat;
        }
        void OnDestroy()
        {
            foreach (var mesh in _meshes) if (mesh != null) Destroy(mesh);
            foreach (var mat in _materials) if (mat != null) Destroy(mat);
        }
    }
}
