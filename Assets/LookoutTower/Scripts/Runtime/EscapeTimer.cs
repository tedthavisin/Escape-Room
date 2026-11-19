using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace LookoutTower
{
    /// <summary>
    /// The escape room's clock: counts down (10 minutes by default) from the start. The player wins when the hatch
    /// keypad unlocks the trapdoor; at 0:00 an alarm rings, the lanterns gutter out, the view fades to black with a
    /// message, and the scene restarts. In the last minute a watch starts ticking and a heartbeat joins in.
    /// <see cref="WristTimer"/> shows the time.
    /// </summary>
    public class EscapeTimer : MonoBehaviour
    {
        public float durationSeconds = 600f;
        public bool startOnLoad = true;
        [Tooltip("The player escapes (and the clock stops) when this unlocks the hatch. Empty = found in the scene.")]
        public HatchKeypadLock hatchLock;

        [Header("Tension")]
        [Tooltip("Looping watch tick; it starts when the last minute begins and speeds up toward 0:00.")]
        public SoundCue tick;
        public float lastMinuteSeconds = 60f;
        [Tooltip("Looping heartbeat that fades in over the last minute.")]
        public SoundCue heartbeat;
        [Tooltip("How much faster the ticking runs by the last second.")]
        public float lastMinuteTickSpeedUp = 0.25f;

        [Header("Time's up")]
        public SoundCue alarm;
        [Tooltip("Black, transparent, unlit material for the fade (the setup step makes M_ScreenFade).")]
        public Material fadeMaterial;
        public string loseMessage = "You didn't make it out.";
        public float fadeSeconds = 2.5f;
        [Tooltip("Seconds after 0:00 before the scene restarts.")]
        public float restartDelay = 8f;

        public UnityEvent onLastMinute = new UnityEvent();
        public UnityEvent onEscaped = new UnityEvent();
        public UnityEvent onTimeUp = new UnityEvent();

        public float TimeLeft { get; private set; }
        public bool IsRunning { get; private set; }
        public bool HasEscaped { get; private set; }
        public bool HasExpired { get; private set; }
        public bool InLastMinute => IsRunning && TimeLeft <= lastMinuteSeconds;

        AudioSource _tick, _heart;
        bool _lastMinuteFired;

        void Start()
        {
            TimeLeft = durationSeconds;
            if (hatchLock == null) hatchLock = FindAnyObjectByType<HatchKeypadLock>();
            if (hatchLock != null) hatchLock.onUnlocked.AddListener(Escape);
            _tick = MakeLoop("Tick", tick);
            _heart = MakeLoop("Heartbeat", heartbeat);
            if (_heart != null) _heart.volume = 0f;
            if (startOnLoad) StartClock();
        }

        void OnDestroy()
        {
            if (hatchLock != null) hatchLock.onUnlocked.RemoveListener(Escape);
        }

        public void StartClock()
        {
            if (HasEscaped || HasExpired) return;
            IsRunning = true;
        }

        AudioSource MakeLoop(string name, in SoundCue cue)
        {
            if (!cue.IsSet) return null;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = cue.clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;                      // in the player's ears: it's their watch / their heart
            s.volume = cue.volume <= 0f ? 1f : cue.volume;
            s.pitch = cue.pitch <= 0f ? 1f : cue.pitch;
            return s;
        }

        void Update()
        {
            if (!IsRunning) return;
            TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);

            if (TimeLeft <= lastMinuteSeconds)
            {
                if (!_lastMinuteFired) { _lastMinuteFired = true; onLastMinute.Invoke(); }
                float k = 1f - TimeLeft / Mathf.Max(1f, lastMinuteSeconds);       // 0 -> 1 over the last minute
                if (_tick != null)
                {
                    if (!_tick.isPlaying) _tick.Play();
                    _tick.volume = (tick.volume <= 0f ? 1f : tick.volume) * Mathf.Clamp01(k * 15f);   // fades in over ~4 s
                    _tick.pitch = (tick.pitch <= 0f ? 1f : tick.pitch) * (1f + lastMinuteTickSpeedUp * k);
                }
                if (_heart != null)
                {
                    if (!_heart.isPlaying) _heart.Play();
                    _heart.volume = (heartbeat.volume <= 0f ? 1f : heartbeat.volume) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.5f));
                    _heart.pitch = 1f + 0.3f * k;                                    // it speeds up too
                }
            }

            if (TimeLeft <= 0f) TimeUp();
        }

        /// <summary>The player got out: stop the clock and the tension sounds.</summary>
        public void Escape()
        {
            if (HasExpired || HasEscaped) return;
            HasEscaped = true;
            IsRunning = false;
            StopLoops();
            Debug.Log($"[LookoutTower] Escaped with {Format(TimeLeft)} left.");
            onEscaped.Invoke();
        }

        void TimeUp()
        {
            IsRunning = false;
            HasExpired = true;
            StopLoops();
            onTimeUp.Invoke();
            StartCoroutine(LoseSequence());
        }

        void StopLoops()
        {
            if (_tick != null) _tick.Stop();
            if (_heart != null) _heart.Stop();
        }

        IEnumerator LoseSequence()
        {
            var head = Camera.main != null ? Camera.main.transform : transform;
            Sfx.Play(alarm, head.position, 0f);

            // the player can't do anything more
            foreach (var d in FindObjectsByType<DesktopExplorer>())
            {
                if (d.flashlight != null) d.flashlight.enabled = false;
                d.enabled = false;
            }
            foreach (var x in FindObjectsByType<XRInteractBridge>()) x.enabled = false;

            // the lanterns gutter, then die
            var lanterns = FindObjectsByType<LanternFlicker>();
            foreach (var l in lanterns) l.sputter = 1f;
            yield return new WaitForSeconds(1.2f);
            var startIntensity = new float[lanterns.Length];
            for (int i = 0; i < lanterns.Length; i++) startIntensity[i] = lanterns[i].baseIntensity;

            var fade = BuildFade(head, out var fadeMat, out var text);
            for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
            {
                float k = t / fadeSeconds;
                for (int i = 0; i < lanterns.Length; i++) lanterns[i].baseIntensity = Mathf.Lerp(startIntensity[i], 0f, k);
                SetAlpha(fadeMat, k);
                yield return null;
            }
            SetAlpha(fadeMat, 1f);
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                if (text != null) text.alpha = t;
                yield return null;
            }

            yield return new WaitForSeconds(Mathf.Max(0f, restartDelay - fadeSeconds - 2.2f));
            RestartGame();
        }

        /// <summary>A black fade and the message, fixed in front of the camera (works on a monitor and in a headset).</summary>
        GameObject BuildFade(Transform head, out Material mat, out TextMeshPro text)
        {
            mat = ViewFader.Create(head, fadeMaterial, out var root);
            root.name = "Time's Up Fade";
            var tgo = new GameObject("Message");
            tgo.transform.SetParent(root.transform, false);
            tgo.transform.localPosition = new Vector3(0f, 0f, 0.25f);
            text = tgo.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = new Vector2(0.3f, 0.08f);
            text.text = loseMessage;
            text.fontSize = 0.22f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.85f, 0.18f, 0.12f);
            text.alpha = 0f;
            text.fontMaterial.renderQueue = 4000;                               // after the black quad
            return root;
        }

        static void SetAlpha(Material m, float a) => ViewFader.SetAlpha(m, a);

        /// <summary>Reloads the current scene: a fresh game with a full clock.</summary>
        public static void RestartGame()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name);
        }

        public static string Format(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        // --- testing helpers (component menu)
        [ContextMenu("Debug: Jump To Last Minute")]
        void DebugLastMinute() { if (IsRunning) TimeLeft = Mathf.Min(TimeLeft, lastMinuteSeconds + 5f); }

        [ContextMenu("Debug: Run Out Now")]
        void DebugRunOut() { if (IsRunning) TimeLeft = Mathf.Min(TimeLeft, 3f); }
    }
}
