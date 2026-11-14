// "Полигон": procedural playable test map in the reference mood (wet concrete slabs, panel 5-storeys,
// garages, fuel tanks, power lines, spruces, wrecked cars). Primitives only; static-batched after NavMesh build.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public static class GreyboxMap
    {
        const float H = 140f;
        static System.Random _r;
        static float R(float a, float b) => a + (float)_r.NextDouble() * (b - a);
        static int RI(int a, int b) => _r.Next(a, b);

        static Material M(float r, float g, float b, float s = 0.15f, float m = 0f) => GameAssets.Lit(new Color(r, g, b), s, m);

        public static Transform Build(Transform parent, int seed, out Bounds bounds)
        {
            _r = new System.Random(seed);
            var root = new GameObject("MAP_POLYGON").transform;
            root.SetParent(parent, false);
            Ground(root);
            Roads(root);
            Fence(root);
            Slabs(root, new Rect(20, 20, 95, 55));
            PanelBlocks(root);
            Ruin(root, new Vector3(5, 0, -45));
            Garages(root, new Vector3(-118, 0, -118), 10);
            Garages(root, new Vector3(-118, 0, -96), 10);
            Industrial(root);
            PowerLine(root, -125f, 125f, 9.5f);
            Trees(root);
            Cars(root, 22);
            Clutter(root);
            Puddles(root, 26);
            bounds = new Bounds(new Vector3(0, 12f, 0), new Vector3(2 * H, 30f, 2 * H));
            return root;
        }

        static void Ground(Transform t)
        {
            Prim.Box(t, new Vector3(0, -0.5f, 0), new Vector3(2 * H + 40, 1f, 2 * H + 40), M(0.25f, 0.26f, 0.2f, 0.12f), true, null, "ground");
            // invisible outer boundary
            for (int i = 0; i < 4; i++)
            {
                var g = new GameObject("boundary" + i); g.transform.SetParent(t, false);
                var c = g.AddComponent<BoxCollider>();
                bool x = i % 2 == 0; float s = i < 2 ? 1 : -1;
                g.transform.localPosition = x ? new Vector3(s * (H + 6), 10, 0) : new Vector3(0, 10, s * (H + 6));
                c.size = x ? new Vector3(1, 20, 2 * H + 20) : new Vector3(2 * H + 20, 20, 1);
            }
        }

        static void Road(Transform t, Vector3 a, Vector3 b, float w)
        {
            var asphalt = M(0.13f, 0.13f, 0.135f, 0.58f);
            var curb = M(0.45f, 0.45f, 0.43f, 0.2f);
            Vector3 d = b - a; float len = d.magnitude; var rot = Quaternion.LookRotation(d.normalized);
            Vector3 mid = (a + b) * 0.5f;
            Prim.Box(t, mid + Vector3.up * 0.03f, new Vector3(w, 0.06f, len), asphalt, true, rot, "road");
            Vector3 side = rot * Vector3.right;
            Prim.Box(t, mid + side * (w * 0.5f + 0.15f) + Vector3.up * 0.08f, new Vector3(0.3f, 0.16f, len), curb, true, rot, "curb");
            Prim.Box(t, mid - side * (w * 0.5f + 0.15f) + Vector3.up * 0.08f, new Vector3(0.3f, 0.16f, len), curb, true, rot, "curb");
        }

        static void Roads(Transform t)
        {
            Road(t, new Vector3(-H + 10, 0, 0), new Vector3(H - 10, 0, 0), 9f);
            Road(t, new Vector3(-20, 0, -H + 10), new Vector3(-20, 0, H - 10), 9f);
            Road(t, new Vector3(-H + 14, 0, 118), new Vector3(H - 14, 0, 118), 7f);
            Road(t, new Vector3(-H + 14, 0, -128), new Vector3(H - 14, 0, -128), 7f);
            Road(t, new Vector3(60, 0, -124), new Vector3(60, 0, -5), 7f);
        }

        static void Fence(Transform t)
        {
            var conc = M(0.47f, 0.46f, 0.43f, 0.18f);
            for (int side = 0; side < 4; side++)
                for (float u = -H + 2; u < H - 1; u += 4f)
                {
                    if (_r.NextDouble() < 0.04) continue;
                    Vector3 p = side == 0 ? new Vector3(u, 1.3f, H - 2) : side == 1 ? new Vector3(u, 1.3f, -H + 2) : side == 2 ? new Vector3(H - 2, 1.3f, u) : new Vector3(-H + 2, 1.3f, u);
                    var rot = Quaternion.Euler(R(-1.5f, 1.5f), side < 2 ? 0 : 90, R(-1f, 1f));
                    Prim.Box(t, p, new Vector3(3.96f, 2.6f, 0.18f), conc, true, rot, "fence");
                }
        }

        static void Slabs(Transform t, Rect r)
        {
            var mats = new[] { M(0.36f, 0.36f, 0.34f, 0.42f), M(0.32f, 0.32f, 0.31f, 0.5f), M(0.4f, 0.39f, 0.37f, 0.38f) };
            for (float x = r.xMin; x < r.xMax; x += 3f)
                for (float z = r.yMin; z < r.yMax; z += 6f)
                {
                    if (_r.NextDouble() < 0.05) continue;
                    Prim.Box(t, new Vector3(x, 0.05f + R(-0.015f, 0.02f), z), new Vector3(2.93f, 0.1f, 5.93f), mats[RI(0, mats.Length)], true,
                        Quaternion.Euler(R(-0.6f, 0.6f), R(-0.4f, 0.4f), R(-0.6f, 0.6f)), "slab");
                }
        }

        static void PanelBlocks(Transform t)
        {
            var walls = new[] { M(0.55f, 0.54f, 0.5f, 0.2f), M(0.5f, 0.52f, 0.53f, 0.2f), M(0.58f, 0.55f, 0.5f, 0.2f) };
            var win = M(0.05f, 0.06f, 0.065f, 0.85f);
            var frame = M(0.3f, 0.29f, 0.27f, 0.2f);
            Vector3[] pos = { new Vector3(-95, 0, 60), new Vector3(-95, 0, 95), new Vector3(-55, 0, 45), new Vector3(-55, 0, 88) };
            float[] yaw = { 0, 0, 90, 90 };
            for (int i = 0; i < pos.Length; i++)
            {
                var b = new GameObject("PanelBlock_" + i).transform; b.SetParent(t, false);
                b.localPosition = pos[i]; b.localRotation = Quaternion.Euler(0, yaw[i], 0);
                float L = 48f, Hh = 15f, W = 12f;
                Prim.Box(b, new Vector3(0, Hh * 0.5f, 0), new Vector3(L, Hh, W), walls[i % walls.Length], true, null, "body");
                Prim.Box(b, new Vector3(0, Hh + 0.4f, 0), new Vector3(L + 0.3f, 0.8f, W + 0.3f), frame, true, null, "parapet");
                for (int f = 0; f < 5; f++)
                    for (int k = 0; k < 12; k++)
                    {
                        float x = -L * 0.5f + 2f + k * (L - 4f) / 11f, y = 1.6f + f * 2.8f;
                        Prim.Box(b, new Vector3(x, y, W * 0.5f + 0.02f), new Vector3(1.4f, 1.5f, 0.06f), win, false, null, "w");
                        Prim.Box(b, new Vector3(x, y, -W * 0.5f - 0.02f), new Vector3(1.4f, 1.5f, 0.06f), win, false, null, "w");
                    }
                for (int e = 0; e < 3; e++)
                {
                    float x = -L * 0.5f + 8f + e * 16f;
                    Prim.Box(b, new Vector3(x, 1.1f, W * 0.5f + 0.05f), new Vector3(1.6f, 2.2f, 0.12f), M(0.2f, 0.17f, 0.13f), true, null, "door");
                    Prim.Box(b, new Vector3(x, 2.6f, W * 0.5f + 0.9f), new Vector3(2.6f, 0.15f, 1.8f), frame, true, null, "canopy");
                }
            }
        }

        static void Wall(Transform t, Vector3 a, Vector3 b, float h, float y0, Material m, float thick = 0.35f)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < 0.05f) return;
            Prim.Box(t, (a + b) * 0.5f + Vector3.up * (y0 + h * 0.5f), new Vector3(thick, h, len), m, true, Quaternion.LookRotation(d / len), "wall");
        }

        /// <summary>Wall along local X from x0 to x1 at z with openings [(center,width,bottom,top)].</summary>
        static void WallWithOpenings(Transform t, float x0, float x1, float z, float y0, float h, Material m, List<Vector4> holes, bool alongZ = false)
        {
            holes.Sort((p, q) => p.x.CompareTo(q.x));
            float cur = x0;
            System.Func<float, float, Vector3> P = (u, v) => alongZ ? new Vector3(z, v, u) : new Vector3(u, v, z);
            foreach (var hole in holes)
            {
                float hs = hole.x - hole.y * 0.5f, he = hole.x + hole.y * 0.5f;
                if (hs > cur) Wall(t, P(cur, 0), P(hs, 0), h, y0, m);
                if (hole.z > 0.01f) Wall(t, P(hs, 0), P(he, 0), hole.z, y0, m);
                if (hole.w < h - 0.01f) Wall(t, P(hs, 0), P(he, 0), h - hole.w, y0 + hole.w, m);
                cur = he;
            }
            if (cur < x1) Wall(t, P(cur, 0), P(x1, 0), h, y0, m);
        }

        static void Ruin(Transform t, Vector3 at)
        {
            var b = new GameObject("RuinBuilding").transform; b.SetParent(t, false); b.localPosition = at;
            var brick = M(0.42f, 0.25f, 0.19f, 0.15f);
            var conc = M(0.42f, 0.42f, 0.4f, 0.25f);
            float L = 20f, W = 12f, fh = 3.5f;
            for (int floor = 0; floor < 2; floor++)
            {
                float y0 = floor * fh;
                var front = new List<Vector4> { new Vector4(-6, 1.6f, 0, 2.3f), new Vector4(0, 1.4f, 1f, 2.4f), new Vector4(5, 1.4f, 1f, 2.4f) };
                if (floor == 0) front[0] = new Vector4(-6, 1.8f, 0, 2.4f);
                WallWithOpenings(b, -L / 2, L / 2, W / 2, y0, fh, brick, front);
                var back = new List<Vector4> { new Vector4(-4, 1.4f, 1f, 2.4f), new Vector4(4, 1.4f, 1f, 2.4f) };
                if (floor == 0) back.Add(new Vector4(8.2f, 1.6f, 0, 2.3f));
                WallWithOpenings(b, -L / 2, L / 2, -W / 2, y0, fh, brick, back);
                WallWithOpenings(b, -W / 2, W / 2, -L / 2, y0, fh, brick, new List<Vector4> { new Vector4(0, 1.4f, 1f, 2.4f) }, true);
                WallWithOpenings(b, -W / 2, W / 2, L / 2, y0, fh, brick, new List<Vector4> { new Vector4(floor == 0 ? 2f : -2f, 1.6f, floor == 0 ? 0 : 1f, 2.4f) }, true);
                // interior partition with doorway
                WallWithOpenings(b, -W / 2, W / 2, 1f, y0, fh, conc, new List<Vector4> { new Vector4(-2.5f, 1.2f, 0, 2.2f) }, true);
            }
            // floor slab with stairwell hole (x in [-9.5,-5.5])
            Prim.Box(b, new Vector3(2f, fh - 0.1f, 0), new Vector3(15.6f, 0.2f, W - 0.4f), conc, true, null, "slab2");
            Prim.Box(b, new Vector3(-7.5f, fh - 0.1f, 3f), new Vector3(4.4f, 0.2f, 5.2f), conc, true, null, "slab2b");
            // stairs (ramp + steps visual)
            float run = 6.2f, ang = Mathf.Atan2(fh, run) * Mathf.Rad2Deg;
            Prim.Box(b, new Vector3(-7.5f, fh * 0.5f - 0.1f, -2.6f), new Vector3(1.6f, 0.2f, Mathf.Sqrt(run * run + fh * fh)), conc, true, Quaternion.Euler(-ang, 0, 0), "stairs");
            Prim.Box(b, new Vector3(-7.5f, 0.05f, -5.4f), new Vector3(1.6f, 0.1f, 0.8f), conc, true, null, "landing");
            // half roof, rubble
            Prim.Box(b, new Vector3(4f, 2 * fh + 0.1f, 0), new Vector3(12f, 0.25f, W), conc, true, null, "roof");
            for (int i = 0; i < 14; i++)
                Prim.Box(b, new Vector3(R(-9, 9), 0.2f, R(-5, 5)), new Vector3(R(0.3f, 1.2f), R(0.2f, 0.5f), R(0.3f, 1f)), i % 2 == 0 ? brick : conc, true,
                    Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20)), "rubble");
            Prim.Box(b, new Vector3(0, 0.03f, 0), new Vector3(L, 0.06f, W), conc, true, null, "floor");
        }

        static void Garages(Transform t, Vector3 start, int n)
        {
            var wall = M(0.5f, 0.48f, 0.45f, 0.15f);
            var roof = M(0.22f, 0.22f, 0.22f, 0.3f);
            var doors = new[] { M(0.36f, 0.2f, 0.12f, 0.3f, 0.3f), M(0.28f, 0.32f, 0.29f, 0.3f, 0.3f), M(0.3f, 0.3f, 0.34f, 0.3f, 0.3f) };
            for (int i = 0; i < n; i++)
            {
                var g = new GameObject("Garage_" + i).transform; g.SetParent(t, false);
                g.localPosition = start + new Vector3(i * 3.6f, 0, 0);
                Wall(g, new Vector3(-1.8f, 0, 0), new Vector3(-1.8f, 0, 6f), 2.6f, 0, wall, 0.2f);
                if (i == n - 1) Wall(g, new Vector3(1.8f, 0, 0), new Vector3(1.8f, 0, 6f), 2.6f, 0, wall, 0.2f);
                Wall(g, new Vector3(-1.8f, 0, 6f), new Vector3(1.8f, 0, 6f), 2.6f, 0, wall, 0.2f);
                Prim.Box(g, new Vector3(0, 2.7f, 3f), new Vector3(3.8f, 0.2f, 6.4f), roof, true, null, "roof");
                if (_r.NextDouble() < 0.55)
                    Prim.Box(g, new Vector3(0, 1.1f, 0.05f), new Vector3(3.0f, 2.2f, 0.08f), doors[RI(0, doors.Length)], true, null, "door");
            }
        }

        static void Tank(Transform t, Vector3 p, float yaw)
        {
            var tank = M(0.62f, 0.62f, 0.6f, 0.35f, 0.2f);
            var rust = M(0.36f, 0.2f, 0.11f, 0.2f, 0.2f);
            var red = M(0.55f, 0.08f, 0.06f, 0.3f);
            var g = new GameObject("FuelTank").transform; g.SetParent(t, false); g.localPosition = p; g.localRotation = Quaternion.Euler(0, yaw, 0);
            Prim.Cyl(g, new Vector3(0, 2.3f, 0), 3.2f, 10f, tank, true, Quaternion.Euler(90, 0, 0), "body");
            Prim.Box(g, new Vector3(0, 0.6f, -3.5f), new Vector3(2.6f, 1.2f, 0.6f), rust, true, null, "saddle");
            Prim.Box(g, new Vector3(0, 0.6f, 3.5f), new Vector3(2.6f, 1.2f, 0.6f), rust, true, null, "saddle");
            Prim.Box(g, new Vector3(1.62f, 2.3f, 0), new Vector3(0.02f, 0.5f, 4f), red, false, null, "stripe");
            Prim.Box(g, new Vector3(-1.62f, 2.3f, 0), new Vector3(0.02f, 0.5f, 4f), red, false, null, "stripe");
        }

        static void Industrial(Transform t)
        {
            Tank(t, new Vector3(55, 0, -95), 90); Tank(t, new Vector3(55, 0, -86), 90); Tank(t, new Vector3(55, 0, -77), 90);
            var vt = M(0.55f, 0.56f, 0.55f, 0.3f, 0.2f);
            Prim.Cyl(t, new Vector3(88, 5, -100), 9f, 10f, vt, true, null, "vtank");
            Prim.Cyl(t, new Vector3(105, 5, -84), 8f, 10f, vt, true, null, "vtank");
            Prim.Box(t, new Vector3(92.8f, 5, -100), new Vector3(0.5f, 10f, 0.6f), M(0.25f, 0.25f, 0.24f, 0.3f, 0.6f), true, null, "ladder");
            // hangar (open on one side)
            var hang = new GameObject("Hangar").transform; hang.SetParent(t, false); hang.localPosition = new Vector3(95, 0, -25);
            var sheet = M(0.42f, 0.44f, 0.43f, 0.35f, 0.4f);
            Wall(hang, new Vector3(-15, 0, -9), new Vector3(15, 0, -9), 9f, 0, sheet, 0.3f);
            Wall(hang, new Vector3(-15, 0, -9), new Vector3(-15, 0, 9), 9f, 0, sheet, 0.3f);
            Wall(hang, new Vector3(15, 0, -9), new Vector3(15, 0, 9), 9f, 0, sheet, 0.3f);
            WallWithOpenings(hang, -15, 15, 9, 0, 9f, sheet, new List<Vector4> { new Vector4(0, 8f, 0, 6f) });
            Prim.Box(hang, new Vector3(0, 9.2f, 0), new Vector3(30.6f, 0.3f, 18.6f), M(0.3f, 0.3f, 0.3f, 0.3f, 0.4f), true, null, "roof");
            Prim.Box(hang, new Vector3(0, 0.03f, 0), new Vector3(30f, 0.06f, 18f), M(0.34f, 0.34f, 0.33f, 0.45f), true, null, "floor");
            // shipping containers
            var cols = new[] { new Color(0.42f, 0.16f, 0.1f), new Color(0.15f, 0.26f, 0.38f), new Color(0.22f, 0.32f, 0.2f), new Color(0.5f, 0.42f, 0.2f) };
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = new Vector3(R(25, 125), 1.3f, R(-120, -45));
                if (Vector3.Distance(p, new Vector3(95, 1.3f, -25)) < 22f || (p.x > 45 && p.x < 65 && p.z < -70) || Mathf.Abs(p.x - 60) < 5f) continue;
                var c = cols[RI(0, cols.Length)];
                Prim.Box(t, p, new Vector3(6.06f, 2.6f, 2.44f), GameAssets.Lit(c, 0.3f, 0.3f), true, Quaternion.Euler(0, R(0, 180), 0), "container");
            }
        }

        static void PowerLine(Transform t, float x0, float x1, float z)
        {
            var pole = M(0.5f, 0.5f, 0.47f, 0.2f);
            var wireMat = GameAssets.Unlit(new Color(0.06f, 0.06f, 0.06f));
            var tops = new List<Vector3>();
            for (float x = x0; x <= x1; x += 32f)
            {
                Prim.Box(t, new Vector3(x, 5.5f, z), new Vector3(0.35f, 11f, 0.35f), pole, true, Quaternion.Euler(R(-1.5f, 1.5f), 0, R(-1.5f, 1.5f)), "pole");
                Prim.Box(t, new Vector3(x, 10.5f, z), new Vector3(0.2f, 0.2f, 2.6f), pole, false, null, "arm");
                tops.Add(new Vector3(x, 10.6f, z));
            }
            for (int w = -1; w <= 1; w++)
                for (int i = 0; i + 1 < tops.Count; i++)
                {
                    var g = new GameObject("wire"); g.transform.SetParent(t, false);
                    var lr = g.AddComponent<LineRenderer>();
                    lr.sharedMaterial = wireMat; lr.widthMultiplier = 0.035f; lr.positionCount = 9; lr.useWorldSpace = true;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    Vector3 a = tops[i] + new Vector3(0, 0, w * 1.2f), b = tops[i + 1] + new Vector3(0, 0, w * 1.2f);
                    for (int k = 0; k < 9; k++) { float u = k / 8f; lr.SetPosition(k, Vector3.Lerp(a, b, u) + Vector3.down * (Mathf.Sin(u * Mathf.PI) * 1.1f)); }
                }
        }

        static void Spruce(Transform t, Vector3 p, float s)
        {
            var g = new GameObject("Spruce").transform; g.SetParent(t, false); g.localPosition = p;
            g.localRotation = Quaternion.Euler(0, R(0, 360), 0);
            var trunk = M(0.2f, 0.15f, 0.1f, 0.1f);
            var needles = new[] { M(0.09f, 0.15f, 0.09f, 0.08f), M(0.1f, 0.17f, 0.11f, 0.08f), M(0.08f, 0.13f, 0.09f, 0.08f) };
            var nm = needles[RI(0, needles.Length)];
            Prim.Cyl(g, new Vector3(0, 1.4f * s, 0), 0.32f * s, 2.8f * s, trunk, true, null, "trunk");
            Prim.Cone(g, new Vector3(0, 1.3f * s, 0), 4.6f * s, 4.2f * s, nm);
            Prim.Cone(g, new Vector3(0, 3.6f * s, 0), 3.5f * s, 3.8f * s, nm);
            Prim.Cone(g, new Vector3(0, 5.8f * s, 0), 2.3f * s, 3.4f * s, nm);
        }

        static bool Blocked(Vector3 p)
        {
            if (Mathf.Abs(p.z) < 7f || Mathf.Abs(p.x + 20f) < 7f) return true;                     // main roads
            if (Mathf.Abs(p.z - 118f) < 6f || Mathf.Abs(p.z + 128f) < 6f) return true;
            if (p.x > 15 && p.x < 120 && p.z > 15 && p.z < 80) return true;                         // slabs yard
            if (p.x > 15 && p.x < 130 && p.z < -40 && p.z > -130) return true;                      // industrial
            if (p.x > 70 && p.z > -40 && p.z < -10) return true;                                    // hangar
            if (p.x > -125 && p.x < -30 && p.z > 30 && p.z < 105) return Physics.CheckSphere(p + Vector3.up * 2, 7f);
            if (p.x > -125 && p.x < -75 && p.z < -85 && p.z > -130) return true;                    // garages
            if (p.x > -10 && p.x < 20 && p.z > -58 && p.z < -32) return true;                       // ruin
            return false;
        }

        static void Trees(Transform t)
        {
            int placed = 0;
            for (int i = 0; i < 900 && placed < 230; i++)
            {
                Vector3 p;
                if (i % 3 == 0) { float u = R(-H + 6, H - 6); int s = RI(0, 4); float d = R(6f, 16f); p = s == 0 ? new Vector3(u, 0, H - d) : s == 1 ? new Vector3(u, 0, -H + d) : s == 2 ? new Vector3(H - d, 0, u) : new Vector3(-H + d, 0, u); }
                else p = new Vector3(R(-H + 8, H - 8), 0, R(-H + 8, H - 8));
                if (Blocked(p)) continue;
                Spruce(t, p, R(0.75f, 1.35f));
                placed++;
            }
        }

        static void Car(Transform t, Vector3 p, float yaw, Color c, bool wreck)
        {
            var g = new GameObject("CarWreck").transform; g.SetParent(t, false); g.localPosition = p; g.localRotation = Quaternion.Euler(0, yaw, 0);
            var body = GameAssets.Lit(c, 0.35f, 0.3f);
            var glass = M(0.04f, 0.05f, 0.05f, 0.8f);
            var tyre = M(0.06f, 0.06f, 0.06f, 0.1f);
            float lift = wreck ? 0.25f : 0.62f;
            Prim.Box(g, new Vector3(0, lift + 0.05f, 0), new Vector3(1.75f, 0.75f, 4.3f), body, true, null, "body");
            Prim.Box(g, new Vector3(0, lift + 0.72f, -0.2f), new Vector3(1.55f, 0.62f, 2.1f), body, true, null, "cabin");
            Prim.Box(g, new Vector3(0, lift + 0.74f, 0.88f), new Vector3(1.45f, 0.5f, 0.05f), glass, false, Quaternion.Euler(-30, 0, 0), "windshield");
            if (!wreck)
                for (int i = 0; i < 4; i++)
                    Prim.Cyl(g, new Vector3(i < 2 ? -0.82f : 0.82f, 0.32f, i % 2 == 0 ? 1.35f : -1.35f), 0.64f, 0.22f, tyre, false, Quaternion.Euler(0, 0, 90), "wheel");
        }

        static void Cars(Transform t, int n)
        {
            var cols = new[] { new Color(0.38f, 0.12f, 0.1f), new Color(0.33f, 0.4f, 0.46f), new Color(0.58f, 0.58f, 0.54f), new Color(0.24f, 0.29f, 0.21f), new Color(0.45f, 0.38f, 0.22f) };
            for (int i = 0; i < n; i++)
            {
                Vector3 p; float yaw;
                if (i % 2 == 0) { p = new Vector3(R(-120, 120), 0, R(-3f, 3f)); yaw = R(-25, 25) + (_r.NextDouble() < 0.5 ? 0 : 180) + 90; }
                else { p = new Vector3(-20 + R(-3f, 3f), 0, R(-120, 110)); yaw = R(-25, 25) + (_r.NextDouble() < 0.5 ? 0 : 180); }
                Car(t, p, yaw, cols[RI(0, cols.Length)], _r.NextDouble() < 0.4);
            }
        }

        static void Clutter(Transform t)
        {
            var conc = M(0.45f, 0.45f, 0.42f, 0.2f);
            var sand = M(0.3f, 0.3f, 0.22f, 0.05f);
            // FBS block stacks
            for (int i = 0; i < 16; i++)
            {
                Vector3 p = new Vector3(R(-120, 120), 0, R(-120, 120));
                if (Blocked(p) && !(p.x > 15 && p.x < 120 && p.z > 15 && p.z < 80)) continue;
                int h = RI(1, 4);
                float yaw = R(0, 180);
                for (int k = 0; k < h; k++) Prim.Box(t, p + Vector3.up * (0.3f + k * 0.6f), new Vector3(2.4f, 0.6f, 0.6f), conc, true, Quaternion.Euler(0, yaw + R(-4, 4), 0), "fbs");
            }
            // checkpoint with sandbags at the east road entry
            Vector3 cp = new Vector3(118, 0, 0);
            for (int k = -3; k <= 3; k++) if (Mathf.Abs(k) > 1) Prim.Box(t, cp + new Vector3(0, 0.45f, k * 1.25f), new Vector3(0.45f, 0.9f, 1.2f), sand, true, null, "sandbag");
            Prim.Box(t, cp + new Vector3(-4, 1.25f, 7.5f), new Vector3(2.2f, 2.5f, 2.2f), M(0.35f, 0.38f, 0.3f, 0.2f), true, null, "booth");
            Prim.Box(t, cp + new Vector3(-1, 1.0f, 0), new Vector3(0.12f, 0.12f, 8f), M(0.6f, 0.1f, 0.08f, 0.3f), true, null, "barrier");
            // lamp posts
            for (float x = -110; x <= 110; x += 40f)
            {
                Prim.Cyl(t, new Vector3(x, 3.5f, -6.2f), 0.16f, 7f, M(0.3f, 0.3f, 0.3f, 0.3f, 0.6f), true, null, "lamp");
                Prim.Box(t, new Vector3(x, 7f, -5.4f), new Vector3(0.3f, 0.15f, 1.6f), M(0.3f, 0.3f, 0.3f, 0.3f, 0.6f), false, null, "lamphead");
            }
        }

        static void Puddles(Transform t, int n)
        {
            var water = M(0.06f, 0.065f, 0.07f, 0.95f, 0f);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = new Vector3(R(-125, 125), 0.075f, R(-125, 125));
                if (!(Mathf.Abs(p.z) < 4.5f || Mathf.Abs(p.x + 20f) < 4.5f || (p.x > 15 && p.x < 120 && p.z > 15 && p.z < 80))) continue;
                var g = Prim.Make(PrimitiveType.Quad, t, p + Vector3.up * 0.04f, new Vector3(R(1.5f, 5f), R(1.2f, 3.5f), 1f), water, false, Quaternion.Euler(90, R(0, 360), 0), "puddle");
                g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
