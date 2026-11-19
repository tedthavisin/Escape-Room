using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LookoutTower
{
    /// <summary>Keeps the held fuse in front of the current camera and shows desktop hints. Created on demand.</summary>
    [AddComponentMenu("")]
    public class FuseHandsRunner : MonoBehaviour
    {
        static FuseHandsRunner s_Instance;
        string _hint;
        float _hintUntil;

        public static void Ensure()
        {
            if (s_Instance != null) return;
            var go = new GameObject("[FuseHands]");
            s_Instance = go.AddComponent<FuseHandsRunner>();
        }

#if ENABLE_INPUT_SYSTEM
        InputAction _cycle;
#endif

        void OnEnable()
        {
            PuzzleHints.Shown += OnHint;
#if ENABLE_INPUT_SYSTEM
            // Q on the keyboard, A or X on a Quest controller: bring the next pocketed fuse into hand
            _cycle = new InputAction("CycleFuse", InputActionType.Button, "<Keyboard>/q");
            _cycle.AddBinding("<XRController>{RightHand}/primaryButton");
            _cycle.AddBinding("<XRController>{LeftHand}/primaryButton");
            _cycle.Enable();
#endif
        }

        void OnDisable()
        {
            PuzzleHints.Shown -= OnHint;
#if ENABLE_INPUT_SYSTEM
            _cycle?.Dispose();
            _cycle = null;
#endif
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (_cycle != null && _cycle.WasPressedThisFrame()) FuseHands.Cycle();
#endif
        }

        void OnHint(string msg)
        {
            _hint = msg;
            _hintUntil = Time.time + 3f;
        }

        Camera _litCam;

        void LateUpdate()
        {
            var f = FuseHands.Held;
            var cam = Camera.main;
            if (f == null || cam == null) return;
            var t = cam.transform;
            if (_litCam != cam)
            {
                // the player's own lights (flashlight) skip the held fuse's layer
                _litCam = cam;
                foreach (var l in t.root.GetComponentsInChildren<Light>(true)) l.cullingMask &= ~(1 << 2);
            }
            f.transform.SetPositionAndRotation(t.TransformPoint(FuseHands.HoldOffset), t.rotation * Quaternion.Euler(FuseHands.HoldEuler));
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            float cx = Screen.width * 0.5f;
            if (FuseHands.Held != null)
                GUI.Label(new Rect(cx - 450f, Screen.height - 60f, 900f, 24f), FuseHands.Describe(), style);
            if (Time.time < _hintUntil && !string.IsNullOrEmpty(_hint))
                GUI.Label(new Rect(cx - 300f, Screen.height * 0.5f + 66f, 600f, 24f), _hint, style);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Instance = null;
            FuseHands.Forget();
        }
    }
}
