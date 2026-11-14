// Extraction zone: stand inside for N seconds. Marked by a pole with a dim green lamp.
using UnityEngine;

namespace Severny
{
    public class ExtractionPoint : MonoBehaviour
    {
        public string exitName = "Выход";
        public float radius = 6f, duration = 8f;
        public bool active = true;
        float _t;
        bool _inside;

        public static ExtractionPoint Create(string name, Vector3 pos, Transform parent)
        {
            var g = new GameObject("EXIT_" + name);
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            var e = g.AddComponent<ExtractionPoint>();
            e.exitName = name;
            var pole = GameAssets.Lit(new Color(0.3f, 0.3f, 0.28f), 0.3f, 0.5f);
            Prim.Cyl(g.transform, new Vector3(0, 1.4f, 0), 0.08f, 2.8f, pole, true);
            Prim.Sphere(g.transform, new Vector3(0, 2.9f, 0), 0.22f, GameAssets.Emissive(new Color(0.3f, 1f, 0.35f), 3f), false);
            var lg = new GameObject("lamp"); lg.transform.SetParent(g.transform, false); lg.transform.localPosition = new Vector3(0, 2.7f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.4f, 1f, 0.45f); l.range = 9f; l.intensity = 1.6f; l.shadows = LightShadows.None;
            Prim.Box(g.transform, new Vector3(0, 2.2f, 0.06f), new Vector3(0.7f, 0.35f, 0.03f), GameAssets.Lit(new Color(0.12f, 0.28f, 0.14f), 0.2f), false);
            return e;
        }

        void Update()
        {
            var p = PlayerController.Local;
            if (!active || p == null || p.health.IsDead || RaidManager.I == null || !RaidManager.I.Running) { if (_inside) Leave(); return; }
            Vector3 d = p.transform.position - transform.position; d.y *= 0.3f;
            bool inside = d.magnitude < radius;
            if (inside && !_inside) { _inside = true; _t = 0f; SFX.Play2D(SFX.Beep, 0.5f); RaidManager.I.SetExtractInfo(exitName, duration); }
            else if (!inside && _inside) Leave();
            if (!_inside) return;
            _t += Time.deltaTime;
            RaidManager.I.SetExtractInfo(exitName, Mathf.Max(0f, duration - _t));
            if (_t >= duration) { _inside = false; RaidManager.I.Extract(exitName); }
        }

        void Leave() { _inside = false; _t = 0f; if (RaidManager.I != null) RaidManager.I.SetExtractInfo(null, 0f); }
    }
}
