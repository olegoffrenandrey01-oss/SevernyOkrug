// Procedural low-poly weapon models built from primitives (original shapes, no external assets).
using UnityEngine;

namespace Severny
{
    public class WeaponView
    {
        public GameObject root;
        public Transform muzzle, mag;
        public Vector3 magRest;
        public GameObject flash;
        public Light light;
        // v07 detailed models (W_AV74 / W_P9M)
        public bool detailed;
        public string tag;
        public Transform bolt;
        public Vector3 boltRest, boltBack, sightLocal;
        public Transform sight, front;   // v09: rear / front sight anchors (ADS line)
        // v09: local rotation of root (relative to its parent) that points the barrel along the parent's +Z with the sights up.
        // Blender FBX roots carry a -90 deg X rotation, so world poses must be set as  aimRotation * rest  (never aimRotation alone).
        public Quaternion rest = Quaternion.identity;
        public Vector3 magDown = Vector3.down;   // "down" of the weapon in the mag's parent space (reload drop direction)
    }

    public static class WeaponModels
    {
        static Material Metal => GameAssets.Lit(new Color(0.09f, 0.09f, 0.09f), 0.42f, 0.55f);
        static Material Wood => GameAssets.Lit(new Color(0.30f, 0.17f, 0.08f), 0.32f);
        static Material Bakelite => GameAssets.Lit(new Color(0.26f, 0.12f, 0.06f), 0.35f);
        static Material Polymer => GameAssets.Lit(new Color(0.12f, 0.11f, 0.10f), 0.25f);
        static Material Glove => GameAssets.Lit(new Color(0.16f, 0.15f, 0.13f), 0.1f);
        static Material Sleeve => GameAssets.Lit(new Color(0.22f, 0.25f, 0.19f), 0.08f);

        public static WeaponView Build(ItemDef def, Transform parent, bool hands, bool firstPerson)
        {
            var v = TryDetailed(def, parent);
            if (v == null)
            {
                v = new WeaponView();
                v.root = new GameObject("W_" + def.id);
                v.root.transform.SetParent(parent, false);
                var t = v.root.transform;
                if (def.isPistol) BuildPistol(v, t, hands); else BuildRifle(v, t, hands);
            }
            // muzzle flash
            v.flash = Prim.Make(PrimitiveType.Sphere, v.muzzle, new Vector3(0, 0, 0.05f), new Vector3(0.05f, 0.05f, 0.14f), GameAssets.Emissive(new Color(1f, 0.62f, 0.25f), 6f), false, null, "flash");
            v.flash.SetActive(false);
            var lg = new GameObject("flashLight"); lg.transform.SetParent(v.muzzle, false); lg.transform.localPosition = new Vector3(0, 0, 0.1f);
            v.light = lg.AddComponent<Light>(); v.light.type = LightType.Point; v.light.range = 7f; v.light.intensity = 2.5f; v.light.color = new Color(1f, 0.7f, 0.4f);
            v.light.shadows = LightShadows.None; v.light.enabled = false;
            foreach (var r in v.root.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = firstPerson ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            if (v.mag != null)
            {
                v.magRest = v.mag.localPosition;
                var mp = v.mag.parent != null ? v.mag.parent : v.root.transform;
                v.magDown = mp.InverseTransformDirection(-parent.up).normalized;
            }
            v.rest = v.root.transform.localRotation;
            return v;
        }

        /// <summary>Blender-made model (Resources/SevernyModels/W_TAG) with anchors muzzle / sight / Mag / Bolt.</summary>
        static WeaponView TryDetailed(ItemDef def, Transform parent)
        {
            string tag = GripPose.TagFor(def);
            if (tag == null) return null;
            var g = ModelLib.Spawn("W_" + tag, parent);
            if (g == null) return null;
            var t = g.transform;
            Transform mz = ModelLib.FindDeep(t, "muzzle"), sight = ModelLib.FindDeep(t, "sight");
            if (mz == null || sight == null) { Object.Destroy(g); return null; }
            // safety: whatever axis conversion the importer applied, make the barrel point along +Z and the sights up
            Vector3 m = parent.InverseTransformPoint(mz.position) - t.localPosition;
            if (Vector3.Angle(m, Vector3.forward) > 2f) t.localRotation = Quaternion.FromToRotation(m, Vector3.forward) * t.localRotation;
            Vector3 sp = parent.InverseTransformPoint(sight.position) - t.localPosition;
            float roll = Mathf.Atan2(sp.x, sp.y) * Mathf.Rad2Deg;
            if (Mathf.Abs(roll) > 2f && new Vector2(sp.x, sp.y).magnitude > 0.005f) t.localRotation = Quaternion.AngleAxis(roll, Vector3.forward) * t.localRotation;
            // v09: the sight line (rear -> front sight) must be parallel to the holder axis, otherwise the ADS picture points off the
            // camera axis while the bullets fly along it (looks like the shots drift sideways)
            Transform front = ModelLib.FindDeep(t, "front");
            if (front != null)
            {
                Vector3 sl = parent.InverseTransformPoint(front.position) - parent.InverseTransformPoint(sight.position);
                float ang = Vector3.Angle(sl, Vector3.forward);
                if (sl.sqrMagnitude > 1e-4f && ang > 0.05f && ang < 15f)
                {
                    Vector3 pivot = sight.position;
                    t.localRotation = Quaternion.FromToRotation(sl, Vector3.forward) * t.localRotation;
                    t.position += pivot - sight.position;          // rotate about the rear sight (keeps the ADS eye relief)
                }
            }
            var v = new WeaponView { root = g, detailed = true, tag = tag, sight = sight, front = front };
            var fx = new GameObject("fx").transform; fx.SetParent(t, false);
            fx.position = mz.position; fx.rotation = parent.rotation;
            v.muzzle = fx;
            v.mag = ModelLib.FindDeep(t, "Mag");
            v.bolt = ModelLib.FindDeep(t, "Bolt");
            v.sightLocal = parent.InverseTransformPoint(sight.position);
            if (v.bolt != null) { v.boltRest = v.bolt.localPosition; v.boltBack = v.bolt.parent.InverseTransformDirection(-parent.forward); }
            return v;
        }

        static void BuildRifle(WeaponView v, Transform t, bool hands)
        {
            Prim.Box(t, new Vector3(0, 0, 0.05f), new Vector3(0.05f, 0.075f, 0.38f), Metal, false, null, "receiver");
            Prim.Box(t, new Vector3(0, 0.044f, 0.04f), new Vector3(0.046f, 0.02f, 0.3f), Metal, false, null, "cover");
            Prim.Box(t, new Vector3(0, -0.006f, 0.32f), new Vector3(0.056f, 0.06f, 0.2f), Wood, false, null, "handguard");
            Prim.Box(t, new Vector3(0, 0.043f, 0.31f), new Vector3(0.04f, 0.03f, 0.17f), Wood, false, null, "gastube");
            Prim.Cyl(t, new Vector3(0, 0.012f, 0.56f), 0.021f, 0.34f, Metal, false, Quaternion.Euler(90, 0, 0), "barrel");
            Prim.Cyl(t, new Vector3(0, 0.012f, 0.745f), 0.032f, 0.07f, Metal, false, Quaternion.Euler(90, 0, 0), "brake");
            Prim.Box(t, new Vector3(0, 0.04f, 0.685f), new Vector3(0.01f, 0.045f, 0.012f), Metal, false, null, "frontsight");
            Prim.Box(t, new Vector3(0, 0.06f, 0.13f), new Vector3(0.03f, 0.012f, 0.03f), Metal, false, null, "rearsight");
            Prim.Box(t, new Vector3(0, -0.085f, -0.035f), new Vector3(0.034f, 0.1f, 0.045f), Bakelite, false, Quaternion.Euler(-16, 0, 0), "grip");
            Prim.Box(t, new Vector3(0, -0.028f, -0.26f), new Vector3(0.045f, 0.085f, 0.27f), Wood, false, Quaternion.Euler(6, 0, 0), "stock");
            Prim.Box(t, new Vector3(0, -0.05f, -0.385f), new Vector3(0.047f, 0.12f, 0.02f), Polymer, false, Quaternion.Euler(6, 0, 0), "buttplate");
            var mag = new GameObject("mag").transform; mag.SetParent(t, false); mag.localPosition = new Vector3(0, -0.04f, 0.115f);
            Prim.Box(mag, new Vector3(0, -0.055f, 0.005f), new Vector3(0.03f, 0.11f, 0.07f), Bakelite, false, Quaternion.Euler(12, 0, 0), "mag_a");
            Prim.Box(mag, new Vector3(0, -0.14f, 0.035f), new Vector3(0.03f, 0.08f, 0.068f), Bakelite, false, Quaternion.Euler(28, 0, 0), "mag_b");
            v.mag = mag;
            var mz = new GameObject("muzzle").transform; mz.SetParent(t, false); mz.localPosition = new Vector3(0, 0.012f, 0.785f);
            v.muzzle = mz;
            if (hands)
            {
                Prim.Box(t, new Vector3(-0.005f, -0.05f, 0.33f), new Vector3(0.07f, 0.05f, 0.1f), Glove, false, Quaternion.Euler(0, 0, -20), "handL");
                Prim.Box(t, new Vector3(-0.09f, -0.13f, 0.18f), new Vector3(0.075f, 0.075f, 0.34f), Sleeve, false, Quaternion.Euler(-20, 28, 0), "armL");
                Prim.Box(t, new Vector3(0.012f, -0.085f, -0.02f), new Vector3(0.055f, 0.07f, 0.08f), Glove, false, Quaternion.Euler(-16, 0, 0), "handR");
                Prim.Box(t, new Vector3(0.06f, -0.16f, -0.16f), new Vector3(0.08f, 0.08f, 0.3f), Sleeve, false, Quaternion.Euler(-35, -12, 0), "armR");
            }
        }

        static void BuildPistol(WeaponView v, Transform t, bool hands)
        {
            Prim.Box(t, Vector3.zero, new Vector3(0.03f, 0.034f, 0.165f), Metal, false, null, "slide");
            Prim.Box(t, new Vector3(0, -0.028f, 0.005f), new Vector3(0.028f, 0.024f, 0.15f), Metal, false, null, "frame");
            Prim.Box(t, new Vector3(0, -0.082f, -0.052f), new Vector3(0.031f, 0.105f, 0.046f), Bakelite, false, Quaternion.Euler(-12, 0, 0), "grip");
            Prim.Box(t, new Vector3(0, -0.052f, 0.028f), new Vector3(0.008f, 0.028f, 0.05f), Metal, false, null, "guard");
            Prim.Box(t, new Vector3(0, 0.022f, 0.072f), new Vector3(0.006f, 0.01f, 0.008f), Metal, false, null, "fsight");
            Prim.Box(t, new Vector3(0, 0.022f, -0.072f), new Vector3(0.024f, 0.01f, 0.008f), Metal, false, null, "rsight");
            var mag = new GameObject("mag").transform; mag.SetParent(t, false); mag.localPosition = new Vector3(0, -0.04f, -0.05f);
            Prim.Box(mag, new Vector3(0, -0.055f, 0f), new Vector3(0.024f, 0.09f, 0.034f), Polymer, false, Quaternion.Euler(-12, 0, 0), "mag_a");
            v.mag = mag;
            var mz = new GameObject("muzzle").transform; mz.SetParent(t, false); mz.localPosition = new Vector3(0, 0, 0.09f);
            v.muzzle = mz;
            if (hands)
            {
                Prim.Box(t, new Vector3(0.0f, -0.08f, -0.05f), new Vector3(0.06f, 0.075f, 0.075f), Glove, false, Quaternion.Euler(-12, 0, 0), "handR");
                Prim.Box(t, new Vector3(-0.02f, -0.085f, -0.035f), new Vector3(0.05f, 0.065f, 0.07f), Glove, false, Quaternion.Euler(-12, 20, 0), "handL");
                Prim.Box(t, new Vector3(0.05f, -0.16f, -0.2f), new Vector3(0.075f, 0.075f, 0.3f), Sleeve, false, Quaternion.Euler(-28, -10, 0), "armR");
                Prim.Box(t, new Vector3(-0.08f, -0.16f, -0.19f), new Vector3(0.075f, 0.075f, 0.3f), Sleeve, false, Quaternion.Euler(-28, 22, 0), "armL");
            }
        }
    }
}
