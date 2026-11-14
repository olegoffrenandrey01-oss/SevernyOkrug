// Sounds: the recorded pack Resources/SevernySounds/SW_*.wav is used when present; every clip that is missing
// falls back to a sound synthesized at startup (so the game also works without audio assets). Pooled one-shot player.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public static partial class SFX
    {
        const int SR = 22050;
        public static AudioClip RifleShot, PistolShot, DistantShot, DryClick, MagOut, MagIn, BoltRack, Hit, Impact, Crack, Wind, Breath, UIClick, Pickup, Beep, Rustle, Thud;
        public static AudioClip[] Steps;
        // recorded pack (null / empty when the file is missing -> callers use the synthesized fallback)
        public static AudioClip Shot545, Shot762, Shot12g, ShotSMG, ShotDMR, ShotSuppressed, ShotFarRifle, ShotFarPistol,
            FireMode, WeaponSwitch, Pump, ShellIn, Jump, Land, Death, LootOpen, Search, Torch,
            MedBandage, MedKit, MedInjector, MedPills, Drink, Eat, ItemPick, ItemDrop, Buy;
        public static AudioClip[] FleshHits, MetalHits, ConcreteHits, ArmorHits, Flybys, Hurts;
        public static int RecordedCount;
        public const string SoundFolder = "SevernySounds";
        static bool _init;
        static AudioSource[] _pool;
        static float[] _busy;
        static GameObject _host;
        static System.Random _r = new System.Random(1234);

        static float N() => (float)(_r.NextDouble() * 2.0 - 1.0);

        public static void Init()
        {
            if (_init) return;
            _init = true;
            RifleShot = Shot("sev_rifle", 0.9f, 1.0f, 16f, 110f);
            PistolShot = Shot("sev_pistol", 0.6f, 0.75f, 24f, 160f);
            DistantShot = Distant("sev_distant");
            DryClick = Clicks("sev_dry", new[] { 0f, 0.028f }, 0.12f, 0.5f);
            MagOut = Metal("sev_magout", new[] { 0f, 0.09f }, 0.35f);
            MagIn = Metal("sev_magin", new[] { 0f, 0.05f, 0.07f }, 0.3f);
            BoltRack = Metal("sev_bolt", new[] { 0f, 0.16f }, 0.45f, true);
            Hit = Thump("sev_hit", 90f, 0.25f, 0.6f);
            Thud = Thump("sev_thud", 60f, 0.4f, 0.9f);
            Impact = ImpactClip("sev_impact");
            Crack = CrackClip("sev_crack");
            Wind = WindLoop("sev_wind", 8f);
            Breath = BreathLoop("sev_breath");
            UIClick = Tone("sev_ui", 1500f, 0.035f, 0.35f);
            Beep = Tone("sev_beep", 880f, 0.12f, 0.4f);
            Pickup = RustleClip("sev_pickup", 0.25f, 0.5f);
            Rustle = RustleClip("sev_rustle", 1.2f, 0.35f);
            Steps = new AudioClip[4];
            for (int i = 0; i < 4; i++) Steps[i] = Step("sev_step" + i, 0.85f + i * 0.08f);
            LoadRecorded();
            InitAmbient();
            _host = new GameObject("SEV_SFX");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _pool = new AudioSource[28];
            _busy = new float[_pool.Length];
            for (int i = 0; i < _pool.Length; i++)
            {
                var g = new GameObject("src" + i);
                g.transform.SetParent(_host.transform);
                var a = g.AddComponent<AudioSource>();
                a.playOnAwake = false;
                a.rolloffMode = AudioRolloffMode.Logarithmic;
                a.dopplerLevel = 0f;
                _pool[i] = a;
            }
        }

        static AudioSource Get()
        {
            if (!_init) Init();
            if (_host == null) { _init = false; Init(); }
            int best = 0; float oldest = float.MaxValue;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i] == null) continue;
                if (!_pool[i].isPlaying) { best = i; break; }
                if (_busy[i] < oldest) { oldest = _busy[i]; best = i; }
            }
            _busy[best] = Time.unscaledTime;
            return _pool[best];
        }

        public static AudioSource PlayAt(AudioClip c, Vector3 pos, float vol = 1f, float minDist = 2f, float maxDist = 400f, float pitchVar = 0.05f)
        {
            if (c == null) return null;
            var a = Get(); if (a == null) return null;
            a.transform.position = pos;
            a.spatialBlend = 1f; a.minDistance = minDist; a.maxDistance = maxDist;
            a.pitch = 1f + (float)(_r.NextDouble() * 2 - 1) * pitchVar;
            a.volume = vol; a.clip = c; a.Play();
            return a;
        }

        public static AudioSource Play2D(AudioClip c, float vol = 1f, float pitchVar = 0.03f)
        {
            if (c == null) return null;
            var a = Get(); if (a == null) return null;
            a.spatialBlend = 0f;
            a.pitch = 1f + (float)(_r.NextDouble() * 2 - 1) * pitchVar;
            a.volume = vol; a.clip = c; a.Play();
            return a;
        }

        /// <summary>Stops a sound started with PlayAt/Play2D if that pooled source still plays the same clip.</summary>
        public static void Stop(AudioSource a, AudioClip c)
        {
            if (a != null && c != null && a.clip == c && a.isPlaying) a.Stop();
        }

        // ---------- recorded pack ----------
        static AudioClip R(string n)
        {
            var c = Resources.Load<AudioClip>(SoundFolder + "/" + n);
            if (c != null)
            {
                RecordedCount++;
                // decode now (menu load) instead of on the first shot — avoids a hitch if "Preload Audio Data" is off
                if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
            }
            return c;
        }
        static AudioClip[] RA(string prefix, int n)
        {
            var l = new List<AudioClip>();
            for (int i = 1; i <= n; i++) { var c = R(prefix + i.ToString("00")); if (c != null) l.Add(c); }
            return l.ToArray();
        }
        public static AudioClip Or(AudioClip a, AudioClip b) => a != null ? a : b;
        /// <summary>Random clip of a variation set, or the fallback when the set is empty.</summary>
        public static AudioClip Pick(AudioClip[] set, AudioClip fallback) => set != null && set.Length > 0 ? set[_r.Next(set.Length)] : fallback;

        static void LoadRecorded()
        {
            RecordedCount = 0;
            Shot545 = R("SW_Shot_545"); Shot762 = R("SW_Shot_762"); Shot12g = R("SW_Shot_12g"); ShotSMG = R("SW_Shot_SMG");
            ShotDMR = R("SW_Shot_DMR"); ShotSuppressed = R("SW_Shot_Suppressed");
            RifleShot = Or(Shot545, RifleShot); PistolShot = Or(R("SW_Shot_Pistol"), PistolShot);
            ShotFarRifle = R("SW_ShotFar_Rifle"); ShotFarPistol = R("SW_ShotFar_Pistol");
            DistantShot = Or(ShotFarRifle, DistantShot);
            DryClick = Or(R("SW_Dry"), DryClick); MagOut = Or(R("SW_MagOut"), MagOut); MagIn = Or(R("SW_MagIn"), MagIn); BoltRack = Or(R("SW_BoltRack"), BoltRack);
            FireMode = Or(R("SW_FireMode"), DryClick); WeaponSwitch = Or(R("SW_WeaponSwitch"), Rustle); Pump = R("SW_Pump"); ShellIn = R("SW_ShellIn");
            FleshHits = RA("SW_Impact_Flesh_", 3); MetalHits = RA("SW_Impact_Metal_", 3); ConcreteHits = RA("SW_Impact_Concrete_", 3);
            ArmorHits = RA("SW_ArmorHit_", 2); Flybys = RA("SW_Flyby_", 3); Hurts = RA("SW_Hurt_", 3);
            var steps = RA("SW_Step_Concrete_", 4); if (steps.Length > 0) Steps = steps;
            Jump = Or(R("SW_Jump"), Steps[0]); Land = Or(R("SW_Land"), Steps[0]); Death = R("SW_Death");
            Breath = Or(R("SW_Breath"), Breath);
            LootOpen = Or(R("SW_Loot_Open"), Rustle); Search = R("SW_Search"); Torch = Or(R("SW_Torch"), UIClick);
            MedBandage = Or(R("SW_Med_Bandage"), Rustle); MedKit = Or(R("SW_Med_Kit"), Rustle); MedInjector = Or(R("SW_Med_Injector"), Rustle);
            MedPills = Or(R("SW_Med_Pills"), Rustle); Drink = R("SW_Drink"); Eat = R("SW_Eat");
            ItemPick = Or(R("SW_UI_ItemPick"), Pickup); ItemDrop = Or(R("SW_UI_ItemDrop"), Pickup); Buy = Or(R("SW_UI_Buy"), Pickup);
            Pickup = ItemPick;
            Debug.Log("[Severny] SFX: recorded sound pack clips loaded: " + RecordedCount + " (Resources/" + SoundFolder + ")");
        }

        /// <summary>Close-range shot of a weapon (by caliber / id).</summary>
        public static AudioClip ShotFor(ItemDef w)
        {
            if (w == null) return RifleShot;
            string id = (w.id ?? "").ToLowerInvariant(), cal = (w.caliber ?? "").ToLowerInvariant();
            if (id.Contains("supp") || id.EndsWith("_sd")) return Or(ShotSuppressed, w.isPistol ? PistolShot : RifleShot);
            if (cal.StartsWith("12")) return Or(Shot12g, RifleShot);
            if (cal.Contains("x54") || id.Contains("dmr") || id.Contains("svd")) return Or(ShotDMR, Or(Shot762, RifleShot));
            if (cal.StartsWith("7.62") || cal.StartsWith("7,62")) return Or(Shot762, RifleShot);
            if (w.isPistol) return PistolShot;
            if (cal.StartsWith("9x") || cal.StartsWith("4.6") || cal.StartsWith("5.7")) return Or(ShotSMG, PistolShot);
            return RifleShot;
        }

        /// <summary>Distant shot (bots far from the listener).</summary>
        public static AudioClip DistantFor(ItemDef w)
        {
            bool small = w != null && (w.isPistol || (w.caliber ?? "").StartsWith("9x"));
            return small ? Or(ShotFarPistol, DistantShot) : DistantShot;
        }

        public static AudioClip MedFor(MedKind k)
        {
            switch (k)
            {
                case MedKind.Bandage: return MedBandage;
                case MedKind.Painkiller: return MedPills;
                case MedKind.Medkit:
                case MedKind.Surgery: return MedKit;
            }
            return Rustle;
        }

        static readonly Dictionary<Collider, bool> _metal = new Dictionary<Collider, bool>();
        static readonly string[] MetalKeys = { "steel", "metal", "chrome", "rust", "corr", "car", "garagedoor", "chainlink", "diamond", "rack",
            "barrel", "trashcan", "propane", "jerrycan", "_rim", "utility", "hydrant", "razor", "grate", "cont_", "dumpster", "skip", "kiosk",
            "bus", "van_", "sedan", "police", "burnt", "fence", "railing", "lamp", "shelf", "gondola", "register", "stove", "hatch", "cabin", "pipe" };
        static bool HasMetalKey(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            n = n.ToLowerInvariant();
            if (n.Contains("carton") || n.Contains("cardboard")) return false;
            foreach (var k in MetalKeys) if (n.Contains(k)) return true;
            return false;
        }
        /// <summary>Bullet impact on a surface: metal for cars, containers, shelves, fences..., concrete otherwise.</summary>
        public static AudioClip ImpactFor(Collider c)
        {
            bool metal = false;
            if (c != null)
            {
                if (!_metal.TryGetValue(c, out metal))
                {
                    var r = c.GetComponent<Renderer>();
                    if (r != null) foreach (var m in r.sharedMaterials) if (m != null && HasMetalKey(m.name)) { metal = true; break; }
                    if (!metal) metal = HasMetalKey(c.gameObject.name);
                    if (_metal.Count > 20000) _metal.Clear();
                    _metal[c] = metal;
                }
            }
            return Pick(metal ? MetalHits : ConcreteHits, Impact);
        }

        // ---------- synthesis ----------
        static AudioClip Make(string name, float[] d, bool normalize = true, float peak = 0.95f)
        {
            if (normalize)
            {
                float m = 1e-6f; foreach (var v in d) m = Mathf.Max(m, Mathf.Abs(v));
                float k = peak / m; for (int i = 0; i < d.Length; i++) d[i] *= k;
            }
            var c = AudioClip.Create(name, d.Length, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        static void Echo(float[] d, float delay, float gain)
        {
            int off = (int)(delay * SR);
            for (int i = d.Length - 1; i >= off; i--) d[i] += d[i - off] * gain;
        }

        static AudioClip Shot(string name, float len, float body, float decay, float thumpHz)
        {
            var d = new float[(int)(SR * len)];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, n = N();
                lp += 0.35f * (n - lp);
                lp2 += 0.04f * (n - lp2);
                float crack = n * Mathf.Exp(-t * 90f);
                float bodyN = lp * Mathf.Exp(-t * decay) * body;
                float f = Mathf.Lerp(thumpHz, 45f, Mathf.Clamp01(t * 6f));
                float thump = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 22f) * 0.9f;
                float tail = lp2 * Mathf.Exp(-t * 3.5f) * 0.9f;
                d[i] = crack * 0.8f + bodyN + thump + tail;
            }
            Echo(d, 0.083f, 0.25f); Echo(d, 0.19f, 0.16f); Echo(d, 0.37f, 0.1f);
            return Make(name, d);
        }

        static AudioClip Distant(string name)
        {
            var d = new float[(int)(SR * 1.6f)];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, n = N();
                lp += 0.06f * (n - lp); lp2 += 0.06f * (lp - lp2);
                float env = (1f - Mathf.Exp(-t * 300f)) * Mathf.Exp(-t * 4.5f);
                float thump = Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t * 9f) * 0.25f;
                d[i] = lp2 * env * 3f + thump;
            }
            Echo(d, 0.21f, 0.4f); Echo(d, 0.47f, 0.25f); Echo(d, 0.8f, 0.15f);
            return Make(name, d, true, 0.8f);
        }

        static AudioClip Clicks(string name, float[] at, float len, float peak)
        {
            var d = new float[(int)(SR * len)];
            foreach (var t0 in at)
            {
                int s = (int)(t0 * SR);
                for (int i = 0; i < (int)(0.012f * SR) && s + i < d.Length; i++)
                {
                    float t = (float)i / SR;
                    d[s + i] += N() * Mathf.Exp(-t * 600f) + Mathf.Sin(2 * Mathf.PI * 3200f * t) * Mathf.Exp(-t * 300f) * 0.5f;
                }
            }
            return Make(name, d, true, peak);
        }

        static AudioClip Metal(string name, float[] at, float len, bool slide = false)
        {
            var d = new float[(int)(SR * len)];
            float[] fr = { 2150f, 3720f, 5050f, 1230f };
            foreach (var t0 in at)
            {
                int s = (int)(t0 * SR);
                for (int i = 0; i < (int)(0.08f * SR) && s + i < d.Length; i++)
                {
                    float t = (float)i / SR, v = N() * Mathf.Exp(-t * 400f) * 0.6f;
                    for (int k = 0; k < fr.Length; k++) v += Mathf.Sin(2 * Mathf.PI * fr[k] * (1f + 0.03f * k) * t) * Mathf.Exp(-t * (55f + 20f * k)) * 0.35f;
                    d[s + i] += v;
                }
            }
            if (slide)
            {
                float lp = 0f;
                for (int i = (int)(0.02f * SR); i < (int)(0.15f * SR) && i < d.Length; i++) { lp += 0.3f * (N() - lp); d[i] += lp * 0.25f; }
            }
            return Make(name, d, true, 0.6f);
        }

        static AudioClip Thump(string name, float hz, float len, float peak)
        {
            var d = new float[(int)(SR * len)];
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR; lp += 0.15f * (N() - lp);
                d[i] = Mathf.Sin(2 * Mathf.PI * hz * t) * Mathf.Exp(-t * 28f) + lp * Mathf.Exp(-t * 40f) * 0.8f;
            }
            return Make(name, d, true, peak);
        }

        static AudioClip ImpactClip(string name)
        {
            var d = new float[(int)(SR * 0.3f)];
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, n = N(); lp += 0.5f * (n - lp);
                d[i] = (n - lp) * Mathf.Exp(-t * 90f) + Mathf.Sin(2 * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 60f) * 0.2f + lp * Mathf.Exp(-t * 25f) * 0.3f;
            }
            return Make(name, d, true, 0.7f);
        }

        static AudioClip CrackClip(string name)
        {
            var d = new float[(int)(SR * 0.25f)];
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float nwave = t < 0.0012f ? (1f - 2f * t / 0.0012f) : 0f;
                d[i] = nwave + N() * Mathf.Exp(-t * 160f) * 0.6f + N() * Mathf.Exp(-t * 18f) * 0.05f;
            }
            Echo(d, 0.06f, 0.2f);
            return Make(name, d, true, 0.8f);
        }

        static AudioClip WindLoop(string name, float len)
        {
            int n = (int)(SR * len), x = (int)(SR * 0.5f);
            var d = new float[n + x];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                lp += 0.02f * (N() - lp); lp2 += 0.05f * (lp - lp2);
                float mod = 0.6f + 0.25f * Mathf.Sin(t * 0.7f) + 0.15f * Mathf.Sin(t * 1.9f + 1f);
                d[i] = lp2 * mod;
            }
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = d[i];
            for (int i = 0; i < x; i++) { float k = (float)i / x; o[i] = d[i] * k + d[n + i] * (1f - k); }
            return Make(name, o, true, 0.5f);
        }

        static AudioClip BreathLoop(string name)
        {
            var d = new float[(int)(SR * 2.4f)];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR; lp += 0.25f * (N() - lp); lp2 += 0.02f * (lp - lp2);
                float ph = (t % 1.2f) / 1.2f;
                float env = ph < 0.45f ? Mathf.Sin(ph / 0.45f * Mathf.PI) : Mathf.Sin((ph - 0.45f) / 0.55f * Mathf.PI) * 0.8f;
                d[i] = (lp - lp2) * env * env;
            }
            return Make(name, d, true, 0.4f);
        }

        static AudioClip Tone(string name, float hz, float len, float peak)
        {
            var d = new float[(int)(SR * len)];
            for (int i = 0; i < d.Length; i++) { float t = (float)i / SR; d[i] = Mathf.Sin(2 * Mathf.PI * hz * t) * Mathf.Min(1f, t * 400f) * Mathf.Exp(-t * 18f); }
            return Make(name, d, true, peak);
        }

        static AudioClip RustleClip(string name, float len, float peak)
        {
            var d = new float[(int)(SR * len)];
            float lp = 0f, env = 0f, target = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                if (i % 600 == 0) target = (float)_r.NextDouble();
                env += 0.002f * (target - env);
                float t = (float)i / SR; lp += 0.4f * (N() - lp);
                float fade = Mathf.Min(1f, t * 20f) * Mathf.Min(1f, (len - t) * 10f);
                d[i] = (N() - lp) * env * fade;
            }
            return Make(name, d, true, peak);
        }

        static AudioClip Step(string name, float pitch)
        {
            var d = new float[(int)(SR * 0.16f)];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, n = N(); lp += 0.3f * pitch * (n - lp); lp2 += 0.03f * (lp - lp2);
                float env = Mathf.Min(1f, t * 300f) * Mathf.Exp(-t * 38f);
                d[i] = (lp - lp2) * env + Mathf.Sin(2 * Mathf.PI * 70f * pitch * t) * Mathf.Exp(-t * 45f) * 0.4f;
            }
            return Make(name, d, true, 0.5f);
        }
    }

    public static class NoiseSystem
    {
        public struct Noise { public Vector3 pos; public float radius; public GameObject source; public bool gunshot; }
        public static event Action<Noise> OnNoise;
        public static void Emit(Vector3 pos, float radius, GameObject source, bool gunshot = false)
            => OnNoise?.Invoke(new Noise { pos = pos, radius = radius, source = source, gunshot = gunshot });
        public static void ClearListeners() { OnNoise = null; }
    }
}
