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
    /// reports how hard the player is breathing out, 0..1. It's a loudness gate against the room's noise level (a low
    /// point of the last few seconds of sound), tuned so casual talking at the glass counts as well as exhaling or
    /// blowing (warm breath fogs glass either way).
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
        [Tooltip("How far above the room's noise floor a breath must be (dB) to count at all. Casual talking close " +
                 "to the mic sits roughly 10-20 dB above the room.")]
        public float thresholdAboveFloorDb = 8f;
        [Tooltip("dB above the noise floor that counts as a full-strength breath (normal speaking voice or a firm exhale).")]
        public float fullAboveFloorDb = 16f;
        [Tooltip("Anything quieter than this (dBFS) is never a breath, however quiet the room is.")]
        public float absoluteMinDb = -50f;
        [Tooltip("How fast the reading rises / falls (per second). The slow fall carries it over the gaps between words.")]
        public float attack = 10f, release = 1.5f;
        [Tooltip("Seconds of recent sound the room's noise level is judged from.")]
        public float floorWindowSeconds = 6f;
        [Tooltip("The room's noise level is this low point of the recent sound: 0.2 = quieter than 80% of it. Speech " +
                 "and breathing pause, so the room shows through the gaps; a fan or AC that keeps going becomes the room.")]
        [Range(0.05f, 0.5f)] public float floorPercentile = 0.2f;

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
        // levels sampled 20 times a second over the floor window
        const float HistoryRate = 20f;
        readonly float[] _history = new float[256], _sorted = new float[256];
        int _historyCount, _historyHead;
        float _nextHistory;
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
                cb.PermissionDenied += _ =>
                {
                    Debug.LogWarning("[LookoutTower] Microphone permission denied; breath puzzles fall back to the keyboard key.");
                    PuzzleHints.Show("Microphone access is off, so your breath can't fog the windows. Allow it in the headset's app permissions.");
                };
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
            _historyCount = _historyHead = 0;
            _nextHistory = 0f;
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
            if (n <= 0) return 0f;                             // mic not delivering yet (e.g. no permission)
            _clip.GetData(_buf, (pos - n + total) % total);   // GetData wraps round the looping clip itself

            double sum = 0;
            for (int i = 0; i < n; i++) sum += _buf[i] * _buf[i];
            float rms = Mathf.Sqrt((float)(sum / n));
            LevelDb = 20f * Mathf.Log10(Mathf.Max(rms, 1e-5f));

            UpdateFloor();
            if (Time.unscaledTime < _warmUntil) return 0f;   // learn the room for the first second

            if (LevelDb < absoluteMinDb) return 0f;
            float above = LevelDb - _floorDb;
            return Mathf.Clamp01(Mathf.InverseLerp(thresholdAboveFloorDb, fullAboveFloorDb, above));
        }

        /// <summary>
        /// Room noise = a low percentile of the last few seconds of levels. A brief silence barely moves it (unlike
        /// tracking the minimum), and steady background noise becomes the floor within a few seconds.
        /// </summary>
        void UpdateFloor()
        {
            float now = Time.unscaledTime;
            if (now < _nextHistory) return;
            _nextHistory = now + 1f / HistoryRate;
            int capacity = Mathf.Clamp(Mathf.RoundToInt(floorWindowSeconds * HistoryRate), 8, _history.Length);
            _history[_historyHead] = LevelDb;
            _historyHead = (_historyHead + 1) % capacity;
            _historyCount = Mathf.Min(_historyCount + 1, capacity);
            System.Array.Copy(_history, _sorted, _historyCount);
            System.Array.Sort(_sorted, 0, _historyCount);
            _floorDb = Mathf.Clamp(_sorted[Mathf.FloorToInt(floorPercentile * (_historyCount - 1))], -90f, -20f);
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
