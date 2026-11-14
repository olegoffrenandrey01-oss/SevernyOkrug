// Runtime NavMesh build from physics colliders (used when no baked NavMesh exists, e.g. the greybox map).
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Severny
{
    public static class RuntimeNavMesh
    {
        static NavMeshData _data;
        static NavMeshDataInstance _inst;
        public static bool Built { get; private set; }

        public static NavMeshBuildSettings Settings()
        {
            var s = NavMesh.GetSettingsByID(0);
            s.agentRadius = 0.35f; s.agentHeight = 1.8f; s.agentClimb = 0.45f; s.agentSlope = 45f;
            s.minRegionArea = 3f;
            return s;
        }

        /// <param name="notWalkable">optional sub-root whose colliders become obstacles with area "Not Walkable" (roofs, cars, walls)</param>
        public static IEnumerator Build(Transform root, Bounds bounds, Action<float> progress, Transform notWalkable = null)
        {
            Clear();
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            if (notWalkable != null)
                markups.Add(new NavMeshBuildMarkup { root = notWalkable, overrideArea = true, area = 1, applyToChildren = true });
            NavMeshBuilder.CollectSources(root, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            var s = Settings();
            _data = new NavMeshData(s.agentTypeID) { name = "SEV_RuntimeNavMesh" };
            _inst = NavMesh.AddNavMeshData(_data);
            bounds.Expand(new Vector3(4f, 20f, 4f));
            var op = NavMeshBuilder.UpdateNavMeshDataAsync(_data, s, sources, bounds);
            while (!op.isDone) { progress?.Invoke(op.progress); yield return null; }
            Built = true;
            Debug.Log("[Severny] Runtime NavMesh built from " + sources.Count + " sources, bounds " + bounds.size);
        }

        public static void Clear()
        {
            if (_inst.valid) _inst.Remove();
            _inst = default;
            _data = null;
            Built = false;
        }

        /// <summary>Random reachable point on the NavMesh inside bounds.</summary>
        public static bool RandomPoint(Bounds b, out Vector3 p, float inset = 5f, int tries = 30)
        {
            for (int i = 0; i < tries; i++)
            {
                var q = new Vector3(UnityEngine.Random.Range(b.min.x + inset, b.max.x - inset), b.center.y + b.extents.y,
                                    UnityEngine.Random.Range(b.min.z + inset, b.max.z - inset));
                if (Physics.Raycast(q, Vector3.down, out var hit, b.size.y + 50f, ~0, QueryTriggerInteraction.Ignore)) q = hit.point;
                if (NavMesh.SamplePosition(q, out var nh, 4f, NavMesh.AllAreas)) { p = nh.position; return true; }
            }
            p = b.center; return false;
        }

        public static bool Reachable(Vector3 a, Vector3 b, NavMeshPath path = null)
        {
            path = path ?? new NavMeshPath();
            return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }
    }
}
