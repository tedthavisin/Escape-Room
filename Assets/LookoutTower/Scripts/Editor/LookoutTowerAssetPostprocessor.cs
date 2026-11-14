using System.IO;
using UnityEditor;
using UnityEngine;

namespace LookoutTower.EditorTools
{
    /// <summary>
    /// First-import settings for the lookout tower's textures and models (only applied when a file has no
    /// .meta yet, so anything you change in the inspector afterwards sticks).
    /// </summary>
    class LookoutTowerAssetPostprocessor : AssetPostprocessor
    {
        const string TexDir = "Assets/LookoutTower/Textures/";
        const string ModelDir = "Assets/LookoutTower/Models/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TexDir) || !assetImporter.importSettingsMissing) return;
            var ti = (TextureImporter)assetImporter;
            string f = Path.GetFileNameWithoutExtension(assetPath);
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.maxTextureSize = 1024;
            if (f.EndsWith("_Normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else if (f.EndsWith("_Mask"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
            }
            else if (f.StartsWith("T_Sky"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.wrapModeU = TextureWrapMode.Repeat;
                ti.wrapModeV = TextureWrapMode.Clamp;
                ti.maxTextureSize = 4096;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.anisoLevel = 0;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                if (f.Contains("GlassGrime")) ti.alphaIsTransparency = true;
            }
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelDir) || !assetImporter.importSettingsMissing) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            // Forest assets are light-probe lit (lots of instances); tower + props get lightmap UVs for baking.
            mi.generateSecondaryUV = !assetPath.EndsWith("Forest.fbx");
            mi.isReadable = false;
        }
    }
}
