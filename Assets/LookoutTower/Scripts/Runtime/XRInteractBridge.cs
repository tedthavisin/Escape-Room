using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LookoutTower
{
    /// <summary>
    /// Lets VR controllers use the same <see cref="IInteractable"/>s as the desktop explorer: point a controller at a
    /// door, the trapdoor, a window or the fire finder and pull its trigger. Y or B (or T on the keyboard) jumps between
    /// the path and the cab, like T on the desktop, which saves the 20-flight climb while testing.
    /// </summary>
    public class XRInteractBridge : MonoBehaviour
    {
        public Transform leftController;
        public Transform rightController;
        [Tooltip("Rig root moved by the cab jump (the XR Origin).")]
        public Transform rigRoot;
        public Transform cabSpawn;
        public float reach = 2.6f;

        InputAction _leftTrigger, _rightTrigger, _cabJump;
        readonly RaycastHit[] _hits = new RaycastHit[16];
        Vector3 _groundSpawn;
        Quaternion _groundRot;

        void OnEnable()
        {
            _leftTrigger = new InputAction("LeftInteract", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
            _rightTrigger = new InputAction("RightInteract", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
            // Y on a Quest controller; B on the right one too, and T on the keyboard (same key as the desktop explorer),
            // because simulators don't always map a gamepad's Y to the left controller's Y.
            _cabJump = new InputAction("CabJump", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
            _cabJump.AddBinding("<XRController>{RightHand}/secondaryButton");
            _cabJump.AddBinding("<Keyboard>/t");
            _leftTrigger.Enable();
            _rightTrigger.Enable();
            _cabJump.Enable();
            if (rigRoot != null)
            {
                _groundSpawn = rigRoot.position;
                _groundRot = rigRoot.rotation;
            }
        }

        void OnDisable()
        {
            _leftTrigger?.Dispose();
            _rightTrigger?.Dispose();
            _cabJump?.Dispose();
        }

        void Update()
        {
            if (_leftTrigger.WasPressedThisFrame()) TryInteract(leftController);
            if (_rightTrigger.WasPressedThisFrame()) TryInteract(rightController);
            if (_cabJump.WasPressedThisFrame()) CabJump();
        }

        void TryInteract(Transform hand)
        {
            if (hand == null) return;
            var ray = new Ray(hand.position, hand.forward);
            // nearest hit that isn't part of the rig itself (its CharacterController body can sit in the ray's path)
            int n = Physics.RaycastNonAlloc(ray, _hits, reach, ~0, QueryTriggerInteraction.Ignore);
            Collider nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (rigRoot != null && c.transform.IsChildOf(rigRoot)) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; nearest = c; }
            }
            if (nearest == null) return;
            var target = nearest.GetComponentInParent<IInteractable>();
            if (target == null) return;
            target.Interact();
            if (target is HingedOpenable h && h.locked) Debug.Log($"[LookoutTower] The {h.displayName} won't budge.");
        }

        void CabJump()
        {
            if (rigRoot == null || cabSpawn == null) return;
            var origin = rigRoot.GetComponent<XROrigin>();
            var head = origin != null && origin.Camera != null ? origin.Camera.transform : rigRoot;
            bool inCab = Vector3.Distance(head.position, cabSpawn.position) < 8f;
            Vector3 targetPos = inCab ? _groundSpawn : cabSpawn.position;
            Quaternion targetRot = inCab ? _groundRot : cabSpawn.rotation;

            var cc = rigRoot.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = false;
            if (origin != null)
            {
                // Turn so the head faces the target's forward, then put the HEAD (not the rig's floor origin) on the
                // target: the player may be standing well off-centre in their play area.
                Vector3 camFwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                Vector3 tgtFwd = Vector3.ProjectOnPlane(targetRot * Vector3.forward, Vector3.up);
                if (camFwd.sqrMagnitude > 1e-4f && tgtFwd.sqrMagnitude > 1e-4f)
                    origin.RotateAroundCameraUsingOriginUp(Vector3.SignedAngle(camFwd, tgtFwd, Vector3.up));
                Vector3 p = rigRoot.position;
                p.x += targetPos.x - head.position.x;
                p.z += targetPos.z - head.position.z;
                p.y = targetPos.y;
                rigRoot.position = p;
            }
            else rigRoot.SetPositionAndRotation(targetPos, targetRot);
            if (cc != null) cc.enabled = true;
        }
    }
}
