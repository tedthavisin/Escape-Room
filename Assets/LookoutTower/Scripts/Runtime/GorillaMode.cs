using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LookoutTower
{
    /// <summary>
    /// Gorilla-Tag-style movement, unlocked by the <see cref="GorillaStatue"/> easter egg. Added to the player when the
    /// statue is taken; G (desktop) or clicking the left thumbstick (VR) turns it off and on again.
    ///
    /// VR: no stick walking. Hands that touch the world stick to it, and the body moves the opposite way to them, so
    /// you push off the floor and walls with your arms; let go mid-push and you fly with the speed you had.
    /// Desktop (no arms to swing): left / right mouse button slaps the surface under the crosshair with that hand,
    /// throwing you towards where you slapped and away from the surface (floor: a forward hop; high on a wall: up
    /// it). Holding the button keeps that hand gripping, which nearly cancels gravity, so you can climb hand over hand.
    ///
    /// A leash keeps the body over the cab and catwalk while you're up there, so the gorilla can't vault the railing
    /// and skip the hatch puzzle. It never touches the puzzles: it only moves the player, and only while active.
    /// </summary>
    [DefaultExecutionOrder(50)]   // after DesktopExplorer, so this frame's mouse look is already on the camera
    public class GorillaMode : MonoBehaviour
    {
        public bool active;

        [Header("Feel")]
        public float gravity = -9.81f;
        public float maxSpeed = 6.5f;
        public float groundFriction = 6f;
        public float airDrag = 0.1f;

        [Header("Desktop slaps")]
        public float armReach = 1.7f;
        public float slapAlong = 3.0f;
        public float slapFloorPush = 3.4f;
        public float slapWallPush = 0.6f;
        public float slapCooldown = 0.12f;
        [Tooltip("Gravity while a hand grips (button held on a surface).")]
        public float gripGravityScale = 0.15f;
        public float gripDamping = 2f;

        [Header("VR arms")]
        public float handRadius = 0.07f;
        [Tooltip("A stuck hand lets go once it pulls this far away from the surface (m per frame) or this far from where it stuck.")]
        public float releaseDistance = 0.02f, maxStretch = 0.5f;
        public float flingMultiplier = 1.15f;

        [Header("Leash")]
        public bool leash = true;
        public Vector3 leashCentre;
        public float leashHalfExtent = 4.3f;
        public float leashMinY = 29f;

        public Material handMaterial;

        CharacterController _cc;
        DesktopExplorer _explorer;
        XRInteractBridge _xr;
        bool _vrMode;
        Vector3 _vel;
        InputAction _toggle;
        string _hint;
        float _hintUntil;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        // desktop hands
        readonly bool[] _planted = new bool[2];
        readonly Vector3[] _plant = new Vector3[2];
        readonly float[] _nextSlap = new float[2];
        readonly float[] _punch = new float[2];
        readonly Transform[] _fist = new Transform[2];
        bool _wasLocked;

        // VR hands
        readonly bool[] _vrInit = new bool[2];
        readonly bool[] _touch = new bool[2];
        readonly Vector3[] _last = new Vector3[2];
        readonly Vector3[] _normal = new Vector3[2];
        bool _wasTouching;
        readonly List<Behaviour> _pausedLocomotion = new List<Behaviour>();

        /// <summary>Finds the player in use (the VR rig if it's running, else the desktop explorer), adds this to it and switches it on.</summary>
        public static GorillaMode Grant(GorillaStatue statue)
        {
            GameObject host = null;
            var xr = FindAnyObjectByType<XRInteractBridge>();
            if (xr != null && xr.isActiveAndEnabled && xr.rigRoot != null)
            {
                var rigCc = xr.rigRoot.GetComponentInChildren<CharacterController>();
                host = rigCc != null ? rigCc.gameObject : xr.rigRoot.gameObject;
            }
            else
            {
                var ex = FindAnyObjectByType<DesktopExplorer>();
                if (ex != null) host = ex.gameObject;
            }
            if (host == null)
            {
                Debug.LogWarning("[LookoutTower] Gorilla statue: no player found to give gorilla mode to.");
                return null;
            }
            var mode = host.GetComponent<GorillaMode>();
            if (mode == null) mode = host.AddComponent<GorillaMode>();
            if (statue != null)
            {
                mode.leashCentre = statue.leashCentre;
                mode.leashHalfExtent = statue.leashHalfExtent;
                mode.leashMinY = statue.leashMinY;
                if (statue.handMaterial != null) mode.handMaterial = statue.handMaterial;
            }
            mode.SetActive(true);
            mode.Hint(mode._vrMode
                ? "GORILLA MODE: push off the world with your hands. Click the left stick to toggle."
                : "GORILLA MODE: left/right click slaps a surface, hold to grip. G to toggle.", 7f);
            Debug.Log("[LookoutTower] Gorilla statue collected: gorilla mode on.");
            return mode;
        }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _explorer = GetComponent<DesktopExplorer>();
            _xr = FindAnyObjectByType<XRInteractBridge>();
            _vrMode = _explorer == null && _xr != null && _xr.isActiveAndEnabled;
        }

        void OnEnable()
        {
            _toggle = new InputAction("GorillaToggle", InputActionType.Button, "<Keyboard>/g");
            _toggle.AddBinding("<XRController>{LeftHand}/{Primary2DAxisClick}");
            _toggle.Enable();
        }

        void OnDisable()
        {
            _toggle?.Dispose();
            _toggle = null;
            if (active) SetActive(false);
        }

        public void SetActive(bool on)
        {
            active = on;
            _vel = Vector3.zero;
            for (int i = 0; i < 2; i++) { _planted[i] = false; _touch[i] = false; _vrInit[i] = false; }
            _wasTouching = false;
            if (_explorer != null) _explorer.externalMovement = on;
            if (_vrMode) PauseRigLocomotion(on);
            else ShowFists(on);
        }

        void Update()
        {
            if (_toggle != null && _toggle.WasPressedThisFrame())
            {
                SetActive(!active);
                Hint(active ? "Gorilla mode on" : "Gorilla mode off", 2f);
            }
            if (!active || _cc == null || !_cc.enabled) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_vrMode) UpdateVr(dt);
            else UpdateDesktop(dt);
        }

        // ------------------------------------------------------------------ desktop
        void UpdateDesktop(float dt)
        {
            var cam = _explorer != null ? _explorer.cam : null;
            if (cam == null) return;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool canUse = locked && _wasLocked;   // the click that captures the cursor isn't a slap
            _wasLocked = locked;
            Vector3 eye = cam.transform.position;

            for (int i = 0; i < 2; i++)
            {
                var btn = Mouse.current == null ? null : (i == 0 ? Mouse.current.leftButton : Mouse.current.rightButton);
                bool down = canUse && btn != null && btn.wasPressedThisFrame;
                bool held = canUse && btn != null && btn.isPressed;
                if (down && Time.time >= _nextSlap[i]) Slap(i, cam);
                if (_planted[i] && (!held || Vector3.Distance(eye, _plant[i]) > armReach * 1.3f)) _planted[i] = false;
                _punch[i] = Mathf.MoveTowards(_punch[i], 0f, dt * 6f);
            }

            bool gripping = _planted[0] || _planted[1];
            _vel.y += gravity * (gripping ? gripGravityScale : 1f) * dt;
            if (gripping) _vel *= Mathf.Exp(-gripDamping * dt);
            else if (_cc.isGrounded)
            {
                float k = Mathf.Exp(-groundFriction * dt);
                _vel.x *= k;
                _vel.z *= k;
            }
            else _vel *= Mathf.Exp(-airDrag * dt);
            MoveBody(_vel * dt);
            UpdateFists(cam, dt);
        }

        void Slap(int i, Camera cam)
        {
            _nextSlap[i] = Time.time + slapCooldown;
            _punch[i] = 1f;
            Vector3 eye = cam.transform.position;
            if (!RaycastWorld(new Ray(eye, cam.transform.forward), armReach, out var hit)) return;   // slapped thin air
            _planted[i] = true;
            _plant[i] = hit.point + hit.normal * 0.04f;
            Vector3 n = hit.normal;
            Vector3 along = Vector3.ProjectOnPlane(hit.point - eye, n);
            along = along.sqrMagnitude > 0.0025f ? along.normalized : Vector3.zero;
            Vector3 push = along * slapAlong + n * Mathf.Lerp(slapWallPush, slapFloorPush, Mathf.Clamp01(n.y));
            // a fresh push wins over whatever we were doing against it (a slap stops a fall)
            Vector3 pd = push.normalized;
            float against = Vector3.Dot(_vel, pd);
            if (against < 0f) _vel -= against * pd;
            _vel += push;
            if (_vel.magnitude > maxSpeed) _vel = _vel.normalized * maxSpeed;
        }

        void ShowFists(bool on)
        {
            var cam = _explorer != null ? _explorer.cam : null;
            for (int i = 0; i < 2; i++)
            {
                if (_fist[i] == null && on && cam != null)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = i == 0 ? "Gorilla Fist L" : "Gorilla Fist R";
                    Destroy(go.GetComponent<Collider>());
                    var r = go.GetComponent<MeshRenderer>();
                    if (handMaterial != null) r.sharedMaterial = handMaterial;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.transform.SetParent(cam.transform, false);
                    go.transform.localScale = new Vector3(0.1f, 0.09f, 0.12f);
                    go.transform.localPosition = FistRest(i);
                    _fist[i] = go.transform;
                }
                if (_fist[i] != null) _fist[i].gameObject.SetActive(on);
            }
        }

        static Vector3 FistRest(int i) => new Vector3(i == 0 ? -0.24f : 0.24f, -0.28f, 0.45f);

        void UpdateFists(Camera cam, float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                if (_fist[i] == null) continue;
                Vector3 local;
                if (_planted[i]) local = cam.transform.InverseTransformPoint(_plant[i]);
                else local = Vector3.Lerp(FistRest(i), new Vector3(i == 0 ? -0.1f : 0.1f, -0.12f, 1.0f), _punch[i]);
                _fist[i].localPosition = Vector3.Lerp(_fist[i].localPosition, local, 1f - Mathf.Exp(-30f * dt));
            }
        }

        // ------------------------------------------------------------------ VR
        void UpdateVr(float dt)
        {
            Vector3 correction = Vector3.zero;
            int touching = 0;
            for (int i = 0; i < 2; i++)
            {
                var hand = i == 0 ? _xr.leftController : _xr.rightController;
                if (hand == null) continue;
                Vector3 target = hand.position;
                if (!_vrInit[i]) { _last[i] = target; _vrInit[i] = true; _touch[i] = false; }

                Vector3 final = target;
                bool touch = false;
                Vector3 d = target - _last[i];
                // a stuck hand stays put until it's pulled away from the surface (or stretched too far)
                if (_touch[i] && Vector3.Dot(d, _normal[i]) < releaseDistance && d.magnitude < maxStretch)
                {
                    final = _last[i];
                    touch = true;
                }
                else if (d.sqrMagnitude > 1e-8f && SphereCastWorld(_last[i], handRadius, d.normalized, d.magnitude, out var hit))
                {
                    final = _last[i] + d.normalized * hit.distance;
                    _normal[i] = hit.normal;
                    touch = true;
                }
                _touch[i] = touch;
                _last[i] = final;
                if (touch) { correction += final - target; touching++; }
            }

            if (touching > 0)
            {
                Vector3 move = correction / touching;
                if (move.magnitude > 0.3f) move = move.normalized * 0.3f;
                _vel = Vector3.Lerp(_vel, move / dt, 0.35f);   // smoothed, so letting go flings with the push's speed
                _wasTouching = true;
                MoveBody(move);
            }
            else
            {
                if (_wasTouching)
                {
                    _vel *= flingMultiplier;
                    if (_vel.magnitude > maxSpeed) _vel = _vel.normalized * maxSpeed;
                    _wasTouching = false;
                }
                _vel.y += gravity * dt;
                if (_cc.isGrounded)
                {
                    float k = Mathf.Exp(-groundFriction * dt);
                    _vel.x *= k;
                    _vel.z *= k;
                }
                else _vel *= Mathf.Exp(-airDrag * dt);
                MoveBody(_vel * dt);
            }
        }

        /// <summary>Switches the rig's own stick locomotion (XRI move/teleport providers) off while gorilla mode is on; snap/smooth turn stays.</summary>
        void PauseRigLocomotion(bool pause)
        {
            if (pause)
            {
                _pausedLocomotion.Clear();
                var root = _xr != null && _xr.rigRoot != null ? _xr.rigRoot : transform;
                foreach (var b in root.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null || !b.enabled || !IsLocomotionProvider(b.GetType()) || b.GetType().Name.Contains("Turn")) continue;
                    b.enabled = false;
                    _pausedLocomotion.Add(b);
                }
            }
            else
            {
                foreach (var b in _pausedLocomotion) if (b != null) b.enabled = true;
                _pausedLocomotion.Clear();
            }
        }

        static bool IsLocomotionProvider(System.Type t)
        {
            for (; t != null; t = t.BaseType) if (t.Name == "LocomotionProvider") return true;
            return false;
        }

        // ------------------------------------------------------------------ shared
        void MoveBody(Vector3 delta)
        {
            if (leash) delta = Leash(delta);
            _cc.Move(delta);
        }

        /// <summary>Clips a move so the body's centre stays inside the leash square while it's up at the cab.</summary>
        Vector3 Leash(Vector3 delta)
        {
            Vector3 p = _cc.transform.TransformPoint(_cc.center);
            if (p.y < leashMinY) return delta;
            float lo = leashCentre.x - leashHalfExtent, hi = leashCentre.x + leashHalfExtent;
            if (p.x + delta.x > hi && delta.x > 0f) { delta.x = Mathf.Max(0f, hi - p.x); _vel.x = Mathf.Min(_vel.x, 0f); }
            if (p.x + delta.x < lo && delta.x < 0f) { delta.x = Mathf.Min(0f, lo - p.x); _vel.x = Mathf.Max(_vel.x, 0f); }
            lo = leashCentre.z - leashHalfExtent;
            hi = leashCentre.z + leashHalfExtent;
            if (p.z + delta.z > hi && delta.z > 0f) { delta.z = Mathf.Max(0f, hi - p.z); _vel.z = Mathf.Min(_vel.z, 0f); }
            if (p.z + delta.z < lo && delta.z < 0f) { delta.z = Mathf.Min(0f, lo - p.z); _vel.z = Mathf.Max(_vel.z, 0f); }
            return delta;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!active) return;
            float into = Vector3.Dot(_vel, hit.normal);
            if (into < 0f) _vel -= into * hit.normal;   // lose the speed going into what we hit, keep the slide
        }

        bool IsSelf(Collider c) => c == _cc || c.transform.IsChildOf(transform) || (_xr != null && _xr.rigRoot != null && c.transform.IsChildOf(_xr.rigRoot));

        bool RaycastWorld(Ray ray, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(ray, _hits, dist, ~0, QueryTriggerInteraction.Ignore);
            return Nearest(n, ref best);
        }

        bool SphereCastWorld(Vector3 from, float r, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.SphereCastNonAlloc(from, r, dir, _hits, dist, ~0, QueryTriggerInteraction.Ignore);
            return Nearest(n, ref best);
        }

        bool Nearest(int n, ref RaycastHit best)
        {
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null || IsSelf(h.collider) || h.distance <= 0f) continue;
                if (h.distance < bestD) { bestD = h.distance; best = h; found = true; }
            }
            return found;
        }

        void Hint(string msg, float seconds)
        {
            _hint = msg;
            _hintUntil = Time.time + seconds;
        }

        void OnGUI()
        {
            if (_vrMode || Time.time >= _hintUntil || string.IsNullOrEmpty(_hint)) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(Screen.width * 0.5f - 320f, Screen.height * 0.18f, 640f, 28f), _hint, style);
        }
    }
}
