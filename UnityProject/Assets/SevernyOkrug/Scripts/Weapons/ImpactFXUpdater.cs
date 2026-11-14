using UnityEngine;

namespace Severny
{
    /// <summary>Drives ImpactFX puff animation.</summary>
    public class ImpactFXUpdater : MonoBehaviour
    {
        void Update() { ImpactFX.Tick(); }
    }
}
