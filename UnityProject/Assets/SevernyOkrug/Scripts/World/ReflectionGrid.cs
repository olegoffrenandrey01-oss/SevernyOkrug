// v12 «Графика и карта»: reflection probes along the streets, rendered once while the raid loads (realtime probes refreshed
// from script — nothing to bake). A grid over the NavMesh (cell 52–80 m by «Качество графики»): the probe stands at eye height
// on an open spot of the busiest cells, its box (with box projection) covers the street; the largest enterable interiors get
// their own darker probes, so floors and glass indoors stop mirroring the bright sky. Wet asphalt in rain reflects the
// houses and the lit lamps around (CityLights.FocusAt). Per-pixel probe blending needs Forward+ (menu Severny > 7).
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    public static class ReflectionGrid
    {
        public static string LastInfo { get; internal set; } = "нет";

        class Cell { public int ix, iz; public float minY = float.MaxValue; public readonly List<int> verts = new List<int>(); }

        public static IEnumerator Build(Transform parent, Vector3[] navVerts, Action<float> progress)
        {
            var q = GfxQuality.Current;
            if (ShotFlags.NoProbes) { LastInfo = "выкл (фототочки: v12 без проб)"; yield break; }
            if (q.probes <= 0) { LastInfo = "выкл (качество «" + GfxQuality.Names[GfxQuality.Level] + "»)"; yield break; }
            if (!GfxQuality.ForwardPlus) { LastInfo = "выкл: рендерер не Forward+ (меню Severny > 7)"; yield break; }
            float t0 = Time.realtimeSinceStartup;
            List<ReflectionProbe> probes = null;
            int streets = 0;
            string err = null;
            try { probes = Plan(parent, navVerts, q, out streets); }
            catch (Exception e) { err = e.Message; Debug.LogWarning("[Severny] Отражения v12: " + e); }
            if (probes == null || probes.Count == 0) { LastInfo = err != null ? "ошибка: " + err : "нет мест для проб"; yield break; }

            var lights = CityLights.I;
            int perFrame = lights != null ? 1 : 3;       // with city lights: one probe per frame, the lamps around it switched on
            var ids = new int[perFrame];
            int done = 0, timeouts = 0;
            for (int i = 0; i < probes.Count; i += perFrame)
            {
                int n = Mathf.Min(perFrame, probes.Count - i);
                for (int j = 0; j < n; j++)
                {
                    var rp = probes[i + j];
                    if (lights != null) lights.FocusAt(rp.transform.position);
                    ids[j] = rp.RenderProbe();
                }
                float tw = Time.realtimeSinceStartup;
                yield return null;
                while (Time.realtimeSinceStartup - tw < 3f)
                {
                    bool all = true;
                    for (int j = 0; j < n; j++) if (probes[i + j] != null && !probes[i + j].IsFinishedRendering(ids[j])) { all = false; break; }
                    if (all) break;
                    yield return null;
                }
                if (Time.realtimeSinceStartup - tw >= 3f) timeouts++;
                done += n;
                progress?.Invoke(done / (float)probes.Count);
            }
            if (lights != null) lights.FocusAt(null);
            LastInfo = streets + " улиц + " + (probes.Count - streets) + " помещений, " + q.probeRes + " px" + (q.probeHdr ? " HDR" : "") +
                       ", " + (Time.realtimeSinceStartup - t0).ToString("F1") + " с" + (timeouts > 0 ? ", таймаутов " + timeouts : "");
            Debug.Log("[Severny] Отражения v12: " + LastInfo);
        }

        static List<ReflectionProbe> Plan(Transform parent, Vector3[] v, GfxQuality.Preset q, out int streets)
        {
            var res = new List<ReflectionProbe>();
            streets = 0;
            if (v == null || v.Length == 0) return res;
            var root = new GameObject("SEV_ReflectionProbes").transform;
            root.SetParent(parent, false);
            float S = q.probeCell;
            var cells = new Dictionary<long, Cell>();
            for (int i = 0; i < v.Length; i++)
            {
                int ix = Mathf.FloorToInt(v[i].x / S), iz = Mathf.FloorToInt(v[i].z / S);
                long key = ((long)ix << 32) ^ (uint)iz;
                if (!cells.TryGetValue(key, out var c)) { c = new Cell { ix = ix, iz = iz }; cells[key] = c; }
                c.verts.Add(i);
                if (v[i].y < c.minY) c.minY = v[i].y;
            }
            var list = new List<Cell>(cells.Values);
            list.Sort((a, b) => b.verts.Count != a.verts.Count ? b.verts.Count.CompareTo(a.verts.Count) : (a.ix != b.ix ? a.ix.CompareTo(b.ix) : a.iz.CompareTo(b.iz)));
            var cand = new List<KeyValuePair<float, int>>();
            foreach (var c in list)
            {
                if (streets >= q.probes) break;
                if (c.verts.Count < 6) break;
                float cx = (c.ix + 0.5f) * S, cz = (c.iz + 0.5f) * S;
                // ground-level spots near the cell centre first (not roofs / upper floors)
                cand.Clear();
                foreach (int vi in c.verts)
                {
                    var p = v[vi];
                    float dx = p.x - cx, dz = p.z - cz;
                    cand.Add(new KeyValuePair<float, int>(Mathf.Sqrt(dx * dx + dz * dz) + 3f * (p.y - c.minY), vi));
                }
                cand.Sort((a, b) => a.Key.CompareTo(b.Key));
                for (int k = 0; k < cand.Count && k < 6; k++)
                {
                    var g = v[cand[k].Value];
                    var pos = g + Vector3.up * 2.2f;
                    if (MapAnchors.InsideZone(pos)) continue;
                    if (Physics.Raycast(pos, Vector3.up, 80f, ~0, QueryTriggerInteraction.Ignore)) continue;   // under a roof / bridge
                    var rp = NewProbe(root, "Street_" + c.ix + "_" + c.iz, pos, q);
                    rp.center = new Vector3(cx, g.y + 9f, cz) - pos;
                    rp.size = new Vector3(S + 12f, 30f, S + 12f);
                    rp.blendDistance = 6f; rp.importance = 1;
                    res.Add(rp); streets++;
                    break;
                }
            }
            // v13 «ТЦ «Волна»»: denser probes at the puddles of the plaza, on the parking and along the mall facades (vol_look.json)
            var extra = VolnaLook.ExtraProbes;
            if (extra != null)
            {
                int max = Mathf.Min(extra.Length, Mathf.Max(0, q.probes / 4)), added = 0;
                for (int i = 0; i < extra.Length && added < max; i++)
                {
                    var e = extra[i];
                    if (e == null || e.pos == null || e.pos.Length < 3 || e.size == null || e.size.Length < 3) continue;
                    var pos = new Vector3(e.pos[0], e.pos[1], e.pos[2]);
                    if (MapAnchors.InsideZone(pos)) continue;
                    if (Physics.Raycast(pos, Vector3.up, 80f, ~0, QueryTriggerInteraction.Ignore)) continue;   // under a canopy
                    var rp = NewProbe(root, "Volna_" + (string.IsNullOrEmpty(e.name) ? i.ToString() : e.name), pos, q);
                    var size = new Vector3(Mathf.Max(4f, e.size[0]), Mathf.Max(4f, e.size[1]), Mathf.Max(4f, e.size[2]));
                    rp.center = new Vector3(0f, size.y * 0.5f - 2.4f, 0f);
                    rp.size = size;
                    rp.blendDistance = 4f; rp.importance = 2;
                    res.Add(rp); streets++; added++;
                }
            }
            // enterable interiors: own probe each (largest first)
            if (q.zoneProbes > 0 && MapAnchors.Zones.Count > 0)
            {
                var zones = new List<MapAnchors.Zone>(MapAnchors.Zones);
                zones.Sort((a, b) => (b.size.x * b.size.y * b.size.z).CompareTo(a.size.x * a.size.y * a.size.z));
                for (int i = 0; i < zones.Count && i < q.zoneProbes; i++)
                {
                    var z = zones[i];
                    if (z.size.x < 2f || z.size.z < 2f) continue;
                    float cs = Mathf.Abs(Mathf.Cos(z.yaw * Mathf.Deg2Rad)), sn = Mathf.Abs(Mathf.Sin(z.yaw * Mathf.Deg2Rad));
                    var size = new Vector3(cs * z.size.x + sn * z.size.z + 0.5f, z.size.y + 0.5f, sn * z.size.x + cs * z.size.z + 0.5f);
                    var rp = NewProbe(root, "Interior_" + i, z.center, q);
                    rp.center = Vector3.zero; rp.size = size;
                    rp.blendDistance = 1f; rp.importance = 2; rp.intensity = 0.55f;
                    rp.farClipPlane = Mathf.Clamp(size.magnitude * 1.5f, 20f, 120f);
                    res.Add(rp);
                }
            }
            return res;
        }

        static ReflectionProbe NewProbe(Transform root, string name, Vector3 pos, GfxQuality.Preset q)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            var rp = go.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Realtime;
            rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            rp.resolution = q.probeRes;
            rp.hdr = q.probeHdr;
            rp.boxProjection = true;
            rp.nearClipPlane = 0.25f; rp.farClipPlane = 220f;
            rp.shadowDistance = q.probeShadow;
            rp.cullingMask = ~(1 << 5);                // no UI
            rp.clearFlags = ReflectionProbeClearFlags.Skybox;
            rp.intensity = 1f;
            return rp;
        }
    }
}
