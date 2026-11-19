using UnityEngine;

namespace LookoutTower
{
    /// <summary>The main switch handle on the fuse panel: interact to throw it.</summary>
    [DisallowMultipleComponent]
    public class FuseBoxSwitch : MonoBehaviour, IInteractable
    {
        public FuseBoxPanel panel;

        public string Prompt => panel == null ? "" : panel.MainOn ? "Switch the main OFF" : "Switch the main ON";

        public void Interact()
        {
            if (panel != null) panel.ToggleMain();
        }
    }
}
