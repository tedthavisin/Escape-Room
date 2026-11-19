using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Small synthesized sound effects for the fuse box (no audio files needed): fuse threads, the main switch clunk,
    /// sparks, and a mains hum. Swap any of them for recorded clips by assigning the static fields at startup.
    /// </summary>
    public static class PuzzleSfx
    {
        const int Rate = 22050;
        static AudioClip s_Pickup, s_SetDown, s_Screw, s_Clunk, s_Zap, s_Hum, s_Surge;

        public static AudioClip Pickup => s_Pickup != null ? s_Pickup : (s_Pickup = Make("Fuse Pickup", 0.05f, PickupWave));
        public static AudioClip SetDown => s_SetDown != null ? s_SetDown : (s_SetDown = Make("Fuse SetDown", 0.12f, SetDownWave));
        public static AudioClip Screw => s_Screw != null ? s_Screw : (s_Screw = Make("Fuse Screw", 0.48f, ScrewWave));
        public static AudioClip Clunk => s_Clunk != null ? s_Clunk : (s_Clunk = Make("Main Switch", 0.35f, ClunkWave));
        public static AudioClip Zap => s_Zap != null ? s_Zap : (s_Zap = Make("Spark", 0.45f, ZapWave));
        public static AudioClip Hum => s_Hum != null ? s_Hum : (s_Hum = Make("Mains Hum", 0.5f, HumWave));
        public static AudioClip Surge => s_Surge != null ? s_Surge : (s_Surge = Make("Power Surge", 1.6f, SurgeWave));

        public static void Play(AudioClip clip, Vector3 at, float volume = 1f)
        {
            if (clip == null || !Application.isPlaying) return;
            AudioSource.PlayClipAtPoint(clip, at, volume);
        }

        delegate float Wave(float t, System.Random rng);

        static AudioClip Make(string name, float seconds, Wave w)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(w(i / (float)Rate, rng), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);
        static float Env(float t, float decay) => Mathf.Exp(-t / decay);

        static float PickupWave(float t, System.Random r) =>
            0.5f * Noise(r) * Env(t, 0.006f) + 0.25f * Mathf.Sin(2f * Mathf.PI * 2400f * t) * Env(t, 0.01f);

        static float SetDownWave(float t, System.Random r)
        {
            float a = 0.6f * Noise(r) * Env(t, 0.004f) + 0.4f * Mathf.Sin(2f * Mathf.PI * 900f * t) * Env(t, 0.015f);
            float t2 = t - 0.045f;
            if (t2 > 0f) a += 0.35f * Noise(r) * Env(t2, 0.003f) + 0.2f * Mathf.Sin(2f * Mathf.PI * 1100f * t2) * Env(t2, 0.01f);
            return a;
        }

        static float ScrewWave(float t, System.Random r)
        {
            // thread ticks speeding up as it seats, over a faint scrape
            float a = 0.06f * Noise(r);
            for (int k = 0; k < 9; k++)
            {
                float tk = 0.42f * Mathf.Sqrt(k / 9f);
                float d = t - tk;
                if (d >= 0f && d < 0.03f) a += 0.5f * Noise(r) * Env(d, 0.003f) + 0.2f * Mathf.Sin(2f * Mathf.PI * 3100f * d) * Env(d, 0.006f);
            }
            return a * Mathf.Clamp01((0.48f - t) / 0.05f);
        }

        static float ClunkWave(float t, System.Random r) =>
            0.9f * Mathf.Sin(2f * Mathf.PI * (85f - 30f * t) * t) * Env(t, 0.06f)
            + 0.5f * Noise(r) * Env(t, 0.012f)
            + 0.18f * Mathf.Sin(2f * Mathf.PI * 1260f * t) * Env(t, 0.09f)
            + 0.08f * Mathf.Sin(2f * Mathf.PI * 2730f * t) * Env(t, 0.05f);

        static float ZapWave(float t, System.Random r)
        {
            float density = Mathf.Lerp(0.45f, 0.02f, t / 0.45f);
            float crackle = r.NextDouble() < density ? Noise(r) : 0f;
            float buzz = (Mathf.Repeat(t * 120f, 1f) * 2f - 1f) * 0.25f;
            return (0.85f * crackle + buzz) * Env(t, 0.16f);
        }

        static float HumWave(float t, System.Random r) =>
            0.22f * Mathf.Sin(2f * Mathf.PI * 60f * t) + 0.12f * Mathf.Sin(2f * Mathf.PI * 120f * t) +
            0.05f * Mathf.Sin(2f * Mathf.PI * 180f * t);

        static float SurgeWave(float t, System.Random r)
        {
            // a relay thunk, a sag, then the mains swelling up through its harmonics and easing off
            float thunk = 0.7f * Mathf.Sin(2f * Mathf.PI * 70f * t) * Env(t, 0.05f) + 0.3f * Noise(r) * Env(t, 0.01f);
            float swell = Mathf.SmoothStep(0f, 1f, (t - 0.2f) / 0.5f) * Mathf.Clamp01((1.6f - t) / 0.9f);
            float f = Mathf.Lerp(48f, 60f, Mathf.Clamp01((t - 0.2f) / 0.5f));
            float phase = 2f * Mathf.PI * f * t;
            float hum = 0.3f * Mathf.Sin(phase) + 0.2f * Mathf.Sin(2f * phase) + 0.1f * Mathf.Sin(3f * phase) + 0.05f * Mathf.Sin(5f * phase);
            float tick = r.NextDouble() < 0.004 * swell ? 0.3f * Noise(r) : 0f;
            return thunk + swell * hum + tick;
        }
    }
}
