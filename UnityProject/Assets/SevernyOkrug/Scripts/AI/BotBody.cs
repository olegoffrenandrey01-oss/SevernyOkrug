// v07 bots: MPFB character (CH_Bot_A/B/C, Blender) with LOD, gear toggles (vest/helmet/cap/hood/hair), bone-attached hitboxes,
// procedural walk + aim (spine/head), both hands IK-locked to the weapon using the first-person grip poses, ragdoll on death.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    public class BotBody : MonoBehaviour
    {
        public Transform aim, eye;     // aim.rotation is driven by BotController (combat aim / lowered weapon)
        public WeaponView view;
        public bool combat;
        public Vector3 velocity;
        public bool Dead { get; private set; }
        public string variant;

        class Arm { public Transform up, low, hand; public float l1, l2; }

        Dictionary<string, Transform> _b;
        Transform _pelvis, _sp1, _sp2, _sp3, _neck, _head, _thL, _thR, _caL, _caR, _ftL, _ftR;
        Arm _armR, _armL;
        GripPose _grip;
        Vector3 _hip;
        bool _pistol;
        readonly List<Transform> _driven = new List<Transform>();
        readonly List<Quaternion> _rest = new List<Quaternion>();
        readonly List<GameObject> _hits = new List<GameObject>();
        readonly List<Rigidbody> _rbs = new List<Rigidbody>();
        Vector3 _pelvisRest;
        float _phase, _lower = 1f, _speed, _deadT, _calm;
        SkinnedMeshRenderer[] _smr;
        Rigidbody _gunRb;
        Quaternion _wRest = Quaternion.identity;   // v09: weapon root rest rotation (Blender FBX root is X-rotated)
        const float MaxAimYaw = 60f, MaxAimPitch = 70f;
        // v10.1: pistols are held out at arm's length just below the line of sight (two-handed stance). The FP rig pose
        // (gun 36 cm from the eye, elbows ~90 deg, wrists bent ~60 deg) looked like "T-rex arms" in third person.
        static readonly Vector3 PistolHold = new Vector3(0f, -0.055f, 0.46f);

        public static BotBody Create(Transform root, Health health, ItemDef wdef, System.Random rng)
        {
            string v = "ABC".Substring(rng.Next(3), 1);
            var g = ModelLib.Spawn("CH_Bot_" + v, root);
            if (g == null) return null;
            var bb = root.gameObject.AddComponent<BotBody>();
            bb.variant = v;
            if (!bb.Init(g.transform, health, wdef, rng))
            {
                Debug.LogWarning("[Severny] CH_Bot_" + v + ": rig incomplete, stickman fallback");
                Destroy(g); Destroy(bb); return null;
            }
            return bb;
        }

        Transform B(string n) => _b.TryGetValue(n, out var t) ? t : null;

        bool Init(Transform model, Health health, ItemDef wdef, System.Random rng)
        {
            _b = ModelLib.Map(model);
            // v10.2: same orientation self-check as the first-person arms (FBX axis conversion of the importer) - must run before the
            // hitboxes, ragdoll and eye are built from the bind pose
            var chk = RigCheck.Measure(model, transform, Vector3.zero, RigCheck.BotMaxAngle, RigCheck.BotMaxShift);
            if (chk.applied)
            {
                RigCheck.Apply(model, chk);
                RigCheck.Log("bot_" + variant, "[Severny] CH_Bot_" + variant + ": imported rig orientation corrected by " + chk, true);
            }
            else if (chk.measured && !chk.ok) RigCheck.Log("bot_" + variant, "[Severny] CH_Bot_" + variant + ": rig check - " + chk, true);
            _pelvis = B("pelvis"); _sp1 = B("spine_01"); _sp2 = B("spine_02"); _sp3 = B("spine_03"); _neck = B("neck_01"); _head = B("head");
            _thL = B("thigh_l"); _thR = B("thigh_r"); _caL = B("calf_l"); _caR = B("calf_r"); _ftL = B("foot_l"); _ftR = B("foot_r");
            _armR = MakeArm("r"); _armL = MakeArm("l");
            _smr = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (_pelvis == null || _sp1 == null || _sp2 == null || _sp3 == null || _neck == null || _head == null || _thL == null || _thR == null
                || _caL == null || _caR == null || _ftL == null || _ftR == null || _armR == null || _armL == null || _smr.Length == 0) return false;
            foreach (var an in model.GetComponentsInChildren<Animator>(true)) Destroy(an);
            bool helmet = health.helmet != null;
            Toggle("Helmet", helmet); Toggle("Vest", health.armor != null);
            Toggle("Cap", !helmet && rng.NextDouble() < 0.65); Toggle("Hood", !helmet && rng.NextDouble() < 0.5); Toggle("Hair", !helmet);
            foreach (var r in _smr) { r.updateWhenOffscreen = false; r.skinnedMotionVectors = false; r.shadowCastingMode = ShadowCastingMode.On; }
            foreach (var h in ModelSpecs.Hits) MakeHit(h, health);          // bind pose == spawn pose
            BuildRagdoll();                                                  // joints referenced to the bind pose, kinematic while alive
            eye = new GameObject("Eye").transform; eye.SetParent(_head, false);
            eye.position = transform.TransformPoint(ModelSpecs.Eye); eye.rotation = transform.rotation;
            aim = new GameObject("Aim").transform; aim.SetParent(transform, false); aim.localPosition = new Vector3(0f, 1.45f, 0f);
            foreach (var t in new[] { _pelvis, _sp1, _sp2, _sp3, _neck, _head, _thL, _thR, _caL, _caR, _ftL, _ftR,
                                      _armR.up, _armR.low, _armR.hand, _armL.up, _armL.low, _armL.hand })
            { _driven.Add(t); _rest.Add(t.localRotation); }
            _pelvisRest = _pelvis.localPosition;
            _pistol = wdef.isPistol;
            _grip = GripPose.Get(GripPose.TagFor(wdef));
            _hip = _pistol ? PistolHold : _grip != null ? _grip.hip : ModelSpecs.Hip_AV74;
            view = WeaponModels.Build(wdef, transform, false, false);
            if (view != null) _wRest = view.rest;
            if (_grip != null) foreach (var kv in _grip.fingers) { var f = B(kv.Key); if (f != null) f.localRotation = kv.Value; }
            SetupLod();
            return true;
        }

        Arm MakeArm(string s)
        {
            Transform up = B("upperarm_" + s), low = B("lowerarm_" + s), hand = B("hand_" + s);
            if (up == null || low == null || hand == null) return null;
            return new Arm { up = up, low = low, hand = hand, l1 = Vector3.Distance(up.position, low.position), l2 = Vector3.Distance(low.position, hand.position) };
        }

        void Toggle(string n, bool on) { var t = B(n); if (t != null) t.gameObject.SetActive(on); }

        void MakeHit(ModelSpecs.Hit h, Health health)
        {
            var bone = B(h.bone);
            if (bone == null) return;
            var g = new GameObject("HB_" + h.bone);
            var t = g.transform; t.SetParent(transform, false);
            if (h.type == 0)
            {
                t.localPosition = (h.a + h.b) * 0.5f; t.localRotation = Quaternion.FromToRotation(Vector3.up, h.b - h.a);
                var c = g.AddComponent<CapsuleCollider>(); c.direction = 1; c.radius = h.r; c.height = (h.b - h.a).magnitude + 2f * h.r;
            }
            else if (h.type == 1) { t.localPosition = h.a; g.AddComponent<BoxCollider>().size = h.b; }
            else { t.localPosition = h.a; g.AddComponent<SphereCollider>().radius = h.r; }
            t.SetParent(bone, true);
            var hb = g.AddComponent<Hitbox>(); hb.owner = health; hb.part = h.part;
            _hits.Add(g);
        }

        Renderer R(string n) { var t = B(n); return t != null && t.gameObject.activeSelf ? t.GetComponent<Renderer>() : null; }

        void SetupLod()
        {
            Renderer body = R("Body"), low = R("BodyLow");
            if (body == null || low == null) return;
            var common = new List<Renderer>();
            foreach (var n in new[] { "Helmet", "Vest", "Cap", "Hood", "Hair" }) { var r = R(n); if (r != null) common.Add(r); }
            if (view != null && view.root != null)
                foreach (var r in view.root.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.name != "flash") common.Add(r);
            var l0 = new List<Renderer>(common) { body };
            var l1 = new List<Renderer>(common) { low };
            var lg = gameObject.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(0.08f, l0.ToArray()), new LOD(0.0025f, l1.ToArray()) });   // ~15 m / ~500 m at 75° FOV
            lg.RecalculateBounds();
        }

        // ------------------------------------------------------------------ procedural pose (no Animator)
        void LateUpdate()
        {
            if (Dead) { DeadTick(); return; }
            float dt = Time.deltaTime;
            for (int i = 0; i < _driven.Count; i++) _driven[i].localRotation = _rest[i];
            _pelvis.localPosition = _pelvisRest;
            Vector3 up = transform.up, right = transform.right, fwd = transform.forward;
            // locomotion
            Vector3 v = velocity; v.y = 0f;
            float sp = v.magnitude;
            _speed = Mathf.Lerp(_speed, sp, 1f - Mathf.Exp(-8f * dt));
            float amt = Mathf.Clamp01(_speed / 1.0f), run = Mathf.InverseLerp(2.4f, 4.5f, _speed);
            _phase = Mathf.Repeat(_phase + _speed / (1.2f + 0.3f * _speed) * Mathf.PI * 2f * dt, Mathf.PI * 2f);
            Vector3 dir = sp > 0.15f ? v / sp : fwd;
            Vector3 ax = Vector3.Cross(up, dir).normalized;
            float A = amt * Mathf.Lerp(24f, 40f, run), K = amt * Mathf.Lerp(42f, 78f, run);
            float fd = Vector3.Dot(dir, fwd);
            Leg(_thL, _caL, _ftL, _phase, A, K, ax, right, fd);
            Leg(_thR, _caR, _ftR, _phase + Mathf.PI, A, K, ax, right, fd);
            _pelvis.position += up * (amt * 0.022f * (Mathf.Abs(Mathf.Cos(_phase)) - 0.6f) - run * 0.035f);
            // aim -> spine / head
            Quaternion aimQ = ClampedAim();                                             // never aim through the own back
            Vector3 lf = transform.InverseTransformDirection(aimQ * Vector3.forward);
            float pitch = -Mathf.Asin(Mathf.Clamp(lf.y, -1f, 1f)) * Mathf.Rad2Deg;   // + = down
            float yaw = Mathf.Atan2(lf.x, lf.z) * Mathf.Rad2Deg;                        // + = right
            _lower = Mathf.MoveTowards(_lower, combat ? 0f : 1f, dt * 2.5f);
            float stance = _pistol ? 4f : Mathf.Lerp(24f, 12f, _lower);
            float sYaw = stance + Mathf.Clamp(yaw, -45f, 45f) * 0.5f;
            float sPitch = Mathf.Clamp(pitch, -35f, 45f) * Mathf.Lerp(0.5f, 0.1f, _lower) + 4f * amt + 9f * run;
            Transform[] spine = { _sp1, _sp2, _sp3 };
            for (int i = 0; i < 3; i++)
            {
                Vector3 pax = Quaternion.AngleAxis(sYaw * (i + 1) / 3f, up) * right;
                spine[i].rotation = Quaternion.AngleAxis(sPitch / 3f, pax) * Quaternion.AngleAxis(sYaw / 3f, up) * spine[i].rotation;
            }
            float lookYaw = Mathf.Lerp(Mathf.Clamp(yaw, -70f, 70f), 0f, _lower), lookPitch = Mathf.Lerp(Mathf.Clamp(pitch, -45f, 50f) * 0.85f, 7f, _lower);
            float hYaw = lookYaw - sYaw, hPitch = lookPitch - sPitch;
            Vector3 hax = Quaternion.AngleAxis(lookYaw, up) * right;
            _neck.rotation = Quaternion.AngleAxis(hPitch * 0.4f, hax) * Quaternion.AngleAxis(hYaw * 0.4f, up) * _neck.rotation;
            _head.rotation = Quaternion.AngleAxis(hPitch * 0.6f, hax) * Quaternion.AngleAxis(hYaw * 0.6f, up) * _head.rotation;
            PoseWeapon(up, right, aimQ);
        }

        /// <summary>Aim rotation limited to the body's front cone (the controller turns the body; this keeps the gun in front of it
        /// while the body is still turning, and for network avatars whose aim/yaw arrive separately).</summary>
        Quaternion ClampedAim()
        {
            Vector3 lf = transform.InverseTransformDirection(aim.forward);
            float yaw = Mathf.Atan2(lf.x, lf.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(lf.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (Mathf.Abs(yaw) <= MaxAimYaw && Mathf.Abs(pitch) <= MaxAimPitch) return aim.rotation;
            return transform.rotation * Quaternion.Euler(Mathf.Clamp(pitch, -MaxAimPitch, MaxAimPitch), Mathf.Clamp(yaw, -MaxAimYaw, MaxAimYaw), 0f);
        }

        // both hands locked to the weapon: weapon at the eye-relative hip offset of the FP rig, arms start from the FP reference
        // orientation (correct forearm twist / elbow plane) and are IK-corrected for this body's shoulders
        void PoseWeapon(Vector3 up, Vector3 right, Quaternion wr)
        {
            if (view == null || view.root == null) return;
            Vector3 wp = eye.position + wr * _hip - up * (0.03f * _lower);
            if (_grip != null)
                for (int it = 0; it < 2; it++) { Reach(_armR, wp + wr * _grip.handPosR, ref wp); Reach(_armL, wp + wr * _grip.handPosL, ref wp); }
            view.root.transform.SetPositionAndRotation(wp, wr * _wRest);   // v09: barrel along the aim (was pointing down)
            if (_grip == null) return;
            IKArm(_armR, wp, wr, _grip.upRotR, _grip.lowRotR, _grip.handPosR, _grip.handRotR, up, right);
            IKArm(_armL, wp, wr, _grip.upRotL, _grip.lowRotL, _grip.handPosL, _grip.handRotL, up, -right);
        }

        static void Reach(Arm a, Vector3 t, ref Vector3 wp)
        {
            Vector3 d = t - a.up.position;
            float L = (a.l1 + a.l2) * 0.97f, m = d.magnitude;
            if (m > L) wp -= d * ((m - L) / m);      // pull the gun back instead of stretching the arm
        }

        static void IKArm(Arm a, Vector3 wp, Quaternion wr, Quaternion upRot, Quaternion lowRot, Vector3 hp, Quaternion hr, Vector3 up, Vector3 side)
        {
            a.up.rotation = wr * upRot; a.low.rotation = wr * lowRot;
            Vector3 pole = a.low.position - up * 0.12f + side * 0.04f;   // elbows slightly tucked down / out
            TwoBoneIK.Solve(a.up, a.low, a.hand, wp + wr * hp, wr * hr, pole, a.l1, a.l2);
        }

        // hip swing along the move direction, knee/foot about the body's right axis (anatomical hinge)
        static void Leg(Transform th, Transform ca, Transform ft, float ph, float A, float K, Vector3 ax, Vector3 right, float fd)
        {
            float s = Mathf.Sin(ph), c = Mathf.Cos(ph);
            float hip = A * s;
            float knee = K * Mathf.Pow(Mathf.Max(0f, c), 1.5f) + 0.25f * A * Mathf.Max(0f, -s);
            th.rotation = Quaternion.AngleAxis(-hip, ax) * th.rotation;
            ca.rotation = Quaternion.AngleAxis(knee, right) * ca.rotation;
            ft.rotation = Quaternion.AngleAxis(0.7f * (hip * fd - knee), right) * ft.rotation;
        }

        // ------------------------------------------------------------------ ragdoll (built in the bind pose -> anatomical joint limits)
        // CharacterJoint twist sign follows Unity's RagdollBuilder: knee flexion = negative twist about the body's right axis.
        BoxCollider _pelvisCol;
        bool _frozen;

        Rigidbody Rb(Transform t, float mass)
        {
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass; rb.isKinematic = true; rb.useGravity = true;
            rb.linearDamping = 0.05f; rb.angularDamping = 0.8f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 10; rb.solverVelocityIterations = 4; rb.maxDepenetrationVelocity = 3f;
            _rbs.Add(rb);
            return rb;
        }

        static void Joint(Rigidbody rb, Rigidbody parent, Vector3 axis, Vector3 swing, float lo, float hi, float s1, float s2)
        {
            var j = rb.gameObject.AddComponent<CharacterJoint>();
            j.anchor = Vector3.zero; j.autoConfigureConnectedAnchor = true;
            j.axis = rb.transform.InverseTransformDirection(axis.normalized);
            j.swingAxis = rb.transform.InverseTransformDirection(swing.normalized);
            j.connectedBody = parent;
            j.lowTwistLimit = new SoftJointLimit { limit = lo };
            j.highTwistLimit = new SoftJointLimit { limit = hi };
            j.swing1Limit = new SoftJointLimit { limit = s1 };
            j.swing2Limit = new SoftJointLimit { limit = s2 };
            j.enableProjection = true; j.projectionDistance = 0.05f; j.projectionAngle = 10f;
            j.enablePreprocessing = false;
        }

        void BuildRagdoll()
        {
            Vector3 R = transform.right, F = transform.forward;
            var pel = Rb(_pelvis, 11f);
            var g = new GameObject("RG_pelvis"); g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(0f, 0.92f, -0.01f);
            _pelvisCol = g.AddComponent<BoxCollider>(); _pelvisCol.size = new Vector3(0.32f, 0.2f, 0.22f);
            _pelvisCol.enabled = false;                       // only for the corpse (no hit zone there while alive)
            g.transform.SetParent(_pelvis, true);
            var ch = Rb(_sp3, 14f); Joint(ch, pel, R, F, -20f, 20f, 15f, 15f);
            var hd = Rb(_head, 5f); Joint(hd, ch, R, F, -40f, 25f, 25f, 20f);
            foreach (var a in new[] { _armR, _armL })
            {
                Vector3 ud = (a.low.position - a.up.position).normalized, ld = (a.hand.position - a.low.position).normalized;
                Vector3 n = Vector3.Cross(ud, ld);
                n = n.sqrMagnitude > 1e-4f ? n.normalized : -R;          // elbow hinge (flexion = +angle about n)
                var ua = Rb(a.up, 2.2f); Joint(ua, ch, ud, F, -35f, 35f, 70f, 50f);
                var la = Rb(a.low, 1.6f); Joint(la, ua, n, ld, -100f, 40f, 4f, 4f);
            }
            foreach (var th in new[] { _thL, _thR })
            {
                var t = Rb(th, 8f); Joint(t, pel, R, F, -20f, 70f, 30f, 15f);
                var c = Rb(th == _thL ? _caL : _caR, 4.5f); Joint(c, t, R, F, -110f, 3f, 3f, 3f);
            }
        }

        public void Die(DamageInfo d)
        {
            if (Dead) return;
            Dead = true; _deadT = 0f;
            var cols = new List<Collider>();
            foreach (var g in _hits)
            {
                if (g == null) continue;
                var hb = g.GetComponent<Hitbox>(); if (hb != null) Destroy(hb);
                var c = g.GetComponent<Collider>(); if (c != null) cols.Add(c);
            }
            if (_pelvisCol != null) { _pelvisCol.enabled = true; cols.Add(_pelvisCol); }
            var gun = DropGun(); if (gun != null) cols.Add(gun);
            for (int i = 0; i < cols.Count; i++)
                for (int k = i + 1; k < cols.Count; k++) Physics.IgnoreCollision(cols[i], cols[k]);
            var pc = PlayerController.Local;
            if (pc != null && pc.cc != null) foreach (var c in cols) Physics.IgnoreCollision(c, pc.cc);
            foreach (var r in _smr) if (r != null) r.updateWhenOffscreen = true;
            Vector3 v = velocity; v.y = Mathf.Min(v.y, 0f);
            foreach (var rb in _rbs)
            {
                rb.isKinematic = false; rb.interpolation = RigidbodyInterpolation.Interpolate;
                if (rb != _gunRb) rb.linearVelocity = v;
            }
            if (d.dir.sqrMagnitude > 1e-4f && d.point != Vector3.zero)
            {
                Rigidbody best = null; float bd = float.MaxValue;
                foreach (var rb in _rbs) { if (rb == _gunRb) continue; float q = (rb.worldCenterOfMass - d.point).sqrMagnitude; if (q < bd) { bd = q; best = rb; } }
                if (best != null) best.AddForceAtPosition(d.dir.normalized * Random.Range(25f, 40f), d.point, ForceMode.Impulse);
            }
        }

        Collider DropGun()
        {
            if (view == null || view.root == null) return null;
            var t = view.root.transform;
            var b = new Bounds(); bool any = false;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.gameObject.name == "flash") continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var p = t.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any) return null;
            var bc = view.root.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = Vector3.Max(b.size, Vector3.one * 0.03f);
            _gunRb = view.root.AddComponent<Rigidbody>(); _gunRb.mass = _pistol ? 0.9f : 3.4f;
            _gunRb.linearDamping = 0.1f; _gunRb.angularDamping = 0.5f;
            _gunRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _gunRb.linearVelocity = velocity + Random.insideUnitSphere * 0.6f;
            _gunRb.angularVelocity = Random.insideUnitSphere * 2f;
            _rbs.Add(_gunRb);
            return bc;
        }

        // freeze the corpse once it settled (or after 7 s): kinematic, fixed bounds, loot trigger
        void DeadTick()
        {
            if (_frozen) return;
            _deadT += Time.deltaTime;
            bool still = _deadT > 1.5f;
            if (still) foreach (var rb in _rbs) if (rb != null && !rb.IsSleeping() && rb.linearVelocity.sqrMagnitude > 0.01f) { still = false; break; }
            if (!still && _deadT < 7f) return;
            _frozen = true;
            foreach (var rb in _rbs) if (rb != null) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            foreach (var r in _smr)
            {
                if (r == null) continue;
                r.updateWhenOffscreen = false;
                var rt = r.rootBone != null ? r.rootBone : r.transform;
                float sc = Mathf.Max(1e-4f, rt.lossyScale.x);
                r.localBounds = new Bounds(rt.InverseTransformPoint(_pelvis.position), Vector3.one * (2.4f / sc));
            }
            var lz = new GameObject("LootZone"); lz.transform.SetParent(transform, false);
            lz.transform.position = (_pelvis.position + _sp3.position) * 0.5f;
            var sc2 = lz.AddComponent<SphereCollider>(); sc2.isTrigger = true; sc2.radius = 0.45f;
            var lg = GetComponent<LODGroup>(); if (lg != null) lg.RecalculateBounds();
            enabled = false;
        }
    }
}
