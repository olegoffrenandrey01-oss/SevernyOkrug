// v13.1: distant skyline of «Волна» — panel districts, the ТЭЦ with its cooling-tower plumes, the TV tower (az 205°), cranes, a power line.
// A textured band (−1.25°…+10°) 960 m around whichever camera renders, like GfxHaze: texture RGB = colour relative to the fog colour,
// so it follows every look (day / morning / evening / rain); shader without fog (Sprites/Default) — the haze is painted into the
// texture, the band strength comes from the look (thick fog → hidden). Drawn right after the haze band, before smoke / rain / glass.
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    public static class GfxSkyline
    {
        const float Radius = 960f, Bottom = -1.25f, Top = 10f, Headroom = 1.6f;   // Headroom: texture = rel / 1.6 (steam is brighter than the fog)
        const string TexPath = "VolnaLook/VOL_Skyline";
        static GameObject _go;
        static Material _mat;
        static Mesh _mesh;
        static bool _hooked, _failed, _wanted;
        public static string Info { get; private set; } = "нет";

        /// <summary>fogGamma: fog colour of the look (gamma), fog: exp² density, haze: horizon band strength.</summary>
        public static void Set(Color fogGamma, float fog, float haze)
        {
            _wanted = false;
            float k = Mathf.Clamp01((0.0068f - fog) / 0.0036f) * (1f - 0.6f * Mathf.Clamp01((haze - 0.75f) / 0.25f));
            if (k < 0.05f) { Hide(); Info = "скрыт (туман)"; return; }
            if (_failed) return;
            if (_go == null && !Create()) return;
            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color c = (lin ? fogGamma.linear : fogGamma) * Headroom;
            if (lin) c = c.gamma;                 // material colours are gamma; Unity linearises them for the shader
            c.a = k;
            _mat.color = c;
            _wanted = true;
            _go.SetActive(true);
            Info = "вкл " + Mathf.RoundToInt(k * 100f) + "%";
        }

        public static void Hide() { _wanted = false; if (_go != null) _go.SetActive(false); }
        public static bool Visible { get => _go != null && _go.activeSelf; set { if (_go != null) _go.SetActive(value && _wanted); } }

        static bool Create()
        {
            var tex = Resources.Load<Texture2D>(TexPath);
            if (tex == null) { _failed = true; Info = "нет текстуры " + TexPath; return false; }
            Shader sh = null;
            foreach (var n in new[] { "Sprites/Default", "UI/Default" })
            {
                var s = Shader.Find(n);
                if (s != null && s.isSupported) { sh = s; break; }
            }
            if (sh == null) { _failed = true; Info = "нет шейдера Sprites/Default"; Debug.LogWarning("[Severny] горизонт v13.1: нет шейдера Sprites/Default — дальний город выключен"); return false; }
            tex.wrapModeU = TextureWrapMode.Repeat; tex.wrapModeV = TextureWrapMode.Clamp;
            _mat = new Material(sh) { name = "SEV_Skyline", hideFlags = HideFlags.DontSave, mainTexture = tex };
            _mat.renderQueue = 2502;              // after the haze band (2501), before smoke, rain, glass
            _mesh = BuildMesh();
            _go = new GameObject("SEV_Skyline");
            Object.DontDestroyOnLoad(_go);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            if (!_hooked) { RenderPipelineManager.beginCameraRendering += OnCamera; _hooked = true; }
            return true;
        }

        static void OnCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_go == null || cam == null || !_go.activeSelf) return;
            if (cam.cameraType == CameraType.Preview) return;
            _go.transform.position = cam.transform.position;
            float s = cam.farClipPlane < Radius * 1.05f ? Mathf.Max(0.05f, cam.farClipPlane * 0.9f / Radius) : 1f;
            if (Mathf.Abs(_go.transform.localScale.x - s) > 1e-4f) _go.transform.localScale = new Vector3(s, s, s);
        }

        static Mesh BuildMesh()
        {
            const int rings = 10, seg = 128;      // u = azimuth / 360° (from +z toward +x), v = elevation −1.25°…+10°
            var v = new Vector3[rings * (seg + 1)];
            var uv = new Vector2[v.Length];
            var col = new Color32[v.Length];
            var idx = new int[(rings - 1) * seg * 6];
            int n = 0;
            for (int r = 0; r < rings; r++)
            {
                float t = r / (rings - 1f), el = Mathf.Lerp(Bottom, Top, t) * Mathf.Deg2Rad, ce = Mathf.Cos(el), se = Mathf.Sin(el);
                for (int s = 0; s <= seg; s++)
                {
                    float az = s * Mathf.PI * 2f / seg;
                    v[n] = new Vector3(Mathf.Sin(az) * ce, se, Mathf.Cos(az) * ce) * Radius;
                    uv[n] = new Vector2(s / (float)seg, t);
                    col[n] = new Color32(255, 255, 255, 255);
                    n++;
                }
            }
            int k = 0;
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + 1, c = a + seg + 1, d = c + 1;
                    idx[k++] = a; idx[k++] = c; idx[k++] = b;
                    idx[k++] = b; idx[k++] = c; idx[k++] = d;
                }
            var m = new Mesh { name = "SEV_SkylineBand", hideFlags = HideFlags.DontSave };
            m.vertices = v; m.uv = uv; m.colors32 = col; m.triangles = idx;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * Radius * 2.2f);
            return m;
        }
    }
}
