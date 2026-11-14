// F9 in a raid: map/NavMesh test. Fast test agents walk between random NavMesh points and log problems
// (stuck, partial paths, falling below the map) + a reachability survey from the player position.
// Log: <persistentDataPath>/severny_navtest.csv   (Windows: %USERPROFILE%/AppData/LocalLow/<company>/<product>/)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Severny
{
    public class NavTester : MonoBehaviour
    {
        static NavTester _i;
        public static bool Active => _i != null;
        public static string Summary { get; private set; } = "";
        public static string LogPath => Path.Combine(Application.persistentDataPath, "severny_navtest.csv");

        class Agent { public NavMeshAgent a; public Vector3 lastPos; public float lastProgress; public bool partialLogged; }

        Bounds _bounds;
        readonly List<Agent> _agents = new List<Agent>();
        int _reached, _stuck, _partial, _fell, _surveyOk, _surveyTotal, _surveyDone;
        float _startTime;
        readonly NavMeshPath _path = new NavMeshPath();
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public static void LogEvent(string ev, Vector3 p, string extra = "")
        {
            try
            {
                bool fresh = !File.Exists(LogPath);
                using (var w = new StreamWriter(LogPath, true))
                {
                    if (fresh) w.WriteLine("time,scene,event,x,y,z,extra");
                    w.WriteLine(string.Join(",", DateTime.Now.ToString("s", CI), SceneManager.GetActiveScene().name, ev,
                        p.x.ToString("F2", CI), p.y.ToString("F2", CI), p.z.ToString("F2", CI), (extra ?? "").Replace(',', ';')));
                }
            }
            catch (Exception e) { Debug.LogWarning("[NavTest] cannot write log: " + e.Message); }
            Debug.Log("[NavTest] " + ev + " at " + p + (string.IsNullOrEmpty(extra) ? "" : " " + extra));
        }

        public static void Toggle(Bounds b, Transform parent)
        {
            if (_i != null) { Stop(); Hud.Notify("NavTester остановлен. Лог: " + LogPath, 4f); return; }
            var go = new GameObject("NavTester");
            go.transform.SetParent(parent, false);
            _i = go.AddComponent<NavTester>();
            _i._bounds = b;
            Hud.Notify("NavTester запущен (F9 — стоп)", 3f);
        }

        public static void Stop()
        {
            if (_i == null) return;
            _i.WriteSummary();
            Destroy(_i.gameObject);
            _i = null;
        }

        void Start()
        {
            _startTime = Time.time;
            LogEvent("navtest_start", _bounds.center, "bounds " + _bounds.size.ToString("F0"));
            for (int k = 0; k < 4; k++)
            {
                if (!RuntimeNavMesh.RandomPoint(_bounds, out var p, 5f, 40)) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "NavTestAgent" + k;
                Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = GameAssets.Emissive(new Color(1f, 0.1f, 0.8f), 2f);
                go.transform.SetParent(transform, false);
                go.transform.position = p;
                var a = go.AddComponent<NavMeshAgent>();
                a.speed = 7f; a.angularSpeed = 720f; a.acceleration = 25f; a.radius = 0.35f; a.height = 1.8f; a.baseOffset = 1f;
                a.autoRepath = true; a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                var ag = new Agent { a = a, lastPos = p, lastProgress = Time.time };
                _agents.Add(ag);
                NewDestination(ag);
            }
            StartCoroutine(Survey());
        }

        void NewDestination(Agent ag)
        {
            if (!ag.a.isOnNavMesh) return;
            for (int i = 0; i < 10; i++)
            {
                if (!RuntimeNavMesh.RandomPoint(_bounds, out var d, 5f, 20)) continue;
                if (Vector3.Distance(d, ag.a.transform.position) < 25f) continue;
                ag.a.SetDestination(d); ag.partialLogged = false; ag.lastProgress = Time.time; ag.lastPos = ag.a.transform.position;
                return;
            }
        }

        void Update()
        {
            foreach (var ag in _agents)
            {
                var a = ag.a; if (a == null) continue;
                var pos = a.transform.position;
                if (pos.y < _bounds.min.y - 5f) { _fell++; LogEvent("agent_fell", pos); Respawn(ag); continue; }
                if (!a.isOnNavMesh) { Respawn(ag); continue; }
                if (a.pathPending) continue;
                if (a.pathStatus == NavMeshPathStatus.PathPartial && !ag.partialLogged) { _partial++; ag.partialLogged = true; LogEvent("path_partial", a.destination, "from " + pos.ToString("F0")); }
                if (a.pathStatus == NavMeshPathStatus.PathInvalid) { NewDestination(ag); continue; }
                if (!a.hasPath || a.remainingDistance < 1.5f) { if (a.hasPath || a.remainingDistance < 1.5f) _reached++; NewDestination(ag); continue; }
                if ((pos - ag.lastPos).sqrMagnitude > 1.5f * 1.5f) { ag.lastPos = pos; ag.lastProgress = Time.time; }
                else if (Time.time - ag.lastProgress > 8f) { _stuck++; LogEvent("agent_stuck", pos, "dest " + a.destination.ToString("F0")); Respawn(ag); }
            }
            Summary = "NavTest " + (Time.time - _startTime).ToString("F0") + "s: дошли " + _reached + ", застряли " + _stuck + ", частичный путь " + _partial + ", упали " + _fell +
                      (_surveyTotal > 0 ? " | достижимость " + (100f * _surveyOk / Mathf.Max(1, _surveyDone)).ToString("F0") + "% (" + _surveyDone + "/" + _surveyTotal + ")" : "");
        }

        void Respawn(Agent ag)
        {
            if (RuntimeNavMesh.RandomPoint(_bounds, out var p, 5f, 40)) ag.a.Warp(p);
            ag.lastPos = ag.a.transform.position; ag.lastProgress = Time.time;
            NewDestination(ag);
        }

        IEnumerator Survey()
        {
            Vector3 from = PlayerController.Local != null ? PlayerController.Local.transform.position : _bounds.center;
            if (!NavMesh.SamplePosition(from, out var h, 5f, NavMesh.AllAreas)) yield break;
            from = h.position;
            _surveyTotal = 200; int logged = 0;
            var rng = new System.Random(4242);
            for (int i = 0; i < _surveyTotal; i++)
            {
                var q = new Vector3(Mathf.Lerp(_bounds.min.x, _bounds.max.x, (float)rng.NextDouble()), _bounds.max.y, Mathf.Lerp(_bounds.min.z, _bounds.max.z, (float)rng.NextDouble()));
                if (Physics.Raycast(q + Vector3.up * 5f, Vector3.down, out var hit, _bounds.size.y + 60f, ~0, QueryTriggerInteraction.Ignore)) q = hit.point;
                if (NavMesh.SamplePosition(q, out var nh, 3f, NavMesh.AllAreas))
                {
                    _surveyDone++;
                    if (NavMesh.CalculatePath(from, nh.position, NavMesh.AllAreas, _path) && _path.status == NavMeshPathStatus.PathComplete) _surveyOk++;
                    else if (logged++ < 40) LogEvent("unreachable_point", nh.position);
                }
                if (i % 8 == 7) yield return null;
            }
            LogEvent("reachability", from, _surveyOk + "/" + _surveyDone + " navmesh samples reachable from player");
        }

        void WriteSummary() { if (!string.IsNullOrEmpty(Summary)) LogEvent("summary", Vector3.zero, Summary); }
    }
}
