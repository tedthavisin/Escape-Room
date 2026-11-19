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
        [Tooltip("Allow the T teleport between where you started and the cab. Off in the escape room (it skips the hatch lock).")]
        public bool allowCabTeleport = true;

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
            // arrow keys look around too (trackpads, testing, remote control)
            Vector2 k = ArrowLook() * (Held(Key.LeftShift) ? 30f : 90f) * Time.deltaTime;
            transform.Rotate(0f, k.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - k.y, -85f, 85f);

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
            if (Pressed(Key.T) && allowCabTeleport && cabSpawn != null) TeleportToggle();
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

        static bool s_DebugAim;
        string _aimDebug;

        void UpdateFocus()
        {
            _focus = null;
            if (cam == null) return;
            var ray = InteractRay();   // eye-correct when an XR runtime mirrors one eye
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) s_DebugAim = !s_DebugAim;
#endif
            if (s_DebugAim)
            {
                // F3: show what the crosshair ray actually hits (for tracking down blocked interactions)
                var sbd = new System.Text.StringBuilder();
                var all = Physics.RaycastAll(ray, 4f, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(all, (x, y) => x.distance.CompareTo(y.distance));
                for (int i = 0; i < all.Length && i < 5; i++)
                {
                    var t = all[i].collider.transform;
                    string path = t.name;
                    for (var p = t.parent; p != null && path.Length < 90; p = p.parent) path = p.name + "/" + path;
                    sbd.AppendLine($"{all[i].distance:0.00} m  {path}  [{all[i].collider.GetType().Name}{(all[i].collider.isTrigger ? ", trigger" : "")}]");
                }
                _aimDebug = sbd.Length > 0 ? sbd.ToString() : "(ray hits nothing within 4 m)";
            }
            // forgiving aim: small things (fuses, tin lids) don't need a pixel-perfect hit; never through walls.
            // The picker also skips the player's own capsule (looking down at the keypad/crate used to hit it).
            _focus = InteractionPicker.Pick(ray, interactDistance, InteractionPicker.DefaultRadius, transform);
        }

        /// <summary>
        /// The ray through the crosshair as the player sees it. When an XR runtime is running (Meta XR Simulator,
        /// Quest Link) the camera renders in stereo and the Game view mirrors one eye, ~3 cm off the camera's centre,
        /// so a ray from the centre lands a button-width away from what's under the crosshair.
        /// </summary>
        Ray InteractRay()
        {
            if (cam.stereoEnabled)
            {
                var mode = UnityEngine.XR.XRSettings.gameViewRenderMode;
                if (mode == UnityEngine.XR.GameViewRenderMode.LeftEye) return EyeCentreRay(Camera.StereoscopicEye.Left);
                if (mode == UnityEngine.XR.GameViewRenderMode.RightEye) return EyeCentreRay(Camera.StereoscopicEye.Right);
            }
            return new Ray(cam.transform.position, cam.transform.forward);
        }

        /// <summary>
        /// Ray from an eye through the centre of that eye's image, from the matrices it renders with. Headset eye
        /// frustums are lopsided (wider on the outside), so the image centre is ~15° off the camera's forward.
        /// (Camera.ViewportPointToRay with an eye argument doesn't line up with the rendered image here.)
        /// </summary>
        Ray EyeCentreRay(Camera.StereoscopicEye eye)
        {
            Matrix4x4 eyeToWorld = cam.GetStereoViewMatrix(eye).inverse;
            Matrix4x4 proj = cam.GetStereoProjectionMatrix(eye);
            // view space looks down -Z; the image centre (NDC 0,0) is at x/-z = m02/m00, y/-z = m12/m11
            var dirView = new Vector3(proj.m02 / proj.m00, proj.m12 / proj.m11, -1f);
            return new Ray(eyeToWorld.GetColumn(3), eyeToWorld.MultiplyVector(dirView).normalized);
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
            if (s_DebugAim && !string.IsNullOrEmpty(_aimDebug))
                GUI.Label(new Rect(12f, 12f, 900f, 120f), _aimDebug, new GUIStyle(GUI.skin.label) { fontSize = 14 });
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

        static Vector2 ArrowLook()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            return new Vector2((kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f),
                               (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f));
        }

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

        static Vector2 ArrowLook() =>
            new Vector2((Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                        (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));

        static Vector2 MoveInput()
        {
            var v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }
#endif
    }
}
