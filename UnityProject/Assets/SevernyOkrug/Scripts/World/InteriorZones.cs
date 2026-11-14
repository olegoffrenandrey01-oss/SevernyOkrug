// Interior "eye adaptation" for PolygonKit maps: while the camera is inside an interior zone (mall halls, shops,
// warehouses — boxes exported from Blender) the sky ambient and reflections are dimmed and post exposure is raised,
// smoothly over ~0.8 s. Restores the outdoor values when the raid ends (component lives on the RaidManager object).
// v13: with the «Волна» look the ambient is a custom SH probe (AmbientMode.Custom) — then the probe is scaled.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Severny
{
    public class InteriorZones : MonoBehaviour
    {
        const float AmbientInside = 0.45f, ReflInside = 0.4f, ExposureInside = 0.35f, FadeTime = 0.8f;
        Color _sky, _eq, _gnd;
        float _refl, _exp, _f, _applied = -1f;
        ColorAdjustments _ca;
        bool _shMode;
        SphericalHarmonicsL2 _probe;

        /// <summary>Screenshot tool: the next LateUpdate jumps to the inside / outside state without the fade.</summary>
        public static bool SnapNext;

        void Start()
        {
            _sky = RenderSettings.ambientSkyColor; _eq = RenderSettings.ambientEquatorColor; _gnd = RenderSettings.ambientGroundColor;
            _shMode = RenderSettings.ambientMode == AmbientMode.Custom;
            _probe = RenderSettings.ambientProbe;
            _refl = RenderSettings.reflectionIntensity;
            _ca = EnvironmentSetup.ColorAdjust;
            _exp = _ca != null ? _ca.postExposure.value : 0f;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            bool inside = cam != null && MapAnchors.InsideZone(cam.transform.position);
            if (SnapNext) { SnapNext = false; _f = inside ? 1f : 0f; _applied = -1f; }
            _f = Mathf.MoveTowards(_f, inside ? 1f : 0f, Time.unscaledDeltaTime / FadeTime);
            float s = Mathf.SmoothStep(0f, 1f, _f);
            if (Mathf.Abs(s - _applied) < 0.004f) return;
            Apply(s);
        }

        void Apply(float s)
        {
            _applied = s;
            float a = Mathf.Lerp(1f, AmbientInside, s);
            if (_shMode && RenderSettings.ambientMode == AmbientMode.Custom) RenderSettings.ambientProbe = _probe * a;
            else { RenderSettings.ambientSkyColor = _sky * a; RenderSettings.ambientEquatorColor = _eq * a; RenderSettings.ambientGroundColor = _gnd * a; }
            RenderSettings.reflectionIntensity = _refl * Mathf.Lerp(1f, ReflInside, s);
            if (_ca != null) _ca.postExposure.value = _exp + ExposureInside * s;
        }

        void OnDestroy()
        {
            if (_applied > 0f) Apply(0f);
        }
    }
}
