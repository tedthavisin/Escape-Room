using UnityEngine;

namespace LookoutTower
{
    /// <summary>A one-off burst of sparks with a blue-white flash. Cleans itself up.</summary>
    [AddComponentMenu("")]
    public class SparkFx : MonoBehaviour
    {
        Light _flash;
        float _born;

        /// <summary>Sparks at a point, spraying along <paramref name="direction"/>. Material: an additive particle material.</summary>
        public static void Burst(Vector3 position, Vector3 direction, Material material, int count = 26)
        {
            if (!Application.isPlaying) return;
            var go = new GameObject("Sparks");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction.sqrMagnitude > 1e-6f ? direction : Vector3.forward));
            var fx = go.AddComponent<SparkFx>();
            fx._born = Time.time;

            if (material != null)
            {
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.duration = 0.3f;
                main.loop = false;
                main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 2.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.008f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.8f), new Color(1f, 0.6f, 0.2f));
                main.gravityModifier = 1.3f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 64;
                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count), new ParticleSystem.Burst(0.08f, (short)(count / 2)) });
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 38f;
                shape.radius = 0.004f;
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.6f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.035f;
                r.lengthScale = 1.5f;
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                ps.Play();
            }

            var lg = new GameObject("Flash");
            lg.transform.SetParent(go.transform, false);
            lg.transform.localPosition = Vector3.forward * 0.03f;
            fx._flash = lg.AddComponent<Light>();
            fx._flash.type = LightType.Point;
            fx._flash.color = new Color(0.75f, 0.85f, 1f);
            fx._flash.range = 2.5f;
            fx._flash.intensity = 3f;
            fx._flash.shadows = LightShadows.None;
            Destroy(go, 1.2f);
        }

        void Update()
        {
            if (_flash == null) return;
            float t = Time.time - _born;
            _flash.intensity = t < 0.12f ? 3f * (0.6f + 0.4f * Mathf.PerlinNoise(t * 90f, 0f)) : Mathf.Max(0f, 3f * (1f - (t - 0.12f) / 0.1f));
            if (_flash.intensity <= 0f) _flash.enabled = false;
        }
    }
}
