using UnityEngine;

namespace Severny
{
    /// <summary>Anything the player can use with F (loot containers, corpses, loose items).</summary>
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string Prompt { get; }
        public virtual bool CanInteract => true;
        public abstract void Interact(PlayerController p);
    }
}
