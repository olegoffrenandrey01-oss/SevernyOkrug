// Maps a collider to a body part. heightBased = single capsule (player CharacterController).
using UnityEngine;

namespace Severny
{
    public class Hitbox : MonoBehaviour
    {
        public Health owner;
        public BodyPart part = BodyPart.Thorax;
        public bool heightBased;
        public float height = 1.8f;

        public BodyPart Resolve(Vector3 worldPoint)
        {
            if (!heightBased) return part;
            Vector3 l = transform.InverseTransformPoint(worldPoint);
            float hf = l.y / Mathf.Max(0.5f, height);
            bool left = l.x < 0f;
            if (hf > 0.86f) return BodyPart.Head;
            if (hf > 0.52f && Mathf.Abs(l.x) > 0.21f) return left ? BodyPart.LeftArm : BodyPart.RightArm;
            if (hf > 0.64f) return BodyPart.Thorax;
            if (hf > 0.50f) return BodyPart.Stomach;
            return left ? BodyPart.LeftLeg : BodyPart.RightLeg;
        }
    }
}
