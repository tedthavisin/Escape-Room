using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LookoutTower
{
    /// <summary>
    /// Minimal first-person walker for checking the tower on a desktop before the VR rig exists.
    /// WASD move, mouse look, Shift sprint, C crouch, E interact, F flashlight,
    /// T teleport between the ground and the cab, Esc releases the cursor (click to re-capture).
    /// Ducks automatically under anything lower than standing height (hatch edges, a closed trapdoor).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DesktopExplorer : MonoBehaviour
    {
        public Camera cam;
        public Light flashlight;
        [Tooltip("Where T teleports you (the cab). Leave empty to disable.")]
        public Transform cabSpawn;

        public float walkSpeed = 2.0f;
        public float sprintSpeed = 4.2f;
        public float crouchSpeed = 1.1f;
        public float lookSensitivity = 0.1f;
        public float gravity = -15f;
        public float interactDistance = 2.6f;
        public float standHeight = 1.8f;
        public float crouchHeight = 1.15f;
        [Tooltip("Lowest the body will squash down to when ducking under something automatically.")]
        public float minHeight = 0.9f;
        public float eyeBelowTop = 0.15f;

        CharacterController _cc;
        float _pitch, _vy;
        bool _crouch;
        Vector3 _groundSpawn;
        Quaternion _groundRot;
        IInteractable _focus;
        string _toast;
        float _toastUntil;
        readonly RaycastHit[] _headHits = new RaycastHit[16];

        void Start()
        {
            _cc = GetComponent<CharacterController>();
            if (cam == null) cam = GetComponentInChildren<Camera>();
            _groundSpawn = transform.position;
            _groundRot = transform.rotation;
            Capture(true);
        }

        void Update()
        {
            if (Pressed(Key.Escape)) Capture(false);
            if (Cursor.lockState != CursorLockMode.Locked && ClickedLeft()) Capture(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = LookDelta() * lookSensitivity;
                transform.Rotate(0f, d.x, 0f, Space.Self);
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
            }

            if (Pressed(Key.C)) _crouch = !_crouch;
            // Duck under anything lower than the body, and never stand up into a ceiling.
            float targetH = _crouch ? crouchHeight : standHeight;
            float room = HeadRoom();
            if (room < targetH + 0.02f) targetH = Mathf.Max(minHeight, room - 0.02f);
            float rate = targetH < _cc.height ? 6f : 2.5f;     // duck quickly, straighten up gently
            _cc.height = Mathf.MoveTowards(_cc.height, targetH, rate * Time.deltaTime);
            _cc.center = new Vector3(0f, _cc.height * 0.5f, 0f);
            cam.transform.localPosition = new Vector3(0f, _cc.height - eyeBelowTop, 0f);
            cam.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);

            Vector2 mv = MoveInput();
            float spd = _crouch ? crouchSpeed : (Held(Key.LeftShift) ? sprintSpeed : walkSpeed);
            Vector3 move = (transform.right * mv.x + transform.forward * mv.y) * spd;
            if (_cc.isGrounded && _vy < 0f) _vy = -2f;
            _vy += gravity * Time.deltaTime;
            move.y = _vy;
            _cc.Move(move * Time.deltaTime);

            UpdateFocus();
            if (Pressed(Key.E) && _focus != null)
            {
                _focus.Interact();
                if (_focus is HingedOpenable h && h.locked) Toast($"The {h.displayName} won't budge.");
            }
            if (Pressed(Key.F) && flashlight != null) flashlight.enabled = !flashlight.enabled;
            if (Pressed(Key.T) && cabSpawn != null) TeleportToggle();
        }

        /// <summary>Free height above the feet: a body-wide sphere swept straight up from just above the floor.</summary>
        float HeadRoom()
        {
            float r = _cc.radius * 0.95f;
            float lift = _cc.radius + 0.05f;
            Vector3 origin = transform.position + Vector3.up * lift;
            int n = Physics.SphereCastNonAlloc(origin, r, Vector3.up, _headHits, standHeight, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = _headHits[i];
                // skip our own capsule and anything the sphere already touches at the start (the stair under us)
                if (h.collider == _cc || h.distance <= 0f) continue;
                best = Mathf.Min(best, lift + h.distance + r);
            }
            return best;
        }

        void UpdateFocus()
        {
            _focus = null;
            if (cam == null) return;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Physics.Raycast(ray, out var hit, interactDistance, ~0, QueryTriggerInteraction.Ignore))
                _focus = hit.collider.GetComponentInParent<IInteractable>();
        }

        void TeleportToggle()
        {
            bool inCab = Vector3.Distance(transform.position, cabSpawn.position) < 8f;
            Vector3 p = inCab ? _groundSpawn : cabSpawn.position;
            Quaternion r = inCab ? _groundRot : cabSpawn.rotation;
            _cc.enabled = false;
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, r.eulerAngles.y, 0f));
            _cc.enabled = true;
            _vy = 0f;
        }

        void Toast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.time + 2f;
        }

        void Capture(bool on)
        {
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        void OnGUI()
        {
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            var dot = _focus != null ? new Color(1f, 0.85f, 0.5f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
            var old = GUI.color;
            GUI.color = dot;
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = old;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            if (_focus != null)
                GUI.Label(new Rect(cx - 200f, cy + 18f, 400f, 24f), $"[E] {_focus.Prompt}", style);
            if (Time.time < _toastUntil)
                GUI.Label(new Rect(cx - 200f, cy + 42f, 400f, 24f), _toast, style);
            if (Cursor.lockState != CursorLockMode.Locked)
                GUI.Label(new Rect(cx - 220f, cy - 40f, 440f, 24f), "Click to look around  ·  WASD  E  F  C  T", style);
        }

        // ------------------------------------------------------------------ input (new Input System, legacy fallback)
        enum Key { Escape, C, E, F, T, LeftShift }

#if ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Key Map(Key k)
        {
            switch (k)
            {
                case Key.Escape: return UnityEngine.InputSystem.Key.Escape;
                case Key.C: return UnityEngine.InputSystem.Key.C;
                case Key.E: return UnityEngine.InputSystem.Key.E;
                case Key.F: return UnityEngine.InputSystem.Key.F;
                case Key.T: return UnityEngine.InputSystem.Key.T;
                default: return UnityEngine.InputSystem.Key.LeftShift;
            }
        }

        static bool Pressed(Key k) => Keyboard.current != null && Keyboard.current[Map(k)].wasPressedThisFrame;
        static bool Held(Key k) => Keyboard.current != null && Keyboard.current[Map(k)].isPressed;
        static bool ClickedLeft() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        static Vector2 LookDelta() => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

        static Vector2 MoveInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            var v = new Vector2((kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                                (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }
#else
        static KeyCode Map(Key k)
        {
            switch (k)
            {
                case Key.Escape: return KeyCode.Escape;
                case Key.C: return KeyCode.C;
                case Key.E: return KeyCode.E;
                case Key.F: return KeyCode.F;
                case Key.T: return KeyCode.T;
                default: return KeyCode.LeftShift;
            }
        }

        static bool Pressed(Key k) => Input.GetKeyDown(Map(k));
        static bool Held(Key k) => Input.GetKey(Map(k));
        static bool ClickedLeft() => Input.GetMouseButtonDown(0);
        static Vector2 LookDelta() => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;

        static Vector2 MoveInput()
        {
            var v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }
#endif
    }
}
