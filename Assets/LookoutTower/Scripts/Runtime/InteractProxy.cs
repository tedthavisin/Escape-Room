using UnityEngine;

namespace LookoutTower
{
    /// <summary>Makes a collider (e.g. a crate's body) do whatever its target does (e.g. open the lid).</summary>
    [DisallowMultipleComponent]
    public class InteractProxy : MonoBehaviour, IInteractable, IProxyInteractable
    {
        [Tooltip("A component that implements IInteractable (HingedOpenable, FuseSocket...).")]
        public MonoBehaviour target;

        IInteractable Target => target as IInteractable;
        public string Prompt => Target != null ? Target.Prompt : "";

        public void Interact()
        {
            if (Target != null) Target.Interact();
        }
    }
}
