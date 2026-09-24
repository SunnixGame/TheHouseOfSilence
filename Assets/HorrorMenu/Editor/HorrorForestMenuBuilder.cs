using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HouseOfSilence.Core;
using HouseOfSilence.HorrorMenu;
using HouseOfSilence.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Construit la scene de menu "foret + maison abandonnee" a partir des
    /// assets exportes de Blender (Assets/HorrorMenu/Models et Textures) :
    ///  - reglage des imports de textures (normal maps, alpha...) ;
    ///  - creation des materiaux URP/Lit et remap sur les FBX ;
    ///  - scene : environnement, camera, lune, bougie, brouillard, brume,
    ///    post-process, UI du menu branchee sur MainMenuController.
    ///
    /// Menu : Tools > House of Silence > Build Horror Forest Menu.
    /// Relancer l'outil reconstruit tout (materiaux et scene) depuis zero.
    /// </summary>
    public static class HorrorForestMenuBuilder
    {
        private const string RootFolder = "Assets/HorrorMenu";
        private const string TexturesFolder = RootFolder + "/Textures";
        private const string ModelsFolder = RootFolder + "/Models";
        private const string MaterialsFolder = RootFolder + "/Materials";
        private const string SettingsFolder = RootFolder + "/Settings";
        private const string ScenePath = RootFolder + "/Scenes/MainMenu_Forest.unity";

        private static readonly Color FogColor = new Color(0.1f, 0.12f, 0.155f);

        [MenuItem("Tools/House of Silence/Build Horror Forest Menu", false, 50)]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build();
        }

        public static void Build()
        {
            EnsureFolder(MaterialsFolder);
            EnsureFolder(SettingsFolder);
            EnsureFolder(RootFolder + "/Scenes");

            ConfigureTextures();
            Dictionary<string, Material> materials = CreateMaterials();
            ConfigureModels(materials);
            BuildScene();

            Debug.Log("[HorrorMenu] Scene construite : " + ScenePath);
        }

        // ------------------------------------------------------------------
        // Textures

        private static void ConfigureTextures()
        {
            foreach (string path in Directory.GetFiles(TexturesFolder, "*.png").Select(p => p.Replace('\\', '/')))
            {
                // Textures generees par ce script : leurs reglages sont faits a la creation.
                if (path.EndsWith("_MetalSmooth.png") || path.Contains("/T_UI_") || path.Contains("/T_Rain") || path.EndsWith("/T_MistPuff.png"))
                {
                    continue;
                }

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    continue;
                }

                string file = Path.GetFileNameWithoutExtension(path);
                importer.textureType = file.EndsWith("_Normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = file.EndsWith("_Albedo") || file.EndsWith("_BaseAlpha");
                importer.alphaIsTransparency = file.EndsWith("_BaseAlpha");
                importer.wrapMode = file.StartsWith("T_Grass") || file.StartsWith("T_PineNeedles") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            // URP/Lit lit la smoothness dans l'alpha de la metallic map :
            // on convertit chaque roughness Blender en "MetalSmooth".
            foreach (string path in Directory.GetFiles(TexturesFolder, "*_Roughness.png").Select(p => p.Replace('\\', '/')))
            {
                string outPath = path.Replace("_Roughness.png", "_MetalSmooth.png");
                float metal = path.Contains("RustMetal") ? 0.45f : 0f;

                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                src.LoadImage(File.ReadAllBytes(path));
                Color32[] px = src.GetPixels32();
                byte m = (byte)Mathf.RoundToInt(metal * 255f);

                for (int i = 0; i < px.Length; i++)
                {
                    px[i] = new Color32(m, 0, 0, (byte)(255 - px[i].r));
                }

                var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
                dst.SetPixels32(px);
                File.WriteAllBytes(outPath, dst.EncodeToPNG());
                Object.DestroyImmediate(src);
                Object.DestroyImmediate(dst);

                AssetDatabase.ImportAsset(outPath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(outPath);
                importer.sRGBTexture = false;
                importer.anisoLevel = 8;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            // Texture douce pour les particules de brume.
            string mistPath = TexturesFolder + "/T_MistPuff.png";
            if (!File.Exists(mistPath))
            {
                const int size = 128;
                var mist = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f;
                        float dy = (y + 0.5f) / size * 2f - 1f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float n = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.15f + 7f, y * 0.15f) * 0.4f;
                        float a = Mathf.Clamp01(1f - r);
                        a = a * a * (3f - 2f * a) * Mathf.Lerp(0.5f, 1f, n);
                        mist.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }

                File.WriteAllBytes(mistPath, mist.EncodeToPNG());
                Object.DestroyImmediate(mist);
                AssetDatabase.ImportAsset(mistPath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(mistPath);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static Texture2D Tex(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesFolder + "/" + name + ".png");
        }

        // ------------------------------------------------------------------
        // Materiaux

        private static Dictionary<string, Material> CreateMaterials()
        {
            var mats = new Dictionary<string, Material>();

            mats["M_Bark"] = LitPbr("M_Bark", "Bark", 1.2f);
            mats["M_ForestFloor"] = LitPbr("M_ForestFloor", "ForestFloor", 1f);
            mats["M_Mud"] = LitPbr("M_Mud", "Mud", 1f);
            mats["M_OldWoodPlanks"] = LitPbr("M_OldWoodPlanks", "OldWoodPlanks", 1f);
            mats["M_DarkWood"] = LitPbr("M_DarkWood", "DarkWood", 1f);
            mats["M_RoofShingles"] = LitPbr("M_RoofShingles", "RoofShingles", 1f);
            mats["M_Stone"] = LitPbr("M_Stone", "Stone", 1f);
            mats["M_RustMetal"] = LitPbr("M_RustMetal", "RustMetal", 1f);

            Material needles = NewLit("M_PineNeedles");
            needles.SetTexture("_BaseMap", Tex("T_PineNeedles_BaseAlpha"));
            needles.SetFloat("_Smoothness", 0.25f);
            SetAlphaClip(needles, 0.45f);
            SetDoubleSided(needles);
            mats["M_PineNeedles"] = Save(needles);

            Material grass = NewLit("M_Grass");
            grass.SetTexture("_BaseMap", Tex("T_Grass_Albedo"));
            grass.SetFloat("_Smoothness", 0.2f);
            SetDoubleSided(grass);
            mats["M_Grass"] = Save(grass);

            Material glass = NewLit("M_DirtyGlass");
            glass.SetColor("_BaseColor", new Color(0.06f, 0.06f, 0.05f, 0.5f));
            glass.SetFloat("_Smoothness", 0.85f);
            SetTransparent(glass);
            SetDoubleSided(glass);
            mats["M_DirtyGlass"] = Save(glass);

            Material interior = NewLit("M_InteriorDark");
            interior.SetColor("_BaseColor", new Color(0.07f, 0.06f, 0.05f));
            interior.SetFloat("_Smoothness", 0.05f);
            mats["M_InteriorDark"] = Save(interior);

            Material flame = NewLit("M_LanternFlame");
            flame.SetColor("_BaseColor", Color.black);
            flame.EnableKeyword("_EMISSION");
            flame.SetColor("_EmissionColor", new Color(1f, 0.42f, 0.12f) * 4f);
            flame.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mats["M_LanternFlame"] = Save(flame);

            foreach (Material m in mats.Values)
            {
                ApplyUrpKeywords(m);
                EditorUtility.SetDirty(m);
            }

            AssetDatabase.SaveAssets();
            return mats;
        }

        private static Material LitPbr(string name, string set, float normalStrength)
        {
            Material m = NewLit(name);
            m.SetTexture("_BaseMap", Tex("T_" + set + "_Albedo"));

            Texture2D normal = Tex("T_" + set + "_Normal");
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", normalStrength);
                m.EnableKeyword("_NORMALMAP");
            }

            Texture2D metalSmooth = Tex("T_" + set + "_MetalSmooth");
            if (metalSmooth != null)
            {
                m.SetTexture("_MetallicGlossMap", metalSmooth);
                m.SetFloat("_Smoothness", 1f);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
            }

            return Save(m);
        }

        private static Material NewLit(string name)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");

            if (m == null)
            {
                m = new Material(lit) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = lit;
                m.shaderKeywords = new string[0];
            }

            m.SetColor("_BaseColor", Color.white);
            return m;
        }

        private static Material Save(Material m)
        {
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void SetAlphaClip(Material m, float cutoff)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", cutoff);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
        }

        private static void SetDoubleSided(Material m)
        {
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.doubleSidedGI = true;
        }

        private static void SetTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }

        /// <summary>
        /// Laisse l'editeur URP recalculer les mots-cles (comme si on avait
        /// touche le materiau dans l'Inspector). Silencieux si l'API change.
        /// </summary>
        private static void ApplyUrpKeywords(Material m)
        {
            Type gui = Type.GetType("UnityEditor.BaseShaderGUI, Unity.RenderPipelines.Universal.Editor");
            MethodInfo method = gui == null ? null : gui.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x => x.Name == "SetMaterialKeywords" && x.GetParameters().Length == 3);

            if (method != null)
            {
                try
                {
                    method.Invoke(null, new object[] { m, null, null });
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[HorrorMenu] Mots-cles URP non recalcules pour " + m.name + " : " + e.Message);
                }
            }
        }

        // ------------------------------------------------------------------
        // Modeles

        private static void ConfigureModels(Dictionary<string, Material> materials)
        {
            foreach (string path in Directory.GetFiles(ModelsFolder, "*.fbx").Select(p => p.Replace('\\', '/')))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    continue;
                }

                importer.importCameras = false;
                importer.importLights = false;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.addCollider = false;
                importer.isReadable = false;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.indexFormat = ModelImporterIndexFormat.Auto;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.bakeAxisConversion = true;

                foreach (KeyValuePair<string, Material> kv in materials)
                {
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                }

                importer.SaveAndReimport();
            }
        }

        private static GameObject LoadModel(string file)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(ModelsFolder + "/" + file);
        }

        // ------------------------------------------------------------------
        // Scene

        private static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Environnement
            var env = new GameObject("[Environment]");
            GameObject terrain = Place("HM_Terrain.fbx", env.transform);
            GameObject grass = Place("HM_Grass.fbx", env.transform);
            GameObject forest = Place("HM_Forest.fbx", env.transform);
            GameObject house = Place("HM_House.fbx", env.transform);

            foreach (Renderer r in grass.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
            }

            foreach (GameObject go in new[] { terrain, grass, forest, house })
            {
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                }
            }

            // Reperes exportes de Blender (camera, lune, lumieres).
            Dictionary<string, Vector3> mk = house.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("MK_"))
                .ToDictionary(t => t.name, t => t.position);

            // --- Camera
            var camGo = new GameObject("Menu Camera") { tag = "MainCamera" };
            camGo.transform.position = mk["MK_CamPos"];
            camGo.transform.rotation = Quaternion.LookRotation(mk["MK_CamTarget"] - mk["MK_CamPos"], mk["MK_CamUp"] - mk["MK_CamPos"]);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 43.6f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 250f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            camData.requiresDepthTexture = true;
            camGo.AddComponent<MenuCameraSway>();

            // --- Lumieres
            var lights = new GameObject("[Lighting]");

            Light moon = NewLight("Moon", LightType.Directional, lights.transform, new Color(0.55f, 0.68f, 1f), 1.8f);
            moon.transform.rotation = Quaternion.LookRotation(mk["MK_MoonTo"] - mk["MK_MoonFrom"]);
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.9f;

            Light fill = NewLight("Moon Fill", LightType.Directional, lights.transform, new Color(0.42f, 0.52f, 0.8f), 0.2f);
            fill.transform.rotation = Quaternion.LookRotation(mk["MK_CamTarget"] - mk["MK_CamPos"] + Vector3.down * 4f);
            fill.shadows = LightShadows.None;

            Light candle = NewLight("Interior Candle", LightType.Point, lights.transform, new Color(1f, 0.45f, 0.15f), 3f);
            candle.transform.position = mk["MK_Candle"];
            candle.range = 7f;
            candle.shadows = LightShadows.Soft;
            candle.gameObject.AddComponent<LightFlicker>();

            Light lantern = NewLight("Porch Lantern", LightType.Point, lights.transform, new Color(1f, 0.5f, 0.2f), 1.4f);
            lantern.transform.position = mk["MK_Lantern"];
            lantern.range = 6f;
            lantern.shadows = LightShadows.Soft;
            lantern.gameObject.AddComponent<LightFlicker>();

            // --- Ambiance : pas de ciel, lumiere ambiante froide, brouillard
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.14f, 0.17f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.1f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.025f, 0.028f, 0.032f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.035f;
            RenderSettings.fogColor = FogColor;
            RenderSettings.sun = moon;

            var lighting = new LightingSettings { name = "HM_Lighting", bakedGI = false, realtimeGI = false };
            string lightingPath = SettingsFolder + "/HM_Lighting.lighting";
            AssetDatabase.DeleteAsset(lightingPath);
            AssetDatabase.CreateAsset(lighting, lightingPath);
            Lightmapping.lightingSettings = lighting;

            Vector3 housePos = mk["MK_Lantern"];
            CreateGroundMist(lights.transform, Vector3.Lerp(mk["MK_CamPos"], housePos, 0.55f));
            CreatePostProcess(lights.transform);

            // --- Pluie et orage (sol collisionnable pour les eclaboussures)
            foreach (MeshFilter mf in terrain.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.name == "Terrain")
                {
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
            }

            CreateRain(camGo.transform);

            // Demon au fond, dans la trouee entre l'arbre mort de gauche et la maison.
            Physics.SyncTransforms();
            Ray demonRay = cam.ViewportPointToRay(new Vector3(0.415f, 0.45f, 0f));
            Vector3 flat = demonRay.direction;
            flat.y = 0f;
            Vector3 demonPos = camGo.transform.position + flat.normalized * 22f;
            if (Physics.Raycast(demonPos + Vector3.up * 50f, Vector3.down, out RaycastHit ground, 100f))
            {
                demonPos = ground.point;
            }

            GameObject demon = CreateDemon(env.transform, demonPos, camGo.transform.position, 2.4f);
            LightningStorm storm = CreateStorm(lights.transform, housePos, mk["MK_CamPos"]);
            SetupDemonApparition(demon, storm);

            // --- Noyau du jeu (GameManager, SceneLoader...) + UI
            GameObject core = CoreSetupMenu.EnsureCore();
            CoreSetupMenu.EnsureSystemsHost();
            var gm = new SerializedObject(core.GetComponent<GameManager>());
            SerializedProperty menuName = gm.FindProperty("mainMenuSceneName");
            if (menuName != null)
            {
                menuName.stringValue = Path.GetFileNameWithoutExtension(ScenePath);
            }

            // "Nouvelle partie" lance la grande foret.
            SerializedProperty gameplayName = gm.FindProperty("gameplaySceneName");
            if (gameplayName != null)
            {
                gameplayName.stringValue = HouseOfSilence.EditorTools.Level.ForestLandscapeBuilder.SceneName;
            }

            gm.ApplyModifiedPropertiesWithoutUndo();

            BuildMenuUi();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
        }

        private static GameObject Place(string file, Transform parent)
        {
            GameObject asset = LoadModel(file);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Light NewLight(string name, LightType type, Transform parent, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            go.AddComponent<UniversalAdditionalLightData>();
            return light;
        }

        private static void CreateGroundMist(Transform parent, Vector3 center)
        {
            var go = new GameObject("Ground Mist");
            go.transform.SetParent(parent, false);
            go.transform.position = center + Vector3.up * 0.6f;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 20f;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.55f, 0.6f, 0.7f, 0.07f);
            main.maxParticles = 140;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 6f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(36f, 0.8f, 36f);

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;

            string matPath = MaterialsFolder + "/M_GroundMist.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }

            mat.shader = shader;
            mat.SetTexture("_BaseMap", Tex("T_MistPuff"));
            mat.SetColor("_BaseColor", Color.white);
            SetTransparent(mat);
            mat.SetFloat("_SoftParticlesEnabled", 1f);
            mat.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            mat.SetFloat("_SoftParticlesFarFadeDistance", 2f);
            mat.SetVector("_SoftParticleFadeParams", new Vector4(0f, 0.5f, 0f, 0f));
            mat.EnableKeyword("_SOFTPARTICLES_ON");
            mat.SetFloat("_CameraFadingEnabled", 1f);
            mat.SetFloat("_CameraNearFadeDistance", 1f);
            mat.SetFloat("_CameraFarFadeDistance", 4f);
            mat.SetVector("_CameraFadeParams", new Vector4(1f, 1f / 3f, 0f, 0f));
            mat.EnableKeyword("_FADING_ON");
            EditorUtility.SetDirty(mat);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingFudge = 10f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
        }

        private static Material ParticleMaterial(string name, Texture2D tex, Color color, bool additive)
        {
            string matPath = MaterialsFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }

            mat.shader = shader;
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", color);
            SetTransparent(mat);

            if (additive)
            {
                mat.SetFloat("_Blend", 2f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.One);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D GeneratedTexture(string name, int w, int h, Func<float, float, float> alpha)
        {
            string path = TexturesFolder + "/" + name + ".png";
            if (!File.Exists(path))
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w;
                        float v = (y + 0.5f) / h;
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(u, v))));
                    }
                }

                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            return Tex(name);
        }

        private static void CreateRain(Transform camera)
        {
            // Goutte : trait fin, plus dense au centre, fondu aux extremites.
            Texture2D streak = GeneratedTexture("T_RainStreak", 16, 128, (u, v) =>
            {
                float across = 1f - Mathf.Abs(u * 2f - 1f);
                float along = Mathf.Sin(v * Mathf.PI);
                return Mathf.Pow(across, 3f) * Mathf.Pow(along, 0.7f);
            });
            Texture2D dot = GeneratedTexture("T_RainSplash", 32, 32, (u, v) =>
            {
                float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                return Mathf.Pow(Mathf.Clamp01(1f - r), 2f);
            });

            var go = new GameObject("Rain");
            go.transform.SetParent(camera, false);
            go.transform.localPosition = new Vector3(0f, 14f, 10f);
            go.transform.localRotation = Quaternion.identity;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 5f;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSizeZ = 1f;
            main.startColor = new Color(0.72f, 0.78f, 0.88f, 0.28f);
            main.maxParticles = 12000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 6000f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(36f, 1f, 34f);
            // Le parent (camera) est incline : on garde la boite a l'horizontale.
            shape.rotation = new Vector3(-camera.eulerAngles.x, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-14f, -11f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            ParticleSystem.CollisionModule collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.lifetimeLoss = 1f;
            collision.bounce = 0f;
            collision.radiusScale = 0.1f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.035f;
            renderer.lengthScale = 1f;
            renderer.sharedMaterial = ParticleMaterial("M_Rain", streak, Color.white, false);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Eclaboussures au sol, emises a chaque impact.
            var splashGo = new GameObject("Rain Splashes");
            splashGo.transform.SetParent(go.transform, false);
            var splash = splashGo.AddComponent<ParticleSystem>();
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule sm = splash.main;
            sm.loop = false;
            sm.playOnAwake = false;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            sm.startColor = new Color(0.75f, 0.8f, 0.9f, 0.35f);
            sm.gravityModifier = 1.2f;
            sm.maxParticles = 4000;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule se = splash.emission;
            se.rateOverTime = 0f;
            se.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 3) });

            ParticleSystem.ShapeModule ss = splash.shape;
            ss.shapeType = ParticleSystemShapeType.Hemisphere;
            ss.radius = 0.02f;
            ss.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.ColorOverLifetimeModule sc = splash.colorOverLifetime;
            sc.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            sc.color = fade;

            var sr = splashGo.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = ParticleMaterial("M_RainSplash", dot, Color.white, false);
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;

            ParticleSystem.SubEmittersModule sub = ps.subEmitters;
            sub.enabled = true;
            sub.AddSubEmitter(splash, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);

            // Regle en dernier : le reglage du sous-emetteur peut ecraser celui du parent.
            main.playOnAwake = true;
            ps.Play();
        }

        private static LightningStorm CreateStorm(Transform parent, Vector3 housePos, Vector3 camPos)
        {
            var storm = new GameObject("Storm");
            storm.transform.SetParent(parent, false);

            // Zone des arcs : derriere la maison, dans l'axe de la camera. Pas trop
            // loin, sinon le brouillard exponentiel les efface completement.
            Vector3 dir = housePos - camPos;
            dir.y = 0f;
            dir.Normalize();
            var area = new GameObject("Bolt Area");
            area.transform.SetParent(storm.transform, false);
            area.transform.position = housePos + dir * 12f;

            Light flash = NewLight("Lightning Flash", LightType.Directional, storm.transform, new Color(0.75f, 0.82f, 1f), 0f);
            flash.transform.rotation = Quaternion.LookRotation(-dir + Vector3.down * 0.9f);
            flash.shadows = LightShadows.Soft;

            var boltGo = new GameObject("Lightning Bolt");
            boltGo.transform.SetParent(storm.transform, false);
            var line = boltGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 0.6f;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f));
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            string boltMatPath = MaterialsFolder + "/M_LightningBolt.mat";
            var boltMat = AssetDatabase.LoadAssetAtPath<Material>(boltMatPath);
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (boltMat == null)
            {
                boltMat = new Material(unlit);
                AssetDatabase.CreateAsset(boltMat, boltMatPath);
            }

            boltMat.shader = unlit;
            boltMat.SetColor("_BaseColor", new Color(0.85f, 0.9f, 1f) * 6f);
            EditorUtility.SetDirty(boltMat);
            line.sharedMaterial = boltMat;

            var lightning = storm.AddComponent<LightningStorm>();
            var ls = new SerializedObject(lightning);
            ls.FindProperty("flashLight").objectReferenceValue = flash;
            ls.FindProperty("bolt").objectReferenceValue = line;
            ls.FindProperty("boltArea").objectReferenceValue = area.transform;
            ls.ApplyModifiedPropertiesWithoutUndo();

            // Tonnerre : thunder_menu_01 puis _02, en boucle infinie.
            var audioGo = new GameObject("Thunder Audio");
            audioGo.transform.SetParent(storm.transform, false);
            var playlist = audioGo.AddComponent<ThunderPlaylist>();
            var ps = new SerializedObject(playlist);
            SerializedProperty clips = ps.FindProperty("clips");
            string[] clipPaths = { "Assets/_Game/Audio/thunder_menu_01.mp3", "Assets/_Game/Audio/thunder_menu_02.mp3" };
            clips.arraySize = clipPaths.Length;
            for (int i = 0; i < clipPaths.Length; i++)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPaths[i]);
                if (clip == null)
                {
                    Debug.LogWarning("[HorrorMenu] Son introuvable : " + clipPaths[i]);
                }

                clips.GetArrayElementAtIndex(i).objectReferenceValue = clip;
            }

            ps.FindProperty("lightning").objectReferenceValue = lightning;
            ps.ApplyModifiedPropertiesWithoutUndo();

            CreateRainAudio(storm.transform);
            return lightning;
        }

        // ------------------------------------------------------------------
        // Demon au fond de la scene

        private const string DemonPrefabPath = "Assets/DemonDoll/Prefab/SKM_DemonDoll_Var1.prefab";
        private const string DemonMaterialsFolder = MaterialsFolder + "/Demon";
        private static readonly Color DemonEyeColor = new Color(1f, 0.02f, 0.01f);

        /// <summary>
        /// Place la DemonDoll agrandie au fond de la scene, tournee vers la camera,
        /// avec des yeux rouges lumineux. Les materiaux d'origine (shader Built-in
        /// Standard, rose sous URP) ne sont pas modifies : on utilise des copies URP.
        /// </summary>
        public static GameObject CreateDemon(Transform parent, Vector3 groundPosition, Vector3 lookAt, float scale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemonPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[HorrorMenu] Prefab introuvable : " + DemonPrefabPath);
                return null;
            }

            EnsureFolder(DemonMaterialsFolder);

            var demon = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            demon.name = "Demon";
            demon.transform.SetParent(parent, false);
            demon.transform.position = groundPosition;
            Vector3 face = lookAt - groundPosition;
            face.y = 0f;
            demon.transform.rotation = Quaternion.LookRotation(face.normalized);
            demon.transform.localScale = Vector3.one * scale;

            var converted = new Dictionary<Material, Material>();
            foreach (Renderer renderer in demon.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                    {
                        continue;
                    }

                    if (!converted.TryGetValue(mats[i], out Material urp))
                    {
                        urp = ConvertDemonMaterial(mats[i]);
                        converted[mats[i]] = urp;
                    }

                    mats[i] = urp;
                }

                renderer.sharedMaterials = mats;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    // Pas d'animation : evite que la poupee disparaisse hors des bounds.
                    skinned.updateWhenOffscreen = false;
                }
            }

            // Lueur rouge sur le visage, portee courte.
            Transform head = demon.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head");
            var glowGo = new GameObject("Eyes Glow");
            glowGo.transform.SetParent(head != null ? head : demon.transform, false);
            glowGo.transform.position = (head != null ? head.position : groundPosition + Vector3.up * 0.9f * scale)
                + demon.transform.forward * 0.12f * scale + Vector3.up * 0.06f * scale;
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = DemonEyeColor;
            glow.intensity = 0.5f;
            glow.range = 0.35f * scale;
            glow.shadows = LightShadows.None;
            glowGo.AddComponent<UniversalAdditionalLightData>();

            foreach (Transform t in demon.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }

            // Seule la tete flottante est gardee (corps, robe, chaussures masques).
            foreach (Renderer renderer in demon.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != "SK_Head" && renderer.name != "SK_Hair")
                {
                    renderer.gameObject.SetActive(false);
                }
            }

            return demon;
        }

        private const string CryingPath = "Assets/_Game/Audio/woman-crying.mp3";

        /// <summary>
        /// Demon cache au lancement, revele au premier eclair ; les pleurs
        /// demarrent a ce moment-la, en boucle.
        /// </summary>
        public static void SetupDemonApparition(GameObject demon, LightningStorm storm)
        {
            if (demon == null)
            {
                return;
            }

            var cryingGo = new GameObject("Crying Audio");
            cryingGo.transform.SetParent(demon.transform, false);
            cryingGo.transform.localPosition = Vector3.up * 0.9f;
            var crying = cryingGo.AddComponent<AudioSource>();
            crying.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(CryingPath);
            crying.loop = true;
            crying.playOnAwake = false;
            crying.volume = 0.6f;
            // Legerement spatialise (vient de la gauche) sans s'attenuer avec la distance.
            crying.spatialBlend = 0.35f;
            crying.rolloffMode = AudioRolloffMode.Linear;
            crying.minDistance = 40f;
            crying.maxDistance = 60f;
            if (crying.clip == null)
            {
                Debug.LogWarning("[HorrorMenu] Son introuvable : " + CryingPath);
            }

            DemonApparition apparition = demon.GetComponent<DemonApparition>();
            if (apparition == null)
            {
                apparition = demon.AddComponent<DemonApparition>();
            }

            var so = new SerializedObject(apparition);
            so.FindProperty("lightning").objectReferenceValue = storm;
            so.FindProperty("crying").objectReferenceValue = crying;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material ConvertDemonMaterial(Material src)
        {
            string path = DemonMaterialsFolder + "/" + src.name + "_URP.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (m == null)
            {
                m = new Material(lit);
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = lit;
            m.shaderKeywords = new string[0];

            Texture baseMap = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);

            Texture normal = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }

            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", src.name.Contains("Eyes") ? 0.9f : 0.35f);

            bool cutout = src.IsKeywordEnabled("_ALPHATEST_ON") || (src.HasProperty("_Mode") && Mathf.Approximately(src.GetFloat("_Mode"), 1f))
                || src.name.Contains("Lashes") || src.name.Contains("Hair");
            if (cutout)
            {
                SetAlphaClip(m, src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0.5f);
            }

            if (src.shader.name.Contains("Double") || src.name.Contains("Lashes") || src.name.Contains("Hair") || src.name.Contains("Dress"))
            {
                SetDoubleSided(m);
            }

            if (src.name.Contains("Eyes"))
            {
                // Yeux : iris rouge et tres lumineux (HDR) pour percer le brouillard.
                m.SetColor("_BaseColor", new Color(0.25f, 0.01f, 0.01f));
                m.SetTexture("_EmissionMap", src.HasProperty("_EmissionMap") ? src.GetTexture("_EmissionMap") : null);
                m.SetColor("_EmissionColor", DemonEyeColor * 8f);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            ApplyUrpKeywords(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        private const string RainLoopPath = "Assets/_Game/Audio/rain-loop.mp3";

        /// <summary>Ambiance de pluie 2D jouee en boucle tant que la scene du menu est chargee.</summary>
        public static AudioSource CreateRainAudio(Transform parent)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(RainLoopPath);
            if (clip == null)
            {
                Debug.LogWarning("[HorrorMenu] Son introuvable : " + RainLoopPath);
                return null;
            }

            var go = new GameObject("Rain Audio");
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 0f;
            source.volume = 0.55f;
            source.priority = 64;
            return source;
        }

        private static void CreatePostProcess(Transform parent)
        {
            string path = SettingsFolder + "/HM_PostProcess.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            Tonemapping tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);

            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.25f);
            color.contrast.Override(8f);
            color.saturation.Override(-20f);
            color.colorFilter.Override(new Color(0.86f, 0.92f, 1f));

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.75f);

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.36f);
            vignette.smoothness.Override(0.5f);

            FilmGrain grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.3f);

            ChromaticAberration ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.12f);

            foreach (VolumeComponent c in profile.components)
            {
                AssetDatabase.AddObjectToAsset(c, profile);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Post Process");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        // ------------------------------------------------------------------
        // UI

        private static void BuildMenuUi()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("Menu Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Degrade sombre a gauche pour la lisibilite du texte.
            Image shade = NewUi<Image>("Left Shade", canvasGo.transform);
            Stretch(shade.rectTransform, new Vector2(0f, 0f), new Vector2(0.55f, 1f));
            shade.sprite = CreateGradientSprite();
            shade.color = new Color(0f, 0f, 0f, 0.75f);
            shade.raycastTarget = false;

            Text title = NewText("Title", canvasGo.transform, font, "THE HOUSE\nOF SILENCE", 104, new Color(0.62f, 0.05f, 0.04f));
            Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(140f, 190f), new Vector2(1000f, 260f));
            title.lineSpacing = 0.9f;
            title.fontStyle = FontStyle.Bold;
            var shadow = title.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(4f, -4f);

            var menuGo = new GameObject("Main Menu", typeof(RectTransform));
            menuGo.transform.SetParent(canvasGo.transform, false);
            var controller = menuGo.AddComponent<MainMenuController>();
            Stretch((RectTransform)menuGo.transform, Vector2.zero, Vector2.one);

            string[] labels = { "Nouvelle partie", "Continuer", "Options", "Quitter" };
            bool[] enabled = { true, false, false, true };
            var buttons = new Button[labels.Length];

            for (int i = 0; i < labels.Length; i++)
            {
                buttons[i] = NewMenuItem(menuGo.transform, font, labels[i], new Vector2(140f, -40f - i * 78f), enabled[i]);
            }

            Text version = NewText("Version", canvasGo.transform, font, "", 18, new Color(0.5f, 0.5f, 0.5f, 0.6f));
            Anchor(version.rectTransform, new Vector2(1f, 0f), new Vector2(-30f, 24f), new Vector2(700f, 30f));
            version.rectTransform.pivot = new Vector2(1f, 0f);
            version.alignment = TextAnchor.LowerRight;

            var so = new SerializedObject(controller);
            so.FindProperty("playButton").objectReferenceValue = buttons[0];
            so.FindProperty("quitButton").objectReferenceValue = buttons[3];
            so.FindProperty("versionText").objectReferenceValue = version;
            so.ApplyModifiedPropertiesWithoutUndo();

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static Button NewMenuItem(Transform parent, Font font, string label, Vector2 pos, bool interactable)
        {
            Image hit = NewUi<Image>("Item - " + label, parent);
            hit.color = new Color(0f, 0f, 0f, 0f);
            Anchor(hit.rectTransform, new Vector2(0f, 0.5f), pos, new Vector2(520f, 64f));

            var button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.interactable = interactable;
            button.targetGraphic = hit;

            Text selector = NewText("Selector", hit.transform, font, "—", 42, new Color(0.75f, 0.06f, 0.04f, 0f));
            Anchor(selector.rectTransform, new Vector2(0f, 0.5f), new Vector2(-62f, 0f), new Vector2(60f, 64f));

            Text text = NewText("Label", hit.transform, font, label, 42, new Color(0.72f, 0.7f, 0.66f));
            Anchor(text.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(520f, 64f));

            var item = hit.gameObject.AddComponent<HorrorMenuItem>();
            var so = new SerializedObject(item);
            so.FindProperty("label").objectReferenceValue = text;
            so.FindProperty("selector").objectReferenceValue = selector;
            so.ApplyModifiedPropertiesWithoutUndo();

            return button;
        }

        private static T NewUi<T>(string name, Transform parent) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        private static Text NewText(string name, Transform parent, Font font, string content, int size, Color color)
        {
            Text t = NewUi<Text>(name, parent);
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Sprite CreateGradientSprite()
        {
            string path = TexturesFolder + "/T_UI_LeftShade.png";
            if (!File.Exists(path))
            {
                var tex = new Texture2D(256, 4, TextureFormat.RGBA32, false);
                for (int x = 0; x < 256; x++)
                {
                    float a = 1f - x / 255f;
                    a = a * a * (3f - 2f * a);
                    for (int y = 0; y < 4; y++)
                    {
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }

                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------------

        private static void AddSceneToBuild(string path)
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path))
            {
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
