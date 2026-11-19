using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LookoutTower
{
    /// <summary>
    /// Thumbstick movement for the VR rig (<see cref="VrRig"/>): left stick walks the way you're looking, right stick
    /// snap-turns (or smooth-turns). The rig's CharacterController follows the headset, so you can also walk and duck for
    /// real (under the hatch edge), and it collides and falls like the desktop player.
    /// Stops when the escape timer runs out or the win screen is up, and hands walking over to <see cref="GorillaMode"/>
    /// while that's on (turning still works).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class VrLocomotion : MonoBehaviour
    {
        public Transform head;
        public float moveSpeed = 2.0f;
        [Tooltip("Hold the left grip to move this much faster.")]
        public float sprintMultiplier = 1.8f;
        public float deadzone = 0.15f;
        public bool smoothTurn;
        public float snapTurnDegrees = 45f;
        public float smoothTurnSpeed = 120f;
        public float gravity = -15f;
        [Tooltip("The body's height follows the headset (crouch for real) between these.")]
        public float minHeight = 0.9f, maxHeight = 2.1f;
        [Tooltip("Head height above the top of the body capsule.")]
        public float headClearance = 0.1f;

        CharacterController _cc;
        XROrigin _origin;
        XRInteractBridge _bridge;
        GorillaMode _gorilla;
        InputAction _move, _turn, _sprint;
        bool _turnArmed = true;
        float _vy;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _origin = GetComponent<XROrigin>();
            _bridge = GetComponent<XRInteractBridge>();
        }

        void OnEnable()
        {
            _move = new InputAction("Move", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}", expectedControlType: "Vector2");
            _turn = new InputAction("Turn", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}", expectedControlType: "Vector2");
            _sprint = new InputAction("Sprint", InputActionType.Button, "<XRController>{LeftHand}/{GripButton}");
            _move.Enable();
            _turn.Enable();
            _sprint.Enable();
        }

        void OnDisable()
        {
            _move?.Dispose();
            _turn?.Dispose();
            _sprint?.Dispose();
        }

        void Update()
        {
            if (head == null || !_cc.enabled) return;
            FollowHead();
            if ((_bridge != null && !_bridge.enabled) || WinScreen.Showing != null) return;   // time's up / escaped
            float dt = Time.deltaTime;
            Turn(dt);

            if (_gorilla == null) _gorilla = GetComponent<GorillaMode>();
            if (_gorilla != null && _gorilla.active) { _vy = 0f; return; }   // the gorilla moves the body

            Vector2 mv = _move.ReadValue<Vector2>();
            if (mv.magnitude < deadzone) mv = Vector2.zero;
            else if (mv.sqrMagnitude > 1f) mv.Normalize();
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);   // looking straight down
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float spd = moveSpeed * (_sprint.IsPressed() ? sprintMultiplier : 1f);
            Vector3 move = (fwd * mv.y + right * mv.x) * spd;
            if (_cc.isGrounded && _vy < 0f) _vy = -2f;
            _vy += gravity * dt;
            move.y = _vy;
            _cc.Move(move * dt);
        }

        /// <summary>Keeps the capsule under the headset and as tall as the player's head.</summary>
        void FollowHead()
        {
            Vector3 local = transform.InverseTransformPoint(head.position);
            float h = Mathf.Clamp(local.y + headClearance, minHeight, maxHeight);
            _cc.height = h;
            _cc.center = new Vector3(local.x, h * 0.5f + _cc.skinWidth, local.z);
        }

        void Turn(float dt)
        {
            float x = _turn.ReadValue<Vector2>().x;
            if (smoothTurn)
            {
                if (Mathf.Abs(x) > deadzone) RotateAroundHead(x * smoothTurnSpeed * dt);
                return;
            }
            if (_turnArmed && Mathf.Abs(x) > 0.7f)
            {
                RotateAroundHead(Mathf.Sign(x) * snapTurnDegrees);
                _turnArmed = false;
            }
            else if (Mathf.Abs(x) < 0.3f) _turnArmed = true;
        }

        void RotateAroundHead(float degrees)
        {
            if (_origin != null) _origin.RotateAroundCameraUsingOriginUp(degrees);
            else transform.RotateAround(head.position, Vector3.up, degrees);
            Physics.SyncTransforms();   // or the CharacterController moves from where the rig was before the turn
        }
    }
}
