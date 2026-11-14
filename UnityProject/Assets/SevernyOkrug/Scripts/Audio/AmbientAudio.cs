// v08 ambience: snow footsteps, blizzard wind, rain, crows, distant dogs, metal creaks, fire crackle.
// Recorded files in Resources/SevernySounds override the synthesized versions (amb_blizzard, amb_rain, amb_crow, amb_dog, amb_creak, amb_fire, step_snow01..04).
using UnityEngine;

namespace Severny
{
    public static partial class SFX
    {
        public static AudioClip WindStrong, RainLoop, Crow, Dog, Creak, FireLoop;
        public static AudioClip[] SnowSteps;
        /// <summary>Winter raids: footsteps crunch.</summary>
        public static bool SnowFootsteps;
        public static AudioClip[] CurSteps => SnowFootsteps && SnowSteps != null && SnowSteps.Length > 0 ? SnowSteps : Steps;

        static void InitAmbient()
        {
            WindStrong = Or(R("amb_blizzard"), BlizzardLoop("sev_blizzard", 12f));
            RainLoop = Or(R("amb_rain"), RainClip("sev_rain", 6f));
            Crow = Or(R("amb_crow"), CrowClip("sev_crow"));
            Dog = Or(R("amb_dog"), DogClip("sev_dog"));
            Creak = Or(R("amb_creak"), CreakClip("sev_creak"));
            FireLoop = Or(R("amb_fire"), FireClip("sev_fire", 4f));
            var rec = RA("step_snow", 4);
            if (rec.Length > 0) SnowSteps = rec;
            else { SnowSteps = new AudioClip[4]; for (int i = 0; i < 4; i++) SnowSteps[i] = SnowStep("sev_snowstep" + i, 0.9f + i * 0.07f); }
        }

        // ---- helpers
        struct Svf { public float low, band; }
        static float BandPass(ref Svf s, float x, float fc, float damp)
        {
            float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(fc, SR * 0.24f) / SR);
            s.low += f * s.band; float high = x - s.low - damp * s.band; s.band += f * high;
            return s.band;
        }
        static float[] LoopFade(float[] d, int n, int x)
        {
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = d[i];
            for (int i = 0; i < x; i++) { float k = (float)i / x; o[i] = d[i] * k + d[n + i] * (1f - k); }
            return o;
        }
        static float Rnd() => (float)_r.NextDouble();

        static AudioClip SnowStep(string name, float pitch)
        {
            var d = new float[(int)(SR * 0.24f)];
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, n = N(); lp += 0.35f * pitch * (n - lp);
                float env = Mathf.Min(1f, t * 120f) * Mathf.Exp(-t * 16f);
                d[i] = (n - lp) * env * 0.22f + Mathf.Sin(2f * Mathf.PI * 55f * pitch * t) * Mathf.Exp(-t * 30f) * 0.25f;
            }
            int k = 28 + _r.Next(14), len = (int)(SR * 0.004f);
            for (int c = 0; c < k; c++)
            {
                float t0 = 0.005f + Mathf.Pow(Rnd(), 1.6f) * 0.16f; int s0 = (int)(t0 * SR);
                float a = (0.3f + 0.7f * Rnd()) * Mathf.Exp(-t0 * 9f);
                for (int j = 0; j < len && s0 + j < d.Length; j++) d[s0 + j] += N() * Mathf.Exp(-(float)j / SR * 1400f) * a;
            }
            return Make(name, d, true, 0.55f);
        }

        static AudioClip BlizzardLoop(string name, float len)
        {
            int n = (int)(SR * len), x = (int)(SR * 0.8f);
            var d = new float[n + x];
            float lp = 0f, lp2 = 0f; var s1 = new Svf(); var s2 = new Svf();
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, w = N();
                lp += 0.012f * (w - lp); lp2 += 0.04f * (lp - lp2);
                float g = 0.55f + 0.28f * Mathf.Sin(t * 0.45f) + 0.14f * Mathf.Sin(t * 1.3f + 2f) + 0.08f * Mathf.Sin(t * 2.7f + 0.5f);
                float wh = BandPass(ref s1, w, 620f + 260f * Mathf.Sin(t * 0.31f), 0.06f) * 0.5f + BandPass(ref s2, w, 1250f + 300f * Mathf.Sin(t * 0.53f + 1f), 0.09f) * 0.25f;
                d[i] = lp2 * g * 6f + wh * g * g * 0.22f;
            }
            return Make(name, LoopFade(d, n, x), true, 0.6f);
        }

        static AudioClip RainClip(string name, float len)
        {
            int n = (int)(SR * len), x = (int)(SR * 0.5f);
            var d = new float[n + x];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float w = N(); lp += 0.25f * (w - lp); lp2 += 0.04f * (lp - lp2);
                d[i] = (w - lp) * 0.16f + (lp - lp2) * 0.35f;
            }
            int drops = (int)(len * 260f), dl = (int)(SR * 0.003f);
            for (int k = 0; k < drops; k++)
            {
                int s0 = _r.Next(d.Length - dl); float a = 0.05f + 0.3f * Rnd() * Rnd();
                for (int j = 0; j < dl; j++) d[s0 + j] += N() * Mathf.Exp(-(float)j / SR * 1600f) * a;
            }
            int drips = (int)(len * 5f), rl = (int)(SR * 0.05f);
            for (int k = 0; k < drips; k++)
            {
                int s0 = _r.Next(d.Length - rl); float hz = 1800f + 2200f * Rnd(), a = 0.08f + 0.1f * Rnd();
                for (int j = 0; j < rl; j++) { float t = (float)j / SR; d[s0 + j] += Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-t * 90f) * a; }
            }
            return Make(name, LoopFade(d, n, x), true, 0.55f);
        }

        static AudioClip CrowClip(string name)
        {
            var d = new float[(int)(SR * 1.35f)];
            int caws = 2 + _r.Next(2);
            for (int c = 0; c < caws; c++)
            {
                float start = c * 0.36f, dur = 0.22f + 0.06f * Rnd(), f0 = 380f + 80f * Rnd();
                var a = new Svf(); var b = new Svf(); float ph = 0f;
                int s0 = (int)(start * SR), sn = (int)(dur * SR);
                for (int j = 0; j < sn && s0 + j < d.Length; j++)
                {
                    float t = (float)j / SR, u = t / dur;
                    float f = f0 * (1.05f - 0.12f * u) * (1f + 0.02f * N());
                    ph += f / SR;
                    float saw = 2f * (ph - Mathf.Floor(ph)) - 1f;
                    float src = saw * (1f + 0.35f * N());
                    float y = BandPass(ref a, src, 1150f, 0.25f) + BandPass(ref b, src, 2300f, 0.3f) * 0.6f;
                    float env = Mathf.Min(1f, t * 90f) * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Min(1f, u)), 0.6f);
                    d[s0 + j] += y * env;
                }
            }
            Echo(d, 0.09f, 0.18f);
            return Make(name, d, true, 0.6f);
        }

        static AudioClip DogClip(string name)
        {
            var d = new float[(int)(SR * 1.5f)];
            int barks = 2 + _r.Next(2);
            for (int c = 0; c < barks; c++)
            {
                float start = c * (0.33f + 0.12f * Rnd()), dur = 0.14f + 0.05f * Rnd(), f0 = 250f + 70f * Rnd();
                var fm = new Svf(); float ph = 0f;
                int s0 = (int)(start * SR), sn = (int)(dur * SR);
                for (int j = 0; j < sn && s0 + j < d.Length; j++)
                {
                    float t = (float)j / SR, u = t / dur;
                    ph += f0 * (1.1f - 0.25f * u) / SR;
                    float v = 0f; for (int h = 1; h <= 6; h++) v += Mathf.Sin(2f * Mathf.PI * ph * h) / h;
                    float src = v + 0.6f * N() * (1f - u);
                    float env = Mathf.Min(1f, t * 200f) * Mathf.Exp(-t * 16f);
                    d[s0 + j] += BandPass(ref fm, src, 900f, 0.35f) * env + src * env * 0.15f;
                }
            }
            float lp = 0f; for (int i = 0; i < d.Length; i++) { lp += 0.18f * (d[i] - lp); d[i] = lp; }   // distance
            Echo(d, 0.14f, 0.3f); Echo(d, 0.31f, 0.15f);
            return Make(name, d, true, 0.55f);
        }

        static AudioClip CreakClip(string name)
        {
            float len = 1.4f;
            var d = new float[(int)(SR * len)];
            var a = new Svf(); var b = new Svf(); float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, u = t / len;
                lp += 0.3f * (N() - lp);
                float fric = lp * (0.6f + 0.4f * Mathf.Sin(t * 37f + Mathf.Sin(t * 5f) * 3f));
                float fc = Mathf.Lerp(180f, 320f, u) * (1f + 0.04f * Mathf.Sin(t * 23f));
                float y = BandPass(ref a, fric, fc, 0.015f) + BandPass(ref b, fric, fc * 2.03f, 0.02f) * 0.5f;
                d[i] = y * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 1.5f);
            }
            return Make(name, d, true, 0.5f);
        }

        static AudioClip FireClip(string name, float len)
        {
            int n = (int)(SR * len), x = (int)(SR * 0.4f);
            var d = new float[n + x];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++) { float w = N(); lp += 0.02f * (w - lp); lp2 += 0.1f * (lp - lp2); d[i] = lp2 * 2.5f + (w - lp) * 0.02f; }
            int pops = (int)(len * 28f);
            for (int k = 0; k < pops; k++)
            {
                int pl = (int)(SR * (0.002f + 0.004f * Rnd())), s0 = _r.Next(d.Length - pl);
                float a = Rnd() < 0.1f ? 0.9f : 0.15f + 0.35f * Rnd();
                for (int j = 0; j < pl; j++) d[s0 + j] += N() * Mathf.Exp(-(float)j / pl * 4f) * a;
            }
            return Make(name, LoopFade(d, n, x), true, 0.5f);
        }
    }
}
