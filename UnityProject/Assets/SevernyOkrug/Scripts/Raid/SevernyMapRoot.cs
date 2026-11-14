// Put on the MAP_ROOT object of a map scene (menu Severny > 3 does it). Holds the baked NavMesh and play bounds.
// If this scene is played directly (no GameBootstrap), a quick raid starts automatically.
using UnityEngine;
using UnityEngine.AI;

namespace Severny
{
    public class SevernyMapRoot : MonoBehaviour
    {
        public string mapId = "severny";
        public NavMeshData navMesh;
        public bool hasPlayBounds;
        public Bounds playBounds;
        public bool quickRaidWhenPlayedDirectly = true;
        /// <summary>Build id of the city dress (Resources/SevernyDress) whose colliders are baked into navMesh (Severny > 4).</summary>
        public string dressVersion = "";
        NavMeshDataInstance _inst;

        void OnEnable()
        {
            if (navMesh != null && !_inst.valid) _inst = NavMesh.AddNavMeshData(navMesh);
        }

        void OnDisable() { if (_inst.valid) _inst.Remove(); _inst = default; }

        void Start()
        {
            if (quickRaidWhenPlayedDirectly && GameBootstrap.I == null)
            {
                var g = new GameObject("GameBootstrap (quick raid)");
                var b = g.AddComponent<GameBootstrap>();
                b.quickRaidMapId = mapId;
            }
        }

        public Bounds ComputeBounds()
        {
            if (hasPlayBounds) return playBounds;
            bool any = false; Bounds b = new Bounds(transform.position, Vector3.one);
            var backdrop = transform.Find("BACKDROP");
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (backdrop != null && r.transform.IsChildOf(backdrop)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
