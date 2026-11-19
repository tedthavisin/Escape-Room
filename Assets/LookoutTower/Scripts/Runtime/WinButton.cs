using UnityEngine;

namespace LookoutTower
{
    /// <summary>The win screen's Play Again button: point a controller at it and pull the trigger, or look at it and press E.</summary>
    public class WinButton : MonoBehaviour, IInteractable
    {
        public WinScreen screen;
        public string Prompt => "Play again";
        public void Interact() { if (screen != null) screen.PlayAgain(); }
    }
}
