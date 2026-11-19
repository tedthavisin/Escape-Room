using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

namespace LookoutTower
{
    /// <summary>
    /// The VR player. When an XR runtime is running (a Quest, Quest Link, the Meta XR Simulator) and the scene has
    /// the desktop explorer but no VR rig of its own, this builds one at the explorer's spot and switches the explorer
    /// off: an XR Origin with a head-tracked camera (tagged MainCamera, so everything that uses Camera.main follows
    /// the headset), two tracked controllers with laser pointers, a CharacterController body that follows the head,
    /// <see cref="VrLocomotion"/> (thumbsticks) and <see cref="XRInteractBridge"/> (trigger to interact).
    /// Built at runtime, so the generated scene doesn't need changing and rebuilds can't lose it.
    ///
    /// Right thumbstick click toggles a flashlight on the right controller (F on the desktop).
    /// </summary>
    public class VrRig : MonoBehaviour
    {
        public Camera head;
        public Transform leftController, rightController;
        public Light flashlight;
        public float pointerReach = 2.6f;
        [Tooltip("Where the head is put when tracking starts (the desktop player's spawn), facing its forward.")]
        public Vector3 spawnPosition;
        public Quaternion spawnRotation = Quaternion.identity;

        const float WaitForXrSeconds = 3f;
        float _waitUntil;
        bool _built, _recentred;
        LineRenderer[] _lasers;
        InputAction _flashlightToggle;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        // ------------------------------------------------------------------ bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SetUp(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) SetUp(scene);
        }

        static void SetUp(Scene scene)
        {
            if (FindAnyObjectByType<VrRig>() != null) return;
            if (FindAnyObjectByType<XRInteractBridge>(FindObjectsInactive.Include) != null) return;   // the scene has its own VR rig
            if (FindAnyObjectByType<DesktopExplorer>() == null) return;                               // not a lookout scene
            var go = new GameObject("XR Rig (VR player)");
            SceneManager.MoveGameObjectToScene(go, scene);
            var rig = go.AddComponent<VrRig>();
            rig._waitUntil = Time.realtimeSinceStartup + WaitForXrSeconds;
            rig.TryBuild();   // usually XR is already up, so the desktop camera never draws a frame in the headset
        }

        static bool XrRunning()
        {
            var s = XRGeneralSettings.Instance;
            return s != null && s.Manager != null && s.Manager.activeLoader != null;
        }

        void TryBuild()
        {
            if (_built) return;
            if (XrRunning()) { Build(); return; }
            if (Time.realtimeSinceStartup > _waitUntil)
            {
                Debug.Log("[LookoutTower] Player mode: desktop explorer (no XR runtime)");
                Destroy(gameObject);
            }
        }

        // ------------------------------------------------------------------ building
        void Build()
        {
            _built = true;
            var desktop = FindAnyObjectByType<DesktopExplorer>();
            var deskT = desktop.transform;
            spawnPosition = deskT.position;
            spawnRotation = Quaternion.Euler(0f, deskT.eulerAngles.y, 0f);

            gameObject.SetActive(false);   // so XROrigin and friends wake up fully wired
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);

            var offset = new GameObject("Camera Offset").transform;
            offset.SetParent(transform, false);

            // head
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(offset, false);
            head = camGo.AddComponent<Camera>();
            head.nearClipPlane = 0.05f;
            head.farClipPlane = desktop.cam != null ? desktop.cam.farClipPlane : 260f;
            head.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            var camData = head.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = false;   // vignette/bloom are for desktop (README: trim for VR)
            camData.antialiasing = AntialiasingMode.None;
            Track(camGo, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation", "<XRHMD>/trackingState");

            // hands: the aim pose, so a controller's forward is where it points
            leftController = Controller(offset, "Left Controller", "{LeftHand}");
            rightController = Controller(offset, "Right Controller", "{RightHand}");

            // flashlight on the right controller, same beam as the desktop one
            var flGo = new GameObject("Flashlight");
            flGo.transform.SetParent(rightController, false);
            flashlight = flGo.AddComponent<Light>();
            var src = desktop.flashlight;
            flashlight.type = LightType.Spot;
            flashlight.spotAngle = src != null ? src.spotAngle : 55f;
            flashlight.innerSpotAngle = src != null ? src.innerSpotAngle : 22f;
            flashlight.range = src != null ? src.range : 28f;
            flashlight.intensity = src != null ? src.intensity : 2.2f;
            flashlight.color = src != null ? src.color : new Color(1f, 0.95f, 0.86f);
            flashlight.shadows = LightShadows.None;   // Quest: no additional-light shadows anyway
            flashlight.enabled = false;

            var origin = gameObject.AddComponent<XROrigin>();
            origin.Origin = gameObject;
            origin.CameraFloorOffsetObject = offset.gameObject;
            origin.Camera = head;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            // body: same proportions as the desktop player, but it follows the head (VrLocomotion)
            var cc = gameObject.AddComponent<CharacterController>();
            var dcc = desktop.GetComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.25f;
            cc.center = new Vector3(0f, 0.9f + cc.skinWidth, 0f);
            cc.slopeLimit = dcc != null ? dcc.slopeLimit : 70f;    // the last flight is a steep ship's ladder
            cc.stepOffset = dcc != null ? dcc.stepOffset : 0.35f;
            cc.skinWidth = 0.03f;

            var bridge = gameObject.AddComponent<XRInteractBridge>();
            bridge.leftController = leftController;
            bridge.rightController = rightController;
            bridge.rigRoot = transform;
            bridge.cabSpawn = desktop.cabSpawn;
            bridge.allowCabJump = desktop.allowCabTeleport;   // off in the escape room: it would skip the hatch lock

            // RESET on the right wrist: touch it with the left controller and pull the left trigger
            var reset = rightController.gameObject.AddComponent<WristResetButton>();
            reset.presser = leftController;

            var loco = gameObject.AddComponent<VrLocomotion>();
            loco.head = head.transform;

            desktop.gameObject.SetActive(false);   // its camera goes too, so Camera.main is the headset
            gameObject.SetActive(true);
            Debug.Log("[LookoutTower] Player mode: VR rig");
        }

        static void Track(GameObject go, string pos, string rot, string state)
        {
            var tpd = go.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction(go.name + " Position", InputActionType.Value, pos, expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction(go.name + " Rotation", InputActionType.Value, rot, expectedControlType: "Quaternion"));
            tpd.trackingStateInput = new InputActionProperty(new InputAction(go.name + " Tracking State", InputActionType.Value, state, expectedControlType: "Integer"));
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }

        Transform Controller(Transform parent, string name, string hand)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Track(go, $"<XRController>{hand}/pointerPosition", $"<XRController>{hand}/pointerRotation", $"<XRController>{hand}/trackingState");

            // a stand-in controller so you can see your hands (the aim pose sits at the controller's tip)
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Controller Model";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, -0.015f, -0.06f);
            body.transform.localScale = new Vector3(0.035f, 0.035f, 0.11f);
            var r = body.GetComponent<MeshRenderer>();
            var dark = FindMat("M_PlasticBlack");
            if (dark != null) r.sharedMaterial = dark;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        static Material FindMat(string name)
        {
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name == name) return m;
            return null;
        }

        // ------------------------------------------------------------------ runtime
        void OnEnable()
        {
            if (!_built) return;
            _flashlightToggle = new InputAction("Flashlight", InputActionType.Button, "<XRController>{RightHand}/{Primary2DAxisClick}");
            _flashlightToggle.Enable();
            if (_lasers == null) _lasers = new[] { Laser(leftController), Laser(rightController) };
            if (_hintText == null) _hintText = HintText(head.transform);
            PuzzleHints.Shown += ShowHint;
        }

        void OnDisable()
        {
            _flashlightToggle?.Dispose();
            _flashlightToggle = null;
            PuzzleHints.Shown -= ShowHint;
        }

        // ------------------------------------------------------------------ hints in the headset
        // Puzzle hints are drawn with OnGUI on the desktop, which a headset never shows: float them in front of the eyes.
        TextMesh _hintText;
        float _hintUntil;

        static TextMesh HintText(Transform eye)
        {
            var go = new GameObject("Hint Text");
            go.transform.SetParent(eye, false);
            go.transform.localPosition = new Vector3(0f, -0.12f, 0.8f);
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.fontSize = 48;
            tm.characterSize = 0.0045f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.95f, 0.85f, 0.95f);
            var r = go.GetComponent<MeshRenderer>();
            if (font != null) r.sharedMaterial = font.material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.enabled = false;
            return tm;
        }

        void ShowHint(string msg)
        {
            if (_hintText == null || string.IsNullOrEmpty(msg)) return;
            _hintText.text = Wrap(msg, 42);
            _hintText.GetComponent<MeshRenderer>().enabled = true;
            _hintUntil = Time.time + 5f;
        }

        static string Wrap(string s, int width)
        {
            var sb = new System.Text.StringBuilder();
            int line = 0;
            foreach (var word in s.Split(' '))
            {
                if (line > 0 && line + 1 + word.Length > width) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(word);
                line += word.Length;
            }
            return sb.ToString();
        }

        static LineRenderer Laser(Transform hand)
        {
            var go = new GameObject("Pointer");
            go.transform.SetParent(hand, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = 0.004f;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var shader = Shader.Find("UI/Default");   // always included in builds; unlit, uses the line's colours
            if (shader != null) lr.sharedMaterial = new Material(shader);
            return lr;
        }

        void Update()
        {
            if (!_built) { TryBuild(); return; }
            Recentre();
            if (_flashlightToggle != null && _flashlightToggle.WasPressedThisFrame() && flashlight != null)
                flashlight.enabled = !flashlight.enabled;
            UpdateLaser(_lasers?[0], leftController);
            UpdateLaser(_lasers?[1], rightController);
            if (_hintText != null && Time.time >= _hintUntil) _hintText.GetComponent<MeshRenderer>().enabled = false;
        }

        /// <summary>Once tracking starts, put the head on the spawn point facing the spawn's way (the player may be standing anywhere in their play area).</summary>
        void Recentre()
        {
            if (_recentred || head == null) return;
            if (head.transform.localPosition.sqrMagnitude < 1e-6f) return;   // no pose yet
            _recentred = true;
            var origin = GetComponent<XROrigin>();
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Vector3 camFwd = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
            Vector3 tgtFwd = spawnRotation * Vector3.forward;
            if (origin != null && camFwd.sqrMagnitude > 1e-4f)
                origin.RotateAroundCameraUsingOriginUp(Vector3.SignedAngle(camFwd, tgtFwd, Vector3.up));
            Vector3 p = transform.position;
            p.x += spawnPosition.x - head.transform.position.x;
            p.z += spawnPosition.z - head.transform.position.z;
            p.y = spawnPosition.y;
            transform.position = p;
            if (cc != null) cc.enabled = true;
        }

        void UpdateLaser(LineRenderer lr, Transform hand)
        {
            if (lr == null || hand == null) return;
            bool show = hand.gameObject.activeInHierarchy && hand.localPosition.sqrMagnitude > 1e-6f;
            lr.enabled = show;
            if (!show) return;
            var ray = new Ray(hand.position, hand.forward);
            float len = pointerReach;
            int n = Physics.RaycastNonAlloc(ray, _hits, pointerReach, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!_hits[i].collider.transform.IsChildOf(transform) && _hits[i].distance < len) len = _hits[i].distance;
            bool onSomething = InteractionPicker.Pick(ray, pointerReach, 0.08f, transform) != null;
            var c = onSomething ? new Color(1f, 0.8f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.25f);
            lr.startColor = c;
            lr.endColor = new Color(c.r, c.g, c.b, onSomething ? 0.9f : 0.05f);
            lr.SetPosition(0, ray.origin);
            lr.SetPosition(1, ray.GetPoint(len));
        }
    }
}
