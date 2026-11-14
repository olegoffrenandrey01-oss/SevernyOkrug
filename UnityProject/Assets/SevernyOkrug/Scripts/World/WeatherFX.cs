// Per-raid weather and ambience (lives under the RaidManager object, created by GameBootstrap):
//  * precipitation around the camera — snowfall or rain (lit particles: at night they show up in the torch beam),
//    killed by world collisions so it does not snow indoors; winter blizzards every few minutes (heavier snow, fog, wind);
//  * wind / blizzard / rain loops, muffled when there is a roof above the camera;
//  * random distant ambience: crows, dogs (mostly at night), creaks, far gunfire bursts;
//  * night: flashlights on the bots near the player, burning barrels next to bot posts;
//  * winter: breath vapour in front of the camera.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Severny
{
    public class WeatherFX : MonoBehaviour
    {
        public static WeatherFX I;
        public static WeatherFX Create(Transform parent)
        {
            var g = new GameObject("WeatherFX");
            g.transform.SetParent(parent, false);
            return g.AddComponent<WeatherFX>();
        }

        /// <summary>0..1, &gt;0 during a winter blizzard.</summary>
        public float Blizzard => _bliz;
        public Vector3 Wind => _windDir * _windSpeed;

        bool _winter, _night, _precip, _snow;
        ParticleSystem _ps, _breath;
        ParticleSystem.EmissionModule _em;
        float _baseRate, _baseFog, _bliz, _blizTarget, _nextBliz, _blizEnd, _windSpeed, _gustT, _indoor, _indoorTarget, _nextIndoor, _nextAmb, _nextBreath, _nextBotLights;
        Vector3 _windDir;
        AudioSource _windSrc, _rainSrc;
        AudioLowPassFilter _windLP, _rainLP;
        Transform _cam;
        static Texture2D _dot;
        readonly List<Light> _botLights = new List<Light>();
        readonly List<Fire> _fires = new List<Fire>();
        /// <summary>LAN: barrel positions (the host sends them, clients place the same ones).</summary>
        public readonly List<Vector3> FirePositions = new List<Vector3>();
        public bool FiresPlaced { get; private set; }
        readonly RaycastHit[] _hit = new RaycastHit[1];

        class Fire { public Transform root, flame1, flame2; public Light light; public AudioSource snd; public float seed; }

        void Awake() { I = this; }

        void Start()
        {
            _winter = RaidOptions.Winter; _night = RaidOptions.Night; _precip = RaidOptions.Precip;
            _snow = _winter;
            _baseFog = RenderSettings.fogDensity;
            float yaw = Random.Range(0f, 360f);
            _windDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            _windSpeed = _winter ? 1.6f : 1.0f;
            _nextBliz = Time.time + Random.Range(60f, 150f);
            _nextAmb = Time.time + Random.Range(5f, 12f);
            _nextBreath = Time.time + 2f;
            if (_precip) MakePrecip();
            if (_winter) MakeBreath();
            MakeAudio();
            if (_night || (_winter && RaidOptions.TimeOfDay == RaidOptions.TodEvening)) StartCoroutine(PlaceFires());
            else FiresPlaced = true;
            SFX.SnowFootsteps = _winter;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            RenderSettings.fogDensity = _baseFog > 0f ? _baseFog : RenderSettings.fogDensity;
            foreach (var l in _botLights) if (l != null) Destroy(l.gameObject);
        }

        // ---------------------------------------------------------------- particles
        static Texture2D Dot
        {
            get
            {
                if (_dot != null) return _dot;
                const int N = 32;
                _dot = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { name = "SEV_Dot", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        a = a * a * (3f - 2f * a);
                        px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                _dot.SetPixels32(px); _dot.Apply(true, true);
                return _dot;
            }
        }

        static Material ParticleMat(Color c, float smooth)
        {
            var m = GameAssets.NewLitMaterial();
            m.name = "SEV_Particle";
            GameAssets.MakeTransparent(m);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Dot);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            if (m.HasProperty("_ReceiveShadows")) m.SetFloat("_ReceiveShadows", 0f);
            m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            return m;
        }

        void MakePrecip()
        {
            var go = new GameObject(_snow ? "Snowfall" : "Rain");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.loop = true; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = _snow ? 16000 : 9000;
            main.startLifetime = _snow ? 9f : 1.35f;
            main.startSpeed = 0f;
            main.startSize = _snow ? new ParticleSystem.MinMaxCurve(0.02f, 0.05f) : new ParticleSystem.MinMaxCurve(0.008f, 0.014f);
            main.gravityModifier = 0f;
            _baseRate = _snow ? 650f : 2600f;
            _em = _ps.emission; _em.rateOverTime = _baseRate;
            var sh = _ps.shape; sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = _snow ? new Vector3(44f, 1f, 44f) : new Vector3(30f, 1f, 30f);
            sh.position = new Vector3(0f, _snow ? 10f : 12f, 0f);
            var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            SetWindVelocity();
            if (_snow)
            {
                var no = _ps.noise; no.enabled = true; no.strength = 0.4f; no.frequency = 0.3f; no.scrollSpeed = 0.2f;
                no.quality = ParticleSystemNoiseQuality.Low; no.damping = true;
            }
            var col = _ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Low; col.lifetimeLoss = 1f; col.bounce = 0f; col.radiusScale = 0.5f;
            col.enableDynamicColliders = false; col.maxCollisionShapes = 256; col.voxelSize = 0.6f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = _snow ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            if (!_snow) { r.velocityScale = 0.055f; r.lengthScale = 1f; }
            r.sharedMaterial = _snow ? ParticleMat(new Color(1f, 1f, 1f, 0.9f), 0.2f) : ParticleMat(new Color(0.78f, 0.82f, 0.9f, 0.32f), 0.8f);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.maxParticleSize = _snow ? 0.02f : 0.01f;
            r.minParticleSize = 0f;
            if (!_snow) MakeSplashes(go.transform);
            _ps.Play();
        }

        /// <summary>v12: ripples where raindrops hit the ground (collision sub-emitter, every ~3rd drop).</summary>
        void MakeSplashes(Transform parentT)
        {
            var mat = GfxRes.FXMat("Splash");
            if (mat == null || _ps == null) return;
            var go = new GameObject("RainSplash");
            go.transform.SetParent(parentT, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.24f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.startColor = new Color(0.82f, 0.86f, 0.92f, 0.5f);
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var sh = ps.shape; sh.enabled = false;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.6f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sortMode = ParticleSystemSortMode.None;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.maxParticleSize = 0.2f; r.minParticleSize = 0f;
            var sub = _ps.subEmitters; sub.enabled = true;
            sub.AddSubEmitter(ps, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing, 0.3f);
        }

        void SetWindVelocity()
        {
            if (_ps == null) return;
            var vel = _ps.velocityOverLifetime;
            Vector3 w = _windDir * _windSpeed * (_snow ? 1f : 0.35f);
            float fall = _snow ? Mathf.Lerp(1.15f, 2.2f, _bliz) : 11f;
            vel.x = new ParticleSystem.MinMaxCurve(w.x * 0.8f, w.x * 1.2f);
            vel.z = new ParticleSystem.MinMaxCurve(w.z * 0.8f, w.z * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-fall * 1.25f, -fall * 0.8f);
            var main = _ps.main;
            float life = _snow ? Mathf.Lerp(9f, 5.5f, _bliz) : 1.35f;
            main.startLifetime = life;
            var sh = _ps.shape;
            sh.position = new Vector3(-w.x * life * 0.5f, _snow ? 10f : 12f, -w.z * life * 0.5f);   // upwind, so the drift crosses the camera
        }

        void MakeBreath()
        {
            var go = new GameObject("Breath");
            go.transform.SetParent(transform, false);
            _breath = go.AddComponent<ParticleSystem>();
            _breath.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _breath.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.14f); main.maxParticles = 60;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = _breath.emission; em.rateOverTime = 0f;
            var sh = _breath.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = 0.02f;
            var sz = _breath.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.2f));
            var vel = _breath.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(0.08f, 0.18f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ParticleMat(new Color(0.95f, 0.97f, 1f, 0.07f), 0f);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            _breath.Play();
        }

        // ---------------------------------------------------------------- audio
        void MakeAudio()
        {
            if (_winter && SFX.WindStrong != null)
            {
                _windSrc = gameObject.AddComponent<AudioSource>();
                _windSrc.clip = SFX.WindStrong; _windSrc.loop = true; _windSrc.spatialBlend = 0f; _windSrc.volume = 0f;
                _windSrc.time = Random.Range(0f, SFX.WindStrong.length * 0.9f);
                _windSrc.Play();
                _windLP = gameObject.AddComponent<AudioLowPassFilter>(); _windLP.cutoffFrequency = 22000f;
            }
            if (_precip && !_snow && SFX.RainLoop != null)
            {
                var g = new GameObject("RainAudio"); g.transform.SetParent(transform, false);
                _rainSrc = g.AddComponent<AudioSource>();
                _rainSrc.clip = SFX.RainLoop; _rainSrc.loop = true; _rainSrc.spatialBlend = 0f; _rainSrc.volume = 0f; _rainSrc.Play();
                _rainLP = g.AddComponent<AudioLowPassFilter>(); _rainLP.cutoffFrequency = 22000f;
            }
        }

        // ---------------------------------------------------------------- update
        void Update()
        {
            var camObj = Camera.main;
            if (camObj == null) return;
            _cam = camObj.transform;
            float dt = Time.deltaTime;
            Vector3 cp = _cam.position;

            // blizzards (winter snowfall only)
            if (_winter && _precip)
            {
                if (_blizTarget <= 0f && Time.time > _nextBliz) { _blizTarget = 1f; _blizEnd = Time.time + Random.Range(60f, 100f); Hud.Notify("Начинается метель", 2.5f); }
                if (_blizTarget > 0f && Time.time > _blizEnd) { _blizTarget = 0f; _nextBliz = Time.time + Random.Range(180f, 360f); }
            }
            float prevB = _bliz;
            _bliz = Mathf.MoveTowards(_bliz, _blizTarget, dt / 20f);
            // wind with gusts
            _gustT += dt * 0.15f;
            float gust = Mathf.PerlinNoise(_gustT, 3.7f);
            _windSpeed = (_winter ? 1.4f : 0.9f) + gust * (_winter ? 2.2f : 1.4f) + _bliz * 6f;
            if (_ps != null)
            {
                _ps.transform.position = new Vector3(cp.x, cp.y, cp.z);
                _em.rateOverTime = _baseRate * (1f + 1.6f * _bliz);
                if (Mathf.Abs(prevB - _bliz) > 0.0001f || Time.frameCount % 30 == 0) SetWindVelocity();
            }
            if (_winter && _precip) RenderSettings.fogDensity = _baseFog * (1f + 1.5f * _bliz);

            // indoor check (roof above the camera) for muffling
            if (Time.time > _nextIndoor)
            {
                _nextIndoor = Time.time + 0.3f;
                _indoorTarget = Physics.RaycastNonAlloc(cp + Vector3.up * 0.2f, Vector3.up, _hit, 30f, ~0, QueryTriggerInteraction.Ignore) > 0 ? 1f : 0f;
            }
            _indoor = Mathf.MoveTowards(_indoor, _indoorTarget, dt * 2f);
            float muff = 1f - 0.7f * _indoor, cutoff = Mathf.Lerp(22000f, 1000f, _indoor);
            if (_windSrc != null)
            {
                _windSrc.volume = Mathf.Clamp01((0.12f + gust * 0.25f + _bliz * 0.55f) * muff);
                _windSrc.pitch = 0.92f + gust * 0.12f + _bliz * 0.08f;
                if (_windLP != null) _windLP.cutoffFrequency = cutoff;
            }
            if (_rainSrc != null)
            {
                _rainSrc.volume = Mathf.Clamp01(0.42f * Mathf.Lerp(1f, 0.55f, _indoor));
                if (_rainLP != null) _rainLP.cutoffFrequency = Mathf.Lerp(22000f, 2200f, _indoor);
            }

            // breath vapour
            if (_breath != null && Time.time > _nextBreath)
            {
                var pc = PlayerController.Local;
                bool sprint = pc != null && pc.IsSprinting;
                _nextBreath = Time.time + (sprint ? Random.Range(1.3f, 1.8f) : Random.Range(3f, 4.2f));
                if (pc != null && !pc.health.IsDead)
                {
                    _breath.transform.SetPositionAndRotation(cp + _cam.forward * 0.28f - _cam.up * 0.1f, Quaternion.LookRotation(_cam.forward + Vector3.down * 0.25f));
                    _breath.Emit(sprint ? 7 : 5);
                }
            }

            // ambience one-shots
            if (Time.time > _nextAmb) { _nextAmb = Time.time + Random.Range(8f, 22f); Ambient(cp); }

            // night: bot flashlights + fire flicker
            if (_night && Time.time > _nextBotLights) { _nextBotLights = Time.time + 0.5f; UpdateBotLights(cp); }
            for (int i = 0; i < _fires.Count; i++) FlickerFire(_fires[i], cp);
        }

        void Ambient(Vector3 cp)
        {
            if (RaidOptions.Tutorial) return;
            float r = Random.value;
            Vector3 Around(float min, float max, float h)
            {
                var d = Random.insideUnitCircle.normalized * Random.Range(min, max);
                return cp + new Vector3(d.x, h, d.y);
            }
            if (r < 0.3f && !_night && !(_precip && !_snow) && SFX.Crow != null)
            {
                var p = Around(35f, 110f, Random.Range(8f, 20f));
                int n = Random.Range(1, 4);
                StartCoroutine(Repeat(SFX.Crow, p, n, 0.35f, 0.7f, Random.Range(0.35f, 0.6f), 5f, 260f));
            }
            else if (r < (_night ? 0.6f : 0.42f) && SFX.Dog != null)
            {
                var p = Around(90f, 230f, 1f);
                StartCoroutine(Repeat(SFX.Dog, p, Random.Range(2, 6), 0.5f, 0.9f, Random.Range(0.35f, 0.55f), 8f, 380f));
            }
            else if (r < 0.7f && SFX.Creak != null)
            {
                var p = Around(12f, 45f, Random.Range(1f, 8f));
                SFX.PlayAt(SFX.Creak, p, Random.Range(0.25f, 0.45f) * (_winter ? 1.3f : 1f), 3f, 70f, 0.15f);
            }
            else
            {
                // far firefight (somebody else's raid)
                var p = Around(260f, 620f, 3f);
                var clip = SFX.Or(SFX.ShotFarRifle, SFX.DistantShot);
                if (clip != null) StartCoroutine(Burst(clip, p));
            }
        }

        IEnumerator Repeat(AudioClip c, Vector3 p, int n, float gapMin, float gapMax, float vol, float minD, float maxD)
        {
            for (int i = 0; i < n; i++)
            {
                SFX.PlayAt(c, p, vol, minD, maxD, 0.12f);
                yield return new WaitForSeconds(Random.Range(gapMin, gapMax));
            }
        }

        IEnumerator Burst(AudioClip c, Vector3 p)
        {
            int volleys = Random.Range(1, 4);
            for (int v = 0; v < volleys; v++)
            {
                int shots = Random.Range(1, 7);
                for (int i = 0; i < shots; i++)
                {
                    SFX.PlayAt(c, p, Random.Range(0.35f, 0.55f), 20f, 1200f, 0.08f);
                    yield return new WaitForSeconds(Random.Range(0.08f, 0.16f));
                }
                yield return new WaitForSeconds(Random.Range(0.6f, 2.5f));
                p += Random.insideUnitSphere * 15f;
            }
        }

        // ---------------------------------------------------------------- night: bot flashlights
        void UpdateBotLights(Vector3 cp)
        {
            var near = new List<Transform>();      // torch mounts of living bots (LAN client: the host's bots)
            foreach (var b in BotController.All)
                if (b != null && !b.IsDead && (b.transform.position - cp).sqrMagnitude < 140f * 140f) near.Add(b.TorchMount);
            foreach (var b in NetBotProxy.All.Values)
                if (b != null && !b.IsDead && (b.transform.position - cp).sqrMagnitude < 140f * 140f) near.Add(b.TorchMount);
            near.Sort((a, b) => (a.position - cp).sqrMagnitude.CompareTo((b.position - cp).sqrMagnitude));
            int n = Mathf.Min(near.Count, 6);
            while (_botLights.Count < n) _botLights.Add(MakeBotLight());
            for (int i = 0; i < _botLights.Count; i++)
            {
                var l = _botLights[i];
                if (l == null) continue;
                if (i < n)
                {
                    var t = near[i];
                    if (l.transform.parent != t) { l.transform.SetParent(t, false); l.transform.localPosition = new Vector3(0.12f, -0.04f, 0.3f); l.transform.localRotation = Quaternion.identity; }
                    l.gameObject.SetActive(true);
                }
                else { l.transform.SetParent(transform, false); l.gameObject.SetActive(false); }
            }
        }

        Light MakeBotLight()
        {
            var g = new GameObject("BotTorch");
            g.transform.SetParent(transform, false);
            var l = g.AddComponent<Light>();
            l.type = LightType.Spot; l.range = 32f; l.spotAngle = 55f; l.innerSpotAngle = 22f;
            l.intensity = 9f; l.color = new Color(1f, 0.93f, 0.8f); l.shadows = LightShadows.None;
            var lens = Prim.Sphere(g.transform, new Vector3(0f, 0f, -0.02f), 0.045f, GameAssets.Emissive(new Color(1f, 0.92f, 0.78f), 6f), false, "Lens");
            lens.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return l;
        }

        // ---------------------------------------------------------------- night: burning barrels
        IEnumerator PlaceFires()
        {
            yield return null;   // bots are spawned in the same frame as this component
            var spots = new List<Vector3>();
            if (NetSession.IsClient && NetSession.I.WorldFires != null) spots.AddRange(NetSession.I.WorldFires);
            else
            {
                var bots = new List<BotController>(BotController.All);
                int want = Mathf.Min(8, Mathf.Max(3, bots.Count / 2));
                var used = new List<Vector3>();
                for (int i = 0; i < bots.Count && spots.Count < want; i++)
                {
                    var b = bots[(i * 7 + 3) % bots.Count];
                    if (b == null) continue;
                    Vector3 bp = b.transform.position;
                    bool close = false;
                    foreach (var u in used) if ((u - bp).sqrMagnitude < 30f * 30f) { close = true; break; }
                    if (close) continue;
                    var off = Random.insideUnitCircle.normalized * Random.Range(2f, 3.5f);
                    if (!NavMesh.SamplePosition(bp + new Vector3(off.x, 0f, off.y), out var h, 2f, NavMesh.AllAreas)) continue;
                    used.Add(h.position);
                    spots.Add(h.position);
                }
            }
            FirePositions.Clear(); FirePositions.AddRange(spots);
            var rust = GameAssets.Lit(new Color(0.24f, 0.13f, 0.08f), 0.2f, 0.4f);
            var fireA = GameAssets.Emissive(new Color(1f, 0.45f, 0.12f), 9f);
            var fireB = GameAssets.Emissive(new Color(1f, 0.75f, 0.3f), 7f);
            foreach (var spot in spots)
            {
                var root = new GameObject("FireBarrel").transform;
                root.SetParent(transform, false);
                root.position = spot;
                var barrel = Prim.Cyl(root, new Vector3(0f, 0.45f, 0f), 0.58f, 0.9f, rust, true, null, "Barrel");
                var obs = root.gameObject.AddComponent<NavMeshObstacle>(); obs.shape = NavMeshObstacleShape.Capsule; obs.radius = 0.32f; obs.height = 1f; obs.center = new Vector3(0f, 0.5f, 0f); obs.carving = true;
                var f = new Fire { root = root, seed = Random.value * 100f };
                f.flame1 = Prim.Sphere(root, new Vector3(0f, 0.98f, 0f), 0.4f, fireA, false, "Flame").transform;
                f.flame2 = Prim.Sphere(root, new Vector3(0.05f, 1.1f, 0.03f), 0.26f, fireB, false, "Flame2").transform;
                foreach (var rr in root.GetComponentsInChildren<Renderer>()) if (rr.name.StartsWith("Flame")) rr.shadowCastingMode = ShadowCastingMode.Off;
                var lg = new GameObject("FireLight"); lg.transform.SetParent(root, false); lg.transform.localPosition = new Vector3(0f, 1.4f, 0f);
                f.light = lg.AddComponent<Light>(); f.light.type = LightType.Point; f.light.range = 10f; f.light.intensity = 3f;
                f.light.color = new Color(1f, 0.55f, 0.22f); f.light.shadows = LightShadows.None;
                if (SFX.FireLoop != null)
                {
                    f.snd = root.gameObject.AddComponent<AudioSource>();
                    f.snd.clip = SFX.FireLoop; f.snd.loop = true; f.snd.spatialBlend = 1f; f.snd.rolloffMode = AudioRolloffMode.Linear;
                    f.snd.minDistance = 1.5f; f.snd.maxDistance = 20f; f.snd.volume = 0.55f; f.snd.dopplerLevel = 0f;
                    f.snd.time = Random.Range(0f, SFX.FireLoop.length * 0.9f); f.snd.Play();
                }
                _fires.Add(f);
            }
            FiresPlaced = true;
            if (_fires.Count > 0) Debug.Log("[Severny] Fire barrels: " + _fires.Count);
        }

        void FlickerFire(Fire f, Vector3 cp)
        {
            if (f.root == null) return;
            float d2 = (f.root.position - cp).sqrMagnitude;
            bool on = d2 < 110f * 110f;
            if (f.light.enabled != on) f.light.enabled = on;
            if (!on) return;
            float t = Time.time * 7f + f.seed;
            float n1 = Mathf.PerlinNoise(t, f.seed), n2 = Mathf.PerlinNoise(f.seed, t * 1.3f);
            f.light.intensity = 2.2f + 1.8f * n1;
            f.flame1.localScale = new Vector3(0.4f + 0.08f * n2, 0.42f + 0.25f * n1, 0.4f + 0.08f * n1);
            f.flame2.localScale = new Vector3(0.24f, 0.28f + 0.3f * n2, 0.24f);
            f.flame2.localPosition = new Vector3(0.05f * (n1 - 0.5f), 1.08f + 0.12f * n2, 0.05f * (n2 - 0.5f));
        }
    }
}
