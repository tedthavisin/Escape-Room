using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Fades the player's view to black with a quad held just in front of the camera. Works the same on a monitor and
    /// in a headset (the quad is wider than any field of view). Used by the time's-up and win screens.
    /// </summary>
    public static class ViewFader
    {
        /// <summary>Puts a fully transparent black quad in front of <paramref name="head"/>; raise it with <see cref="SetAlpha"/>.</summary>
        public static Material Create(Transform head, Material template, out GameObject root, float distance = 0.3f)
        {
            root = new GameObject("View Fade");
            root.transform.SetParent(head, false);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, distance);
            quad.transform.localScale = new Vector3(distance * 8f, distance * 8f, 1f);
            var mat = MakeMaterial(template, Color.black);
            mat.renderQueue = 3999;
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetAlpha(mat, 0f);
            return mat;
        }

        /// <summary>A copy of <paramref name="template"/> (an unlit transparent material), or a URP Unlit one, in <paramref name="colour"/>.</summary>
        public static Material MakeMaterial(Material template, Color colour)
        {
            Material m;
            if (template != null) m = new Material(template);
            else
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            return m;
        }

        public static void SetAlpha(Material m, float a)
        {
            if (m == null || !m.HasProperty("_BaseColor")) return;
            var c = m.GetColor("_BaseColor");
            c.a = a;
            m.SetColor("_BaseColor", c);
        }
    }
}
