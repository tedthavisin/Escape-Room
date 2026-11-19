using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LookoutTower
{
    /// <summary>
    /// A red RESET button on the right wrist (VR), opposite the timer watch on the left. Touch it with the tip of the
    /// left controller and pull the left trigger: the game restarts from the beginning with a full clock
    /// (<see cref="EscapeTimer.RestartGame"/>, the same as Play Again). Needs the trigger, so resting your hands
    /// together can't reset by accident. Added to the right controller by <see cref="VrRig"/>.
    /// </summary>
    public class WristResetButton : MonoBehaviour
    {
        [Tooltip("The hand that presses it (the left controller).")]
        public Transform presser;
        [Tooltip("Button position on this (right) controller: a little up and back toward the wrist, like the watch.")]
        public Vector3 offset = new Vector3(0f, 0.035f, -0.09f);
        public Vector3 euler = new Vector3(90f, 0f, 0f);
        [Tooltip("How close (metres) the presser's tip has to be to the button.")]
        public float touchRadius = 0.045f;
        [Tooltip("Ignore presses for this long after the scene starts (so a held trigger can't chain resets).")]
        public float armDelay = 1.5f;

        static readonly Color Idle = new Color(0.75f, 0.08f, 0.06f), Touched = new Color(1f, 0.35f, 0.2f);

        Transform _root, _cap;
        MeshRenderer _capRenderer;
        MaterialPropertyBlock _mpb;
        TextMeshPro _label;
        InputAction _trigger;
        float _armedAt;
        bool _resetting;

        void Start()
        {
            _armedAt = Time.time + armDelay;
            Build();
        }

        void OnEnable()
        {
            _trigger = new InputAction("WristReset", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
            _trigger.Enable();
        }

        void OnDisable()
        {
            _trigger?.Dispose();
            _trigger = null;
        }

        void Build()
        {
            _root = new GameObject("Wrist Reset").transform;
            _root.SetParent(transform, false);
            _root.localPosition = offset;
            _root.localRotation = Quaternion.Euler(euler);   // local -Z points up out of the back of the wrist

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(plate.GetComponent<Collider>());
            plate.name = "Plate";
            plate.transform.SetParent(_root, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            plate.transform.localScale = new Vector3(0.05f, 0.06f, 0.006f);
            var pr = plate.GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            _mpb.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.05f));
            pr.SetPropertyBlock(_mpb);
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(cap.GetComponent<Collider>());
            cap.name = "Button";
            cap.transform.SetParent(_root, false);
            cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // cylinder axis out of the wrist
            cap.transform.localScale = new Vector3(0.03f, 0.005f, 0.03f);
            _cap = cap.transform;
            _capRenderer = cap.GetComponent<MeshRenderer>();
            _capRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetCap(false);

            var tgo = new GameObject("Label");
            tgo.transform.SetParent(_root, false);
            tgo.transform.localPosition = new Vector3(0f, -0.019f, -0.001f);   // below the button   // along the plate's edge, toward the elbow
            _label = tgo.AddComponent<TextMeshPro>();
            _label.rectTransform.sizeDelta = new Vector2(0.05f, 0.012f);
            _label.fontSize = 0.07f;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.color = new Color(0.95f, 0.9f, 0.85f);
            _label.text = "RESET";
        }

        void SetCap(bool touched)
        {
            // pushed in a little while touched
            _cap.localPosition = new Vector3(0f, 0.008f, touched ? -0.003f : -0.006f);
            _mpb.SetColor("_BaseColor", touched ? Touched : Idle);
            _capRenderer.SetPropertyBlock(_mpb);
        }

        void Update()
        {
            if (_cap == null || presser == null || _resetting) return;
            Vector3 tip = presser.position + presser.forward * 0.01f;
            bool touching = presser.gameObject.activeInHierarchy && presser.localPosition.sqrMagnitude > 1e-6f
                            && Vector3.Distance(tip, _cap.position) <= touchRadius;
            SetCap(touching);
            _label.text = touching ? "PULL TRIGGER" : "RESET";
            if (touching && Time.time >= _armedAt && _trigger != null && _trigger.WasPressedThisFrame())
            {
                _resetting = true;
                _label.text = "RESETTING...";
                Debug.Log("[LookoutTower] Wrist reset: restarting the game.");
                EscapeTimer.RestartGame();
            }
        }
    }
}
