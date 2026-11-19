using NavKeypad;
using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// Keeps the stair hatch (the trapdoor in the cab floor) locked until the right code goes into the keypad beside it.
    /// The code lives on the Keypad component (keypadCombo); the tower builder sets it and keeps it across rebuilds.
    /// </summary>
    public class HatchKeypadLock : MonoBehaviour
    {
        public Keypad keypad;
        public HingedOpenable hatch;
        [Tooltip("Swing the hatch open as soon as the code is accepted.")]
        public bool openOnUnlock = true;

        public UnityEvent onUnlocked = new UnityEvent();

        /// <summary>Builder bookkeeping: which default layout this keypad was placed with (a rebuild keeps a moved
        /// keypad only while the builder's default layout is unchanged).</summary>
        [HideInInspector] public int builderLayout;

        public bool IsUnlocked { get; private set; }

        void Awake()
        {
            if (hatch != null) hatch.Lock();
            if (keypad != null) keypad.OnAccessGranted.AddListener(Unlock);
            else Debug.LogWarning("[LookoutTower] HatchKeypadLock has no keypad; the hatch stays locked.", this);
        }

        void OnDestroy()
        {
            if (keypad != null) keypad.OnAccessGranted.RemoveListener(Unlock);
        }

        /// <summary>Unlocks (and by default opens) the hatch. Called by the keypad; also handy for hints or debugging.</summary>
        public void Unlock()
        {
            if (IsUnlocked) return;
            IsUnlocked = true;
            if (hatch != null)
            {
                hatch.Unlock();
                if (openOnUnlock) hatch.Open();
            }
            onUnlocked.Invoke();
        }
    }
}
