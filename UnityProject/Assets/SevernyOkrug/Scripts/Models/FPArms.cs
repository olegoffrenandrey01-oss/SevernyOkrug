// First-person arms (FP_Arms_<TAG>): spawned at the eye under the camera; both hands IK-locked to the weapon grips,
// shoulders follow the weapon by half of its offset; the left hand can be pulled to the magazine during reloads.
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    public class FPArms : MonoBehaviour
    {
        public Transform weapon;
        public Vector3 hipLocal;                 // weapon root rest position in camera space
        // v10.1: set by PlayerWeapon every frame - ADS blend and the static holder target (hip..ADS, reload pull-in).
        // Pistol: the shoulders follow only the dynamic rest of the weapon offset (sway, kick, lowering) plus a small lift in ADS.
        public float ads;
        public Vector3 poseLocal;
        public float leftWeight;                 // 0..1 blend of the left hand towards leftPos/leftRot (world)
        public Vector3 leftPos;
        public Quaternion leftRot = Quaternion.identity;
        public bool expectVisible;               // v10.2: set by PlayerWeapon (not lowered / retracted / reloading) -> out-of-view watchdog
        /// <summary>v10.2 diagnostics (F3 overlay, Console): the arms currently on screen and their rig check.</summary>
        public static FPArms Active;
        public string rigTag;
        public RigCheck.Result check;
        public bool mountFallback;
        Transform _cam;
        Camera _camC;
        Vector3 _rootRest;
        // v10.2: hand offsets live in the camera-aligned frame of the rig; at run time that frame is the weapon holder
        // (weapon.rotation * inverse(rest local rotation of the weapon root)) - no longer derived from the imported weapon_mount
        // rotation, which follows whatever axis conversion the importer applied.
        Quaternion _wRest = Quaternion.identity;
        Arm _r, _l;
        bool _pistol;
        Renderer[] _wr;
        float _hiddenT, _nextVis;
        bool _reported;
        // v10.1 ("T-rex" pistol): the P9M rig was authored with the gun 36 cm in front of the eye -> elbows ~90 deg, forearms
        // rising 45 deg, wrists bent ~60 deg. Hold it 13 cm further out (arms almost straight, two-handed stance) and lift the
        // shoulders a bit when aiming: elbows ~150 deg, wrists ~30 deg (checked by re-solving the same IK in Blender).
        public static readonly Vector3 PistolHoldOffset = new Vector3(-0.01f, -0.035f, 0.13f);
        static readonly Vector3 PistolAdsShoulders = new Vector3(-0.01f, 0.07f, 0f);

        class Arm
        {
            public Transform up, low, hand;
            public Quaternion upRest, lowRest, handRest, handRot;
            public Vector3 handPos, elbow;
            public float l1, l2, err;
        }

        public static FPArms Create(string tag, Transform cam, Transform weapon)
        {
            var g = ModelLib.Spawn("FP_Arms_" + tag, cam);
            if (g == null) return null;
            var a = g.AddComponent<FPArms>();
            if (!a.Init(cam, weapon, tag)) { Destroy(g); return null; }
            return a;
        }

        bool Init(Transform cam, Transform w, string tag)
        {
            _cam = cam; _camC = cam.GetComponent<Camera>(); weapon = w; _pistol = tag == "P9M"; rigTag = tag;
            foreach (var an in GetComponentsInChildren<Animator>(true)) Destroy(an);   // empty Animator from the Generic rig import
            // v10.2: orientation self-check. The arms looked "rotated" in game (pistol and hands gone at the hip, open hands "begging"
            // around the pistol in ADS, no hands on the rifle) - exactly what a rig turned by 90 deg about the eye gives. Measure the body
            // bones against the reference skeleton (eye at the origin) and put the whole rig back, whatever the FBX import did.
            check = RigCheck.Measure(transform, cam, RigCheck.FPOffset, RigCheck.FPMaxAngle, RigCheck.FPMaxShift);
            if (check.applied)
            {
                RigCheck.Apply(transform, check);
                RigCheck.Log("fp_" + tag, "[Severny] FP_Arms_" + tag + ": imported rig orientation corrected by " + check, true);
            }
            else if (!check.ok) RigCheck.Log("fp_" + tag, "[Severny] FP_Arms_" + tag + ": rig check - " + check, true);
            var mount = ModelLib.FindDeep(transform, "weapon_mount");
            Vector3 mc = RigCheck.CheckMount(tag, mount != null ? cam.InverseTransformPoint(mount.position) : RigCheck.RefMount(tag), "fp_" + tag);
            mountFallback = mount == null || (mc - cam.InverseTransformPoint(mount.position)).sqrMagnitude > 1e-6f;
            _r = MakeArm("r", mc); _l = MakeArm("l", mc);
            if (_r == null || _l == null) { Debug.LogWarning("[Severny] FP arms: arm bones missing"); return false; }
            hipLocal = mc + (_pistol ? PistolHoldOffset : Vector3.zero);
            poseLocal = hipLocal;
            // the weapon keeps the orientation WeaponModels gave it (barrel and sight line on the camera axis); the hands follow it via Frame
            _wRest = w.localRotation;
            _rootRest = transform.localPosition;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
                r.updateWhenOffscreen = true; r.skinnedMotionVectors = false;
            }
            var wr = new System.Collections.Generic.List<Renderer>();
            foreach (var r in w.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.name != "flash") wr.Add(r);
            _wr = wr.ToArray();
            Active = this;
            RigCheck.Log("fpinfo_" + tag, "[Severny] FP_Arms_" + tag + ": rig " + (check.applied ? "corrected" : check.ok ? "OK" : "unchecked")
                + " (" + check + "), hip " + hipLocal.ToString("F3") + ", arms " + _r.l1.ToString("F2") + "+" + _r.l2.ToString("F2") + " m"
                + (mountFallback ? ", reference weapon_mount" : ""), false);
            return true;
        }

        void OnDestroy() { if (Active == this) Active = null; }

        Arm MakeArm(string s, Vector3 mountCam)
        {
            Transform up = ModelLib.FindDeep(transform, "upperarm_" + s), low = ModelLib.FindDeep(transform, "lowerarm_" + s), hand = ModelLib.FindDeep(transform, "hand_" + s);
            if (up == null || low == null || hand == null) return null;
            Quaternion ci = Quaternion.Inverse(_cam.rotation);
            return new Arm
            {
                up = up, low = low, hand = hand,
                upRest = up.localRotation, lowRest = low.localRotation, handRest = hand.localRotation,
                handPos = _cam.InverseTransformPoint(hand.position) - mountCam, handRot = ci * hand.rotation,
                elbow = _cam.InverseTransformPoint(low.position) - mountCam,
                l1 = Vector3.Distance(up.position, low.position), l2 = Vector3.Distance(low.position, hand.position)
            };
        }

        void LateUpdate()
        {
            if (weapon == null || _cam == null) return;
            Vector3 wl = _cam.InverseTransformPoint(weapon.position);
            if (_pistol) transform.localPosition = _rootRest + PistolAdsShoulders * ads + Vector3.ClampMagnitude((wl - poseLocal) * 0.5f, 0.12f);
            else transform.localPosition = _rootRest + Vector3.ClampMagnitude((wl - hipLocal) * 0.5f, 0.12f);
            Solve(_r, false);
            Solve(_l, leftWeight > 0.001f);
            Watchdog();
        }

        void Solve(Arm a, bool alt)
        {
            a.up.localRotation = a.upRest; a.low.localRotation = a.lowRest; a.hand.localRotation = a.handRest;
            Quaternion wf = Frame;
            Vector3 t = weapon.position + wf * a.handPos;
            Quaternion r = wf * a.handRot;
            if (alt) { t = Vector3.Lerp(t, leftPos, leftWeight); r = Quaternion.Slerp(r, leftRot, leftWeight); }
            Vector3 pole = Vector3.Lerp(weapon.position + wf * a.elbow, _cam.TransformPoint(hipLocal + a.elbow), 0.5f);
            TwoBoneIK.Solve(a.up, a.low, a.hand, t, r, pole, a.l1, a.l2);
            a.err = Vector3.Distance(a.hand.position, t);
        }

        // v10.2: if the weapon stays out of the picture while it should be up, write the whole FP state to the Console once
        void Watchdog()
        {
            if (_reported || _camC == null || Time.time < _nextVis) return;
            _nextVis = Time.time + 0.25f;
            if (!expectVisible) { _hiddenT = 0f; return; }
            bool vis = false;
            foreach (var r in _wr)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Vector3 v = _camC.WorldToViewportPoint(r.bounds.center);
                if (v.z > _camC.nearClipPlane && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f) { vis = true; break; }
            }
            _hiddenT = vis ? 0f : _hiddenT + 0.25f;
            if (_hiddenT < 1.5f) return;
            _reported = true;
            Debug.LogWarning("[Severny] FP " + rigTag + ": weapon is out of view - " + Describe());
        }

        /// <summary>Rest-pose rotation of the left hand relative to the weapon (for reload targets).</summary>
        public Quaternion LeftGripRot => weapon != null ? Frame * _l.handRot : Quaternion.identity;

        /// <summary>Camera-aligned frame of the weapon == weapon holder rotation (camera rotation when the weapon is at rest).</summary>
        Quaternion Frame => weapon.rotation * Quaternion.Inverse(_wRest);

        public string Describe()
        {
            if (weapon == null || _cam == null) return rigTag + ": no weapon";
            Vector3 wl = _cam.InverseTransformPoint(weapon.position);
            float pitch = Vector3.SignedAngle(_cam.forward, weapon.parent != null ? weapon.parent.forward : weapon.forward, _cam.right);
            return rigTag + " rig " + (check.applied ? "corrected " : check.ok ? "OK " : "unchecked ") + check.angle.ToString("F0") + " deg"
                + (mountFallback ? " (ref mount)" : "") + " | weapon " + wl.ToString("F2") + " pitch " + pitch.ToString("F0")
                + " | hip " + hipLocal.ToString("F2") + " ads " + ads.ToString("F2") + " | hands err " + _r.err.ToString("F3") + "/" + _l.err.ToString("F3")
                + (_camC != null ? " | fov " + _camC.fieldOfView.ToString("F0") : "");
        }

        /// <summary>F3 overlay line (null when no first-person arms are active).</summary>
        public static string DebugLine() => Active != null && Active.isActiveAndEnabled ? Active.Describe() : null;
    }
}
