namespace LookoutTower
{
    /// <summary>
    /// Anything the player can poke: doors, the trapdoor, windows, the fire finder sight.
    /// The desktop explorer calls Interact(); a VR interactor can call the same method later.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short verb phrase shown on screen, e.g. "Open trapdoor".</summary>
        string Prompt { get; }

        void Interact();
    }
}
