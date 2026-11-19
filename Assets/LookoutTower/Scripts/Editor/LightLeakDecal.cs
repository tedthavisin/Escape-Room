using System.IO;
using UnityEditor;
using UnityEngine;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// Bakes the texture for <see cref="LightLeakReveal"/>'s ceiling decal (drawn by <see cref="LightLeakPattern"/>).
    /// LightLeakReveal draws its own, bigger pattern at start by default (drawPattern); this baked one is the fallback.
    /// </summary>
    public static class LightLeakDecal
    {
        /// <param name="path">Asset path of the PNG to write.</param>
        /// <param name="quad">The decal quad (built-in Quad mesh, scaled to size), already placed and turned.</param>
        /// <param name="readRight">World direction that reads left to right for someone looking up at it.</param>
        /// <param name="readUp">World direction that is the top of the characters.</param>
        /// <param name="code">Characters to punch, e.g. "___9".</param>
        /// <param name="pitch">Hole spacing, metres.</param>
        public static Texture2D Build(string path, Transform quad, Vector3 readRight, Vector3 readUp, string code,
                                      float pitch = 0.022f, int width = 512, int height = 256)
        {
            var px = LightLeakPattern.Render(quad, readRight, readUp, code, pitch, width, height, 0.8f);

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
