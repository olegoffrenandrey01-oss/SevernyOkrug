// v12 «Графика и карта»: runtime access to Resources/SevernyGfx — CC0 HDRI skies (Poly Haven), FX textures and the particle
// materials created by menu Severny > 7 (also by Setup / Build). Asset materials are never modified at runtime (copies are
// made where per-raid values are needed). If a material asset is missing (Severny > 7 not run yet) an equivalent one is built
// from the shader: works in the editor; in a player the effect is silently skipped when the shader was stripped.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Severny
{
    public static class GfxRes
    {
        public const string Root = "SevernyGfx/";
        public const string ParticleUnlit = "Universal Render Pipeline/Particles/Unlit";
        public const string ParticleLit = "Universal Render Pipeline/Particles/Simple Lit";
        public enum Blend { Alpha = 0, Premultiply = 1, Additive = 2 }

        /// <summary>Particle material recipe (shared by the editor setup and the runtime fallback).</summary>
        public struct FXDef
        {
            public string name, tex, shader; public Blend blend;
            public float soft, camNear, camFar;  // soft-particle fade distance; camera fade near/far (0 = off)
            public Color color;                  // linear base colour (HDR allowed)
        }

        public static readonly FXDef[] FX =
        {
            new FXDef { name = "Smoke",  tex = "SEV_Smoke",  shader = ParticleUnlit, blend = Blend.Alpha,    soft = 2.5f, camNear = 1.0f, camFar = 5f,   color = Color.white },
            new FXDef { name = "Mist",   tex = "SEV_Mist",   shader = ParticleUnlit, blend = Blend.Alpha,    soft = 3.0f, camNear = 1.5f, camFar = 8f,   color = Color.white },
            new FXDef { name = "Glow",   tex = "SEV_Glow",   shader = ParticleUnlit, blend = Blend.Additive, soft = 0.8f, camNear = 0.6f, camFar = 2.5f, color = new Color(2.4f, 2.4f, 2.4f, 1f) },
            new FXDef { name = "Splash", tex = "SEV_Splash", shader = ParticleUnlit, blend = Blend.Alpha,    soft = 0f,   camNear = 0f,   camFar = 0f,   color = Color.white },
            new FXDef { name = "Crow",   tex = "SEV_Crow",   shader = ParticleUnlit, blend = Blend.Alpha,    soft = 0f,   camNear = 0f,   camFar = 0f,   color = Color.white },
            new FXDef { name = "Dust",   tex = "SEV_Glow",   shader = ParticleLit,   blend = Blend.Alpha,    soft = 0f,   camNear = 0.15f, camFar = 0.7f, color = Color.white },
        };

        static readonly Dictionary<string, Material> _fx = new Dictionary<string, Material>();
        static readonly HashSet<string> _missing = new HashSet<string>();

        public static Texture2D Tex(string name) => Resources.Load<Texture2D>(Root + "Textures/" + name);

        /// <summary>Shared FX material (asset from Resources/SevernyGfx/Materials or a runtime equivalent); null = effect off.</summary>
        public static Material FXMat(string name)
        {
            if (_fx.TryGetValue(name, out var m) && m != null) return m;
            if (_missing.Contains(name)) return null;
            m = Resources.Load<Material>(Root + "Materials/SEV_FX_" + name);
            if (m == null || m.shader == null || !m.shader.isSupported)
            {
                m = null;
                foreach (var d in FX)
                {
                    if (d.name != name) continue;
                    var sh = Shader.Find(d.shader);
                    var tex = Tex(d.tex);
                    if (sh != null && tex != null) { m = MakeParticleMaterial(sh, tex, d); m.name = "SEV_FX_" + name + " (runtime)"; m.hideFlags = HideFlags.DontSave; }
                    break;
                }
                if (m == null)
                {
                    _missing.Add(name);
                    Debug.Log("[Severny] Графика v12: нет материала SEV_FX_" + name + " (меню Severny > 7) — эффект выключен.");
                    return null;
                }
            }
            _fx[name] = m;
            return m;
        }

        /// <summary>URP particle material set up like the URP material inspector would (transparent, blend, soft particles, camera fade).</summary>
        public static Material MakeParticleMaterial(Shader sh, Texture tex, FXDef d)
        {
            var m = new Material(sh);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", d.color.gamma);
            SetF(m, "_Surface", 1f); SetF(m, "_Blend", (float)d.blend); SetF(m, "_ZWrite", 0f); SetF(m, "_Cull", 0f); SetF(m, "_AlphaClip", 0f);
            SetF(m, "_ColorMode", 0f); SetF(m, "_ReceiveShadows", 0f);
            BlendMode src = BlendMode.SrcAlpha, dst = BlendMode.OneMinusSrcAlpha, srcA = BlendMode.One, dstA = BlendMode.OneMinusSrcAlpha;
            if (d.blend == Blend.Premultiply) { src = BlendMode.One; }
            else if (d.blend == Blend.Additive) { dst = BlendMode.One; dstA = BlendMode.One; }
            SetF(m, "_SrcBlend", (float)src); SetF(m, "_DstBlend", (float)dst);
            SetF(m, "_SrcBlendAlpha", (float)srcA); SetF(m, "_DstBlendAlpha", (float)dstA);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (d.blend == Blend.Premultiply) m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); else m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            if (d.soft > 0f)
            {
                SetF(m, "_SoftParticlesEnabled", 1f); SetF(m, "_SoftParticlesNearFadeDistance", 0f); SetF(m, "_SoftParticlesFarFadeDistance", d.soft);
                m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / d.soft, 0f, 0f));
                m.EnableKeyword("_SOFTPARTICLES_ON");
            }
            else { SetF(m, "_SoftParticlesEnabled", 0f); m.DisableKeyword("_SOFTPARTICLES_ON"); }
            if (d.camFar > d.camNear)
            {
                SetF(m, "_CameraFadingEnabled", 1f); SetF(m, "_CameraNearFadeDistance", d.camNear); SetF(m, "_CameraFarFadeDistance", d.camFar);
                m.SetVector("_CameraFadeParams", new Vector4(d.camNear, 1f / (d.camFar - d.camNear), 0f, 0f));
                m.EnableKeyword("_FADING_ON");
            }
            else { SetF(m, "_CameraFadingEnabled", 0f); m.DisableKeyword("_FADING_ON"); }
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = false;
            return m;
        }

        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        /// <summary>Gamma-encoded colour whose linear value is (r, g, b) — for SetColor / Light.color in a Linear project.</summary>
        public static Color Lin(float r, float g, float b, float a = 1f)
        {
            var c = new Color(r, g, b, a);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;
        }

        public static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Deterministic 0..1 hash (LAN-safe: no UnityEngine.Random).</summary>
        public static float Hash01(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }

    /// <summary>Per-raid material swaps of v12 (lamp heads by day, wet surfaces in rain). Undone in reverse order by
    /// RestoreAll() — GameBootstrap.Cleanup calls it before Winter.Restore so stacked swaps unwind correctly.</summary>
    public static class GfxSwap
    {
        static readonly List<KeyValuePair<Renderer, Material[]>> _swapped = new List<KeyValuePair<Renderer, Material[]>>();
        static readonly List<Material> _owned = new List<Material>();

        public static void Swap(Renderer r, Material[] newMats)
        {
            if (r == null || newMats == null) return;
            _swapped.Add(new KeyValuePair<Renderer, Material[]>(r, r.sharedMaterials));
            r.sharedMaterials = newMats;
        }

        public static Material Own(Material m) { if (m != null) _owned.Add(m); return m; }

        public static void RestoreAll()
        {
            for (int i = _swapped.Count - 1; i >= 0; i--) if (_swapped[i].Key != null) _swapped[i].Key.sharedMaterials = _swapped[i].Value;
            _swapped.Clear();
            foreach (var m in _owned) if (m != null) Object.Destroy(m);
            _owned.Clear();
        }
    }
}
