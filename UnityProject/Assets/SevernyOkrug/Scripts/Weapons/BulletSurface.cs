using UnityEngine;
namespace Severny
{
    public enum BulletMaterial { Wood, Glass, ThinMetal, Solid }
    /// <summary>Explicitly opt a collider into penetration. Unmarked/concave compound map geometry stays protective.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Collider))]
    public class BulletSurface : MonoBehaviour
    {
        public BulletMaterial kind = BulletMaterial.Wood;
        [Tooltip("Минимальная толщина, м. Для сплошного объекта дополнительно измеряется путь внутри коллайдера.")]
        [Min(0.001f)] public float thickness = 0.025f;
        [Tooltip("Только полый BoxCollider: две стенки заданной толщины вместо объёма ящика.")]
        public bool hollowShell;
        [Min(1f)] public float resistancePerMeter = 180f;
        [Range(0.05f, 1f)] public float damageRetention = 0.7f;

        public bool TryCross(Collider col, RaycastHit hit, Vector3 dir, float budget, out float cost)
        {
            cost = 0f;
            if (kind == BulletMaterial.Solid || budget <= 0f) return false;
            // A back-face ray cannot reliably measure an open or concave mesh: fail closed.
            if (!(col is BoxCollider) && !(col is SphereCollider) && !(col is CapsuleCollider) && !(col is MeshCollider mc && mc.convex)) return false;
            float span = col.bounds.size.magnitude + 0.02f;
            if (!col.Raycast(new Ray(hit.point + dir * span, -dir), out var exit, span + 0.02f)) return false;
            float measured = Vector3.Dot(exit.point - hit.point, dir);
            if (measured <= 0.001f) return false;
            float incidence = Mathf.Max(0.05f, Mathf.Abs(Vector3.Dot(-dir, hit.normal)));
            cost = PenetrationRules.Cost(measured, thickness, resistancePerMeter, incidence, hollowShell && col is BoxCollider);
            return budget > cost;
        }
        [ContextMenu("Профиль: дерево")]
        void Wood() { kind = BulletMaterial.Wood; thickness = 0.025f; resistancePerMeter = 180f; damageRetention = 0.7f; }
        [ContextMenu("Профиль: стекло")]
        void Glass() { kind = BulletMaterial.Glass; thickness = 0.006f; resistancePerMeter = 120f; damageRetention = 0.85f; hollowShell = false; }
        [ContextMenu("Профиль: тонкий металл")]
        void Metal() { kind = BulletMaterial.ThinMetal; thickness = 0.003f; resistancePerMeter = 2500f; damageRetention = 0.55f; hollowShell = false; }
    }
}
