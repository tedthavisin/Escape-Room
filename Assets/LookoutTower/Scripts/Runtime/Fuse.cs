using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// An Edison-base plug fuse the player can carry (one at a time, see <see cref="FuseHands"/>) and screw into a
    /// <see cref="FuseSocket"/>. Rating and colour band tell fuses apart; a blown fuse has a sooty window and never
    /// passes power.
    /// </summary>
    [DisallowMultipleComponent]
    public class Fuse : MonoBehaviour, IInteractable
    {
        [Tooltip("Rating in amps. A slot only works with exactly its rating; a higher one trips the main switch.")]
        public int amps = 15;
        [Tooltip("Colour of the band, as written on the wiring card (blue, red, yellow, green...).")]
        public string colourName = "blue";
        public bool blown;

        [Header("Look (filled in by the builder)")]
        [Tooltip("Renderers of the fuse model.")]
        public Renderer[] renderers;
        [Tooltip("Materials that turn sooty when the fuse is blown (the window and the link behind it).")]
        public Material windowMaterial, linkMaterial;
        public Material sootMaterial;

        Collider[] _colliders;
        Material[][] _original;

        /// <summary>The socket (panel slot, tin, crate) this fuse sits in, or null while held.</summary>
        public FuseSocket Socket { get; internal set; }
        public bool IsHeld => FuseHands.Held == this;

        public string Describe() => blown ? $"blown {amps} A fuse" : $"{amps} A fuse ({colourName})";

        public string Prompt
        {
            get
            {
                if (Socket != null) return Socket.Prompt;
                return FuseHands.Held == null || FuseHands.HasRoom ? $"Pick up {Describe()}" : $"Swap for {Describe()}";
            }
        }

        public void Interact()
        {
            // Aiming at a fuse that sits in a socket means "use the socket" (unscrew, swap).
            if (Socket != null) Socket.Interact();
            else FuseHands.PickUp(this);
        }

        void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>(true);
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>(true);
            _original = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++) _original[i] = renderers[i].sharedMaterials;
            ApplyLook();
        }

        public void SetBlown(bool value)
        {
            blown = value;
            ApplyLook();
        }

        void ApplyLook()
        {
            if (_original == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = (Material[])_original[i].Clone();
                if (blown && sootMaterial != null)
                    for (int k = 0; k < mats.Length; k++)
                        if (mats[k] != null && (mats[k] == windowMaterial || mats[k] == linkMaterial)) mats[k] = sootMaterial;
                renderers[i].sharedMaterials = mats;
            }
        }

        int _layer = -1;

        /// <summary>While held: on the Ignore Raycast layer, which the player's flashlight leaves out (it would blow
        /// the porcelain out to a white blob at arm's length).</summary>
        internal void SetHeld(bool held)
        {
            if (_layer < 0) _layer = gameObject.layer;
            int layer = held ? 2 : _layer;
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        internal void SetCollidersEnabled(bool on)
        {
            if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
            foreach (var c in _colliders) if (c != null) c.enabled = on;
        }

        void OnValidate()
        {
            if (Application.isPlaying) ApplyLook();
        }
    }
}
