using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace LookoutTower
{
    /// <summary>
    /// Listens to the microphone (the Quest headset mic, or the computer's mic in the Editor / Meta XR Simulator) and
    /// reports how hard the player is breathing out, 0..1. Exhaling or blowing near the mic is much louder than the
    /// room, so this is a loudness gate against a slowly tracked noise floor. Talking right at the glass counts too,
    /// which is also true of real glass.
    ///
    /// One sensor serves every <see cref="BreathFogWindow"/>. If the scene doesn't have one, the first window
    /// creates it with the defaults below; add this component to any object to tune it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class BreathSensor : MonoBehaviour
    {
        public static BreathSensor Instance { get; private set; }

        [Header("Microphone")]
        [Tooltip("Leave empty for the default microphone.")]
        public string deviceName = "";
        public bool useMicrophone = true;
        public int sampleRate = 16000;

        [Header("Detection")]
        [Tooltip("How far above the room's noise floor a breath must be (dB) to count at all.")]
        public float thresholdAboveFloorDb = 12f;
        [Tooltip("dB above the noise floor that counts as a full-strength breath.")]
        public float fullAboveFloorDb = 28f;
        [Tooltip("Anything quieter than this (dBFS) is never a breath, however quiet the room is.")]
        public float absoluteMinDb = -50f;
        [Tooltip("How fast the reading rises / falls (per second).")]
        public float attack = 10f, release = 4f;

        [Header("Testing without a mic")]
        [Tooltip("Hold this key to breathe at full strength (desktop explorer, simulator, or when the mic is unavailable).")]
        public bool allowKeyFallback = true;
#if ENABLE_INPUT_SYSTEM
        public Key fallbackKey = Key.B;
#else
        public KeyCode fallbackKey = KeyCode.B;
#endif
        [Tooltip("Breathe at full strength while ticked (try the puzzle in Play mode without a mic).")]
        public bool simulateBreath;
        public bool logLevels;

        /// <summary>Smoothed breath strength, 0 (not breathing) .. 1 (hard exhale).</summary>
        public float Intensity { get; private set; }
        public bool IsBreathing => Intensity > 0.05f;
        /// <summary>Latest raw mic level in dBFS (for tuning).</summary>
        public float LevelDb { get; private set; } = -80f;
        public float NoiseFloorDb => _floorDb;
        public bool MicrophoneActive => _clip != null;

        AudioClip _clip;
        string _device;
        int _readPos;
        readonly float[] _buf = new float[1024];
        float _warmUntil;
        float _floorDb = -60f;
        bool _permissionAsked;
        float _nextLog;

        /// <summary>The scene's sensor, created on first use if there isn't one.</summary>
        public static BreathSensor GetOrCreate()
        {
            if (Instance != null) return Instance;
            var found = FindAnyObjectByType<BreathSensor>();
            if (found != null) { Instance = found; return found; }
            return new GameObject("Breath Sensor (auto)").AddComponent<BreathSensor>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnEnable() => TryStartMic();

        void OnDisable() => StopMic();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void TryStartMic()
        {
            if (!useMicrophone || _clip != null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                if (_permissionAsked) return;
                _permissionAsked = true;
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += _ => TryStartMic();
                cb.PermissionDenied += _ => Debug.LogWarning("[LookoutTower] Microphone permission denied; breath puzzles fall back to the keyboard key.");
                Permission.RequestUserPermission(Permission.Microphone, cb);
                return;
            }
#endif
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[LookoutTower] No microphone found; hold " + fallbackKey + " to breathe on glass.");
                return;
            }
            _device = string.IsNullOrEmpty(deviceName) ? null : deviceName;
            Microphone.GetDeviceCaps(_device, out int minF, out int maxF);
            int rate = sampleRate;
            if (minF != 0 || maxF != 0) rate = Mathf.Clamp(rate, minF, maxF);
            _clip = Microphone.Start(_device, true, 1, rate);
            _readPos = -1;
            _floorDb = -50f;
            _warmUntil = Time.unscaledTime + 1f;
            if (_clip == null) Debug.LogWarning("[LookoutTower] Couldn't start the microphone.");
        }

        void StopMic()
        {
            if (_clip == null) return;
            Microphone.End(_device);
            _clip = null;
        }

        void Update()
        {
            float target = 0f;

            if (_clip != null) target = ReadMic();
            else if (useMicrophone && !_permissionAsked) TryStartMic();

            if (simulateBreath || (allowKeyFallback && KeyHeld())) target = 1f;

            float rate = target > Intensity ? attack : release;
            Intensity = Mathf.MoveTowards(Intensity, target, rate * Time.deltaTime);

            if (logLevels && Time.unscaledTime > _nextLog)
            {
                _nextLog = Time.unscaledTime + 0.25f;
                Debug.Log($"[BreathSensor] level {LevelDb:F1} dB, floor {_floorDb:F1} dB, intensity {Intensity:F2}");
            }
        }

        /// <summary>Loudness of the most recent ~64 ms of mic input, turned into a 0..1 breath strength.</summary>
        float ReadMic()
        {
            int pos = Microphone.GetPosition(_device);
            if (pos == _readPos) return Intensity;            // no new samples this frame
            _readPos = pos;
            int total = _clip.samples;
            int n = Mathf.Min(_buf.Length, total);
            _clip.GetData(_buf, (pos - n + total) % total);   // GetData wraps round the looping clip itself

            double sum = 0;
            for (int i = 0; i < n; i++) sum += _buf[i] * _buf[i];
            float rms = Mathf.Sqrt((float)(sum / n));
            LevelDb = 20f * Mathf.Log10(Mathf.Max(rms, 1e-5f));

            float dt = Time.unscaledDeltaTime;
            if (Time.unscaledTime < _warmUntil)
            {
                // Learn the room for the first second before reporting anything.
                _floorDb = Mathf.Lerp(_floorDb, LevelDb, 1f - Mathf.Exp(-dt * 8f));
                return 0f;
            }
            // Noise floor: drops quickly to quiet moments and creeps up slowly, so a long breath doesn't become
            // "the room" but a fan switched on does, after a few seconds.
            _floorDb = LevelDb < _floorDb ? Mathf.Lerp(_floorDb, LevelDb, 1f - Mathf.Exp(-dt * 4f))
                                          : _floorDb + Mathf.Min(LevelDb - _floorDb, 2f * dt);
            _floorDb = Mathf.Clamp(_floorDb, -90f, -20f);

            if (LevelDb < absoluteMinDb) return 0f;
            float above = LevelDb - _floorDb;
            return Mathf.Clamp01(Mathf.InverseLerp(thresholdAboveFloorDb, fullAboveFloorDb, above));
        }

        bool KeyHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current[fallbackKey].isPressed;
#else
            return Input.GetKey(fallbackKey);
#endif
        }
    }
}
