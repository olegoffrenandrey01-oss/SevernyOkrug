// Grip poses read from the first-person arm rigs (FP_Arms_<TAG>.fbx: rest pose == hands on the weapon, eye at the origin).
// Reused by FPArms (player) and BotBody (bots share the same MPFB game-engine skeleton).
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public class GripPose
    {
        public string tag;
        public Vector3 hip;                      // weapon root in eye/camera space
        public Vector3 handPosR, handPosL, elbowR, elbowL;   // relative to the weapon root, camera-aligned frame
        public Quaternion handRotR, handRotL;
        public Quaternion upRotR, upRotL, lowRotR, lowRotL;  // reference arm orientation (camera frame) -> natural elbow/forearm twist
        public readonly Dictionary<string, Quaternion> fingers = new Dictionary<string, Quaternion>();
        static readonly Dictionary<string, GripPose> _cache = new Dictionary<string, GripPose>();

        public static string TagFor(ItemDef def) => def == null ? null : def.id == "av74" ? "AV74" : def.id == "p9m" ? "P9M" : null;

        public static GripPose Get(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            if (_cache.TryGetValue(tag, out var g)) return g;
            g = null;
            var p = ModelLib.Prefab("FP_Arms_" + tag);
            if (p != null)
            {
                var t = p.transform;
                var mount = ModelLib.FindDeep(t, "weapon_mount");
                Transform hr = ModelLib.FindDeep(t, "hand_r"), hl = ModelLib.FindDeep(t, "hand_l");
                Transform lr = ModelLib.FindDeep(t, "lowerarm_r"), ll = ModelLib.FindDeep(t, "lowerarm_l");
                Transform ur = ModelLib.FindDeep(t, "upperarm_r"), ul = ModelLib.FindDeep(t, "upperarm_l");
                if (hr != null && hl != null && lr != null && ll != null && ur != null && ul != null)
                {
                    // prefab "world" space == the camera space of the FP rig (prefab root sits at the eye).
                    // v10.2: the imported rig can come out rotated as a whole (in game: weapon under the camera at the hip, open hands
                    // "begging" around the pistol in ADS) - measure the body against the reference skeleton and correct the read-out
                    // exactly like FPArms corrects its instance, so bots / LAN players get the same (correct) grip.
                    var c = RigCheck.Measure(t, null, RigCheck.FPOffset, RigCheck.FPMaxAngle, RigCheck.FPMaxShift);
                    Quaternion q = c.applied ? c.rot : Quaternion.identity;
                    Vector3 o = c.applied ? c.pos : Vector3.zero;
                    if (c.applied) RigCheck.Log("grip_" + tag, "[Severny] FP_Arms_" + tag + " (grip poses for bots): rig orientation corrected - " + c, true);
                    Vector3 m = RigCheck.CheckMount(tag, mount != null ? q * mount.position + o : RigCheck.RefMount(tag), "grip_" + tag);
                    g = new GripPose { tag = tag, hip = m };
                    g.handPosR = q * hr.position + o - m; g.handRotR = q * hr.rotation;
                    g.handPosL = q * hl.position + o - m; g.handRotL = q * hl.rotation;
                    g.elbowR = q * lr.position + o - m; g.elbowL = q * ll.position + o - m;
                    g.upRotR = q * ur.rotation; g.upRotL = q * ul.rotation; g.lowRotR = q * lr.rotation; g.lowRotL = q * ll.rotation;
                    foreach (var h in new[] { hr, hl })
                        foreach (var x in h.GetComponentsInChildren<Transform>(true))
                            if (x != h) g.fingers[x.name] = x.localRotation;
                }
                else Debug.LogWarning("[Severny] FP_Arms_" + tag + ": arm bones not found");
            }
            _cache[tag] = g;
            return g;
        }
    }

    /// <summary>v10.2: orientation self-check of the imported MPFB rigs (FP_Arms_*, CH_Bot_*). The body bones are compared with the
    /// reference skeleton of the Blender export; if the whole rig came out rotated/shifted (FBX axis conversion of the importer),
    /// the rigid correction is measured here and applied by the caller - independent of how Unity imported the file.</summary>
    public static class RigCheck
    {
        // reference skeleton in the bot model space (Unity axes: feet at the origin, facing +Z, right = +X); identical in CH_Bot_A/B/C
        static readonly string[] Bones = { "head", "neck_01", "pelvis", "thigh_l", "thigh_r", "calf_l", "calf_r", "foot_l", "foot_r" };
        static readonly Vector3[] Ref =
        {
            new Vector3(0f, 1.6487f, 0.0469f), new Vector3(0f, 1.55f, 0.0152f), new Vector3(0f, 0.9707f, 0.007f),
            new Vector3(-0.112f, 0.9647f, -0.0036f), new Vector3(0.112f, 0.9647f, -0.0036f),
            new Vector3(-0.1556f, 0.5184f, 0.0314f), new Vector3(0.1556f, 0.5184f, 0.0314f),
            new Vector3(-0.2007f, 0.0745f, 0.0176f), new Vector3(0.2007f, 0.0745f, 0.0176f),
        };
        /// <summary>FP_Arms_*: the same skeleton moved so that the eye is at the origin (camera space).</summary>
        public static readonly Vector3 FPOffset = new Vector3(0f, -1.688f, -0.15f);
        public const float FPMaxAngle = 1f, FPMaxShift = 0.01f, BotMaxAngle = 5f, BotMaxShift = 0.05f, MaxRms = 0.06f;
        static readonly HashSet<string> _logged = new HashSet<string>();

        public struct Result
        {
            public bool measured, ok, applied;      // bones found / rig matches the reference shape / correction needed
            public Quaternion rot; public Vector3 pos; // apply as  local = rot * local,  pos = rot * pos + pos
            public float angle, shift, rms;
            public override string ToString() => !measured ? "body bones missing (not checked)"
                : angle.ToString("F1") + " deg, " + (shift * 100f).ToString("F1") + " cm" + " (fit error " + (rms * 100f).ToString("F1") + " cm)"
                  + (ok ? "" : " - shape does not match the reference, not corrected");
        }

        public static Vector3 RefMount(string tag) => tag == "P9M" ? ModelSpecs.Hip_P9M : ModelSpecs.Hip_AV74;

        /// <summary>Rigid transform that moves the rig's body bones (measured in `space`, world space if null) onto the reference
        /// skeleton + offset. applied == false when the rig already matches within maxAngle / maxShift.</summary>
        public static Result Measure(Transform root, Transform space, Vector3 offset, float maxAngle, float maxShift)
        {
            var r = new Result { rot = Quaternion.identity };
            int n = Bones.Length;
            var a = new Vector3[n]; var e = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var b = ModelLib.FindDeep(root, Bones[i]);
                if (b == null) return r;
                a[i] = space != null ? space.InverseTransformPoint(b.position) : b.position;
                e[i] = Ref[i] + offset;
            }
            Vector3 ua = Up(a), la = Lat(a), ue = Up(e), le = Lat(e);
            Vector3 fa = Vector3.Cross(la, ua), fe = Vector3.Cross(le, ue);
            if (ua.sqrMagnitude < 0.01f || fa.sqrMagnitude < 1e-4f) return r;
            r.measured = true;
            // TRIAD: body up (feet -> head) and right (left -> right leg) onto the reference axes
            Quaternion q = Quaternion.LookRotation(fe, ue) * Quaternion.Inverse(Quaternion.LookRotation(fa, ua));
            Vector3 ma = Vector3.zero, me = Vector3.zero;
            for (int i = 0; i < n; i++) { ma += a[i]; me += e[i]; }
            Vector3 t = me / n - q * (ma / n);
            float s = 0f;
            for (int i = 0; i < n; i++) s += (q * a[i] + t - e[i]).sqrMagnitude;
            r.rms = Mathf.Sqrt(s / n);
            r.angle = Quaternion.Angle(Quaternion.identity, q);
            r.shift = t.magnitude;
            r.ok = r.rms < MaxRms;
            if (r.ok && (r.angle > maxAngle || r.shift > maxShift)) { r.applied = true; r.rot = q; r.pos = t; }
            return r;
        }

        static Vector3 Up(Vector3[] p) => p[0] + p[1] - p[7] - p[8];
        static Vector3 Lat(Vector3[] p) => (p[4] - p[3]) + (p[6] - p[5]) + (p[8] - p[7]);

        /// <summary>Applies a measured correction to `target`; valid when the rig was measured in target.parent space.</summary>
        public static void Apply(Transform target, Result r)
        {
            if (!r.applied) return;
            target.localRotation = r.rot * target.localRotation;
            target.localPosition = r.rot * target.localPosition + r.pos;
        }

        /// <summary>weapon_mount (camera space of the FP rig) must sit where the rig was authored; otherwise use the reference.</summary>
        public static Vector3 CheckMount(string tag, Vector3 m, string logKey)
        {
            Vector3 refm = RefMount(tag);
            if ((m - refm).sqrMagnitude <= 0.02f * 0.02f) return m;
            Log(logKey + "_mount", "[Severny] FP_Arms_" + tag + ": weapon_mount at " + m.ToString("F3") + " instead of " + refm.ToString("F3") + " - using the reference", true);
            return refm;
        }

        public static void Log(string key, string msg, bool warning)
        {
            if (!_logged.Add(key)) return;
            if (warning) Debug.LogWarning(msg); else Debug.Log(msg);
        }
    }

    public static class TwoBoneIK
    {
        /// <summary>Analytic two-bone IK: rotates up/low so the hand reaches target (clamped), bend plane from pole.
        /// Expects the chain in a bent reference pose (rest or GripPose rotations) - its hinge plane is carried over.</summary>
        public static void Solve(Transform up, Transform low, Transform hand, Vector3 target, Quaternion handRot, Vector3 pole, float l1, float l2)
        {
            Vector3 s = up.position, d = target - s;
            float dist = d.magnitude;
            if (dist < 1e-4f) return;
            Vector3 u = d / dist;
            dist = Mathf.Clamp(dist, Mathf.Abs(l1 - l2) + 0.002f, (l1 + l2) * 0.999f);
            float a = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 v = Vector3.ProjectOnPlane(pole - s, u);
            if (v.sqrMagnitude < 1e-8f) v = Vector3.ProjectOnPlane(low.position - s, u);
            v.Normalize();
            Vector3 e = s + u * a + v * h, tc = s + u * dist;
            // swing the upper arm so that both its direction AND the elbow hinge plane match (keeps the elbow a hinge, no twisted forearm)
            Vector3 cu = low.position - s, cl = hand.position - low.position;
            Vector3 n0 = Vector3.Cross(cu, cl), n1 = Vector3.Cross(e - s, tc - e);
            if (n0.sqrMagnitude > 1e-9f && n1.sqrMagnitude > 1e-9f)
                up.rotation = Quaternion.LookRotation(e - s, n1) * Quaternion.Inverse(Quaternion.LookRotation(cu, n0)) * up.rotation;
            else
                up.rotation = Quaternion.FromToRotation(cu, e - s) * up.rotation;
            low.rotation = Quaternion.FromToRotation(hand.position - low.position, tc - low.position) * low.rotation;
            hand.rotation = handRot;
        }
    }
}
