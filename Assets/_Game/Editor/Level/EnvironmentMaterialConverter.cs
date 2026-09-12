using System.IO;
using UnityEditor;
using UnityEngine;

namespace HouseOfSilence.EditorTools.Level
{
    /// <summary>
    /// Convertit les materiaux Built-in (Standard, StandardDoubleSide) du pack
    /// d'environnement vers URP/Lit. Sans cela, tout le pack rend en magenta.
    ///
    /// Conversion idempotente : un materiau deja en URP est ignore.
    /// Menu : Tools > House of Silence > Level > Convert Environment Materials To URP
    /// </summary>
    public static class EnvironmentMaterialConverter
    {
        private const string EnvironmentFolder = "Assets/JP Environmental Asset Pack/Materials";
        private const string ModularFolder = "Assets/ModularHousePack1/Art/Materials";

        [MenuItem("Tools/House of Silence/Level/Convert Environment Materials To URP", false, 200)]
        public static void ConvertEnvironmentMaterials()
        {
            int converted = ConvertFolder(EnvironmentFolder);
            converted += ConvertFolder(ModularFolder);
            Debug.Log("[Level] Materiaux convertis vers URP : " + converted);
        }

        /// <summary>Convertit tous les .mat d'un dossier (recursif). Renvoie le nombre convertis.</summary>
        public static int ConvertFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning("[Level] Dossier introuvable : " + folder);
                return 0;
            }

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");

            if (urpLit == null)
            {
                Debug.LogError("[Level] Shader 'Universal Render Pipeline/Lit' introuvable : URP n'est pas installe ?");
                return 0;
            }

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folder });
            int converted = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null || material.shader == null)
                {
                    continue;
                }

                if (Convert(material, urpLit))
                {
                    EditorUtility.SetDirty(material);
                    converted++;
                }
            }

            AssetDatabase.SaveAssets();
            return converted;
        }

        private static bool Convert(Material material, Shader urpLit)
        {
            string shaderName = material.shader.name;

            bool isStandard = shaderName == "Standard" || shaderName == "Standard (Specular setup)";
            bool isDoubleSided = shaderName == "StandardDoubleSide";

            if (!isStandard && !isDoubleSided)
            {
                return false;
            }

            // --- Lecture des anciennes valeurs AVANT de changer de shader ------
            Texture mainTex = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            Vector2 mainScale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
            Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            Texture bumpMap = material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;
            float bumpScale = material.HasProperty("_BumpScale") ? material.GetFloat("_BumpScale") : 1f;
            float glossiness = material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : 0.3f;
            float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
            float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;
            Texture occlusion = material.HasProperty("_OcclusionMap") ? material.GetTexture("_OcclusionMap") : null;

            // Standard : _Mode 0 = Opaque, 1 = Cutout, 2 = Fade, 3 = Transparent.
            float mode = material.HasProperty("_Mode") ? material.GetFloat("_Mode") : 0f;
            bool alphaTest = material.IsKeywordEnabled("_ALPHATEST_ON") || isDoubleSided || Mathf.Approximately(mode, 1f);
            bool transparent = mode >= 1.5f || material.IsKeywordEnabled("_ALPHABLEND_ON") || material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON");

            // --- Bascule vers URP -------------------------------------------
            material.shader = urpLit;

            material.SetTexture("_BaseMap", mainTex);
            material.SetTextureScale("_BaseMap", mainScale);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);

            // La vegetation brille trop avec le lissage Standard : on l'attenue.
            material.SetFloat("_Smoothness", Mathf.Clamp01(glossiness * 0.6f));

            if (bumpMap != null)
            {
                material.SetTexture("_BumpMap", bumpMap);
                material.SetFloat("_BumpScale", bumpScale);
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.DisableKeyword("_NORMALMAP");
            }

            if (occlusion != null)
            {
                material.SetTexture("_OcclusionMap", occlusion);
                material.EnableKeyword("_OCCLUSIONMAP");
            }

            material.SetFloat("_Surface", 0f); // Opaque
            material.SetFloat("_ReceiveShadows", 1f);

            if (transparent)
            {
                // Vitres, rideaux fins : surface transparente avec fondu alpha.
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f); // Alpha
                material.SetFloat("_AlphaClip", 0f);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            }
            else if (alphaTest)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", cutoff);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

                // Feuilles et herbe doivent etre visibles des deux cotes.
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }
            else
            {
                material.SetFloat("_AlphaClip", 0f);
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            }

            // Indispensable pour les details de terrain et les arbres instancies.
            material.enableInstancing = true;

            return true;
        }
    }
}
