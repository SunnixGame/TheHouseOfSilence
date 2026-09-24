using System.Collections.Generic;
using System.IO;
using HouseOfSilence.Core;
using HouseOfSilence.Doors;
using HouseOfSilence.EditorTools.UI;
using HouseOfSilence.Interaction;
using HouseOfSilence.Level;
using HouseOfSilence.Player;
using HouseOfSilence.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace HouseOfSilence.EditorTools.Level
{
    /// <summary>
    /// Construit la grande foret 2 km x 2 km a partir des fichiers exportes par Blender
    /// (Assets/_Game/Art/Landscape) : relief 16 bits, masques des clairieres et des
    /// chemins, JSON des clairieres. Place les textures, les arbres, l'herbe, un repere
    /// par clairiere pour y poser les habitations, l'eglise (porte ouvrable), la carte 3D,
    /// la nuit et un joueur limite au deplacement + interaction (aucun autre systeme
    /// de gameplay), puis branche
    /// "Nouvelle partie" du menu sur cette scene.
    ///
    /// Tout est deterministe (graine fixe) : relancer l'outil redonne la meme foret.
    /// </summary>
    public static class ForestLandscapeBuilder
    {
        private const string LandscapeFolder = "Assets/_Game/Art/Landscape/";
        private const string HeightPath = LandscapeFolder + "Forest2k_Height.raw";
        private const string ClearingsPath = LandscapeFolder + "Forest2k_Clearings.raw";
        private const string PathsPath = LandscapeFolder + "Forest2k_Paths.raw";
        private const string MetaPath = LandscapeFolder + "Forest2k_Meta.json";

        public const string ScenePath = "Assets/_Game/Scenes/Forest_Landscape_2k.unity";
        public const string SceneName = "Forest_Landscape_2k";
        private const string MainMenuScenePath = "Assets/HorrorMenu/Scenes/MainMenu_Forest.unity";
        private const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";
        private const string FlashlightAudioFolder = "Assets/_Game/Audio/Flashlight/";
        private const string ForestSkyPath = "Assets/_Game/Materials/M_NightSky_Forest.mat";
        private const string TerrainDataPath = "Assets/_Game/Scenes/Forest_Landscape_2k_Terrain.asset";
        private const string LayersFolder = "Assets/_Game/Scenes/TerrainLayers/";
        private const string TreeVariantsFolder = "Assets/_Game/Prefabs/Trees/";
        private const string JpPrefabs = "Assets/JP Environmental Asset Pack/Prefabs/";

        // --- Reglages de la foret ---------------------------------------------

        /// <summary>Espacement de la grille d'arbres (avec gigue). 5.5 m = foret dense.</summary>
        private const float TreeSpacing = 5.5f;

        /// <summary>Au-dela de cette pente (degres), plus d'arbres : falaises et berges nues.</summary>
        private const float MaxTreeSlope = 38f;

        /// <summary>Pas d'arbre a moins de cette distance (m) de l'axe d'un chemin.</summary>
        private const float PathTreeClearance = 4.5f;

        /// <summary>Demi-largeur (m) de la terre battue du chemin.</summary>
        private const float PathHalfWidth = 1.8f;

        private const int Seed = 2049;

        [System.Serializable]
        private class ClearingInfo
        {
            public string name;
            public float x;
            public float z;
            public float y;
            public float radius;
        }

        [System.Serializable]
        private class LandscapeMeta
        {
            public int resolution;
            public float size;
            public float heightRange;
            public float pathMaskMeters = 8f;
            public ClearingInfo[] clearings;
        }

        [MenuItem("Tools/House of Silence/Level/Build Forest Landscape 2k", false, 220)]
        public static void BuildForestLandscape()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Level] Arrete le Play Mode avant de construire la foret.");
                return;
            }

            if (!File.Exists(HeightPath) || !File.Exists(MetaPath))
            {
                Debug.LogError("[Level] Fichiers Blender introuvables dans " + LandscapeFolder);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            LandscapeMeta meta = JsonUtility.FromJson<LandscapeMeta>(File.ReadAllText(MetaPath));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Transform environment = new GameObject("--- Environment ---").transform;
            Transform lighting = new GameObject("--- Lighting ---").transform;

            Terrain terrain = BuildTerrain(environment, meta);
            Dictionary<string, string> builtSites = new Dictionary<string, string>();
            PlaceBuildings(terrain, meta, environment, builtSites);
            CreateClearingMarkers(meta);
            ForestLocations locations = EnsureLocations(meta, builtSites);
            BuildMap3D(terrain, meta, environment, locations);

            // Nuit du prototype (lune, brouillard, post-traitement), assombrie pour la foret :
            // sans lampe, on ne distingue plus que des silhouettes.
            PrototypeLevelBuilder.SetupNight(lighting);
            DarkenNight(lighting);
            CreateAmbience();

            // Noyau, menu pause et joueur (deplacement seul) au centre de la clairiere principale.
            SetupPlayer(terrain, meta);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            RegisterInBuildSettings();
            PointMainMenuToForest();

            Debug.Log("[Level] Foret 2k construite et sauvegardee : " + ScenePath);
        }

        // ------------------------------------------------------------------
        // Joueur et menu
        // ------------------------------------------------------------------

        private static void SetupPlayer(Terrain terrain, LandscapeMeta meta)
        {
            Vector3 spawn = new Vector3(meta.size * 0.5f, 0f, meta.size * 0.5f);

            if (meta.clearings != null && meta.clearings.Length > 0)
            {
                spawn = new Vector3(meta.clearings[0].x, 0f, meta.clearings[0].z);
            }

            spawn.y = terrain.SampleHeight(spawn) + terrain.transform.position.y + 0.15f;

            // Carte d'exploration : seulement le noyau (etat de jeu, curseur, pause)
            // et le deplacement. Aucun systeme de gameplay (objectifs, lumieres,
            // horreur, peur, inventaire, interaction, vie, endurance, HUD).
            GameObject core = CoreSetupMenu.EnsureCore();
            SetSceneNames(core.GetComponent<GameManager>());

            // Echap met en pause : sans menu, le jeu resterait fige.
            MenuSetupMenu.AddPauseMenu();

            CreateMovementOnlyPlayer(spawn);
        }

        /// <summary>
        /// Joueur FPS minimal : CharacterController, lecture des entrees, deplacement
        /// (marche, course, saut, accroupi) et camera. Sans PlayerStamina le sprint est illimite.
        /// </summary>
        private static void CreateMovementOnlyPlayer(Vector3 position)
        {
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = position;

            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.65f, 0f);

            GameObject cameraObject = new GameObject("PlayerCamera");
            cameraObject.transform.SetParent(pivot.transform, false);
            cameraObject.tag = "MainCamera";

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.35f;
            controller.skinWidth = 0.02f;
            controller.minMoveDistance = 0f;

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 220f; // pas plus loin que le brouillard
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = RenderSettings.fogColor;
            cameraObject.AddComponent<AudioListener>();

            InputReader input = player.AddComponent<InputReader>();
            PlayerMotor motor = player.AddComponent<PlayerMotor>();
            PlayerLook look = player.AddComponent<PlayerLook>();
            PlayerCharacter character = player.AddComponent<PlayerCharacter>();

            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);

            if (actions == null)
            {
                Debug.LogWarning("[Level] " + InputAssetPath + " introuvable : assigne l'InputActionAsset a la main sur l'InputReader.");
            }
            else
            {
                EditorSetupUtility.SetObjectField(input, "inputActions", actions);
            }

            EditorSetupUtility.SetObjectField(look, "cameraPivot", pivot.transform);
            EditorSetupUtility.SetObjectField(look, "input", input);
            EditorSetupUtility.SetObjectField(motor, "input", input);
            EditorSetupUtility.SetObjectField(motor, "cameraPivot", pivot.transform);
            EditorSetupUtility.SetObjectField(character, "playerCamera", camera);
            EditorSetupUtility.SetObjectField(character, "cameraPivot", pivot.transform);

            // Seule exception au "deplacement seul" : l'interaction, pour ouvrir les portes
            // (eglise). Invite "Ouvrir [E]" en IMGUI, comme dans le prototype.
            PlayerInteractor interactor = player.AddComponent<PlayerInteractor>();
            EditorSetupUtility.SetObjectField(interactor, "player", character);
            EditorSetupUtility.SetObjectField(interactor, "input", input);
            EditorSetupUtility.SetObjectField(interactor, "rayOrigin", cameraObject.transform);

            new GameObject("[HUD]").AddComponent<InteractionPromptOverlay>();

            // Lampe torche (F) : tenue en main droite, un peu sous le regard.
            GameObject lampObject = new GameObject("Flashlight");
            lampObject.transform.SetParent(pivot.transform, false);
            lampObject.transform.localPosition = new Vector3(0.22f, -0.28f, 0.25f);

            Light lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.color = new Color(1f, 0.93f, 0.8f);   // ampoule blanc chaud
            lamp.intensity = 60f;   // URP lineaire : attenuation en 1/d², il faut une forte valeur
            lamp.range = 40f;
            lamp.spotAngle = 58f;
            lamp.innerSpotAngle = 26f;
            lamp.shadows = LightShadows.Soft;
            lamp.shadowStrength = 0.9f;
            lamp.shadowNearPlane = 0.2f;

            // Clic de l'interrupteur (sons synthetises, Assets/_Game/Audio/Flashlight).
            AudioSource click = lampObject.AddComponent<AudioSource>();
            click.playOnAwake = false;
            click.spatialBlend = 0f; // dans la main : pas de spatialisation

            Flashlight flashlight = player.AddComponent<Flashlight>();
            EditorSetupUtility.SetObjectField(flashlight, "spot", lamp);
            EditorSetupUtility.SetObjectField(flashlight, "aim", cameraObject.transform);
            EditorSetupUtility.SetObjectField(flashlight, "input", input);
            EditorSetupUtility.SetObjectField(flashlight, "motor", motor);
            EditorSetupUtility.SetObjectField(flashlight, "audioSource", click);
            EditorSetupUtility.SetObjectField(flashlight, "onClip", AssetDatabase.LoadAssetAtPath<AudioClip>(FlashlightAudioFolder + "Flashlight_On.wav"));
            EditorSetupUtility.SetObjectField(flashlight, "offClip", AssetDatabase.LoadAssetAtPath<AudioClip>(FlashlightAudioFolder + "Flashlight_Off.wav"));

            AddFootsteps(player, character, motor);
        }

        // ------------------------------------------------------------------
        // Sons : pas et ambiance
        // ------------------------------------------------------------------

        private const string FootstepsPack = "Assets/Footsteps - Essentials/";
        private const string ForestAmbiencePath = "Assets/_Game/Audio/horror/forest-ambience.mp3";

        /// <summary>Surfaces du pack "Footsteps - Essentials" utilisees par le jeu.</summary>
        private static readonly string[] FootstepSurfaces = { "Grass", "Leaves", "DirtyGround", "Gravel", "Mud", "Rock", "Tile", "Wood", "Metal" };

        /// <summary>Couches du terrain de la foret -> surface.</summary>
        private static readonly string[,] TerrainSurfaceRules =
        {
            { "TL_Grass", "Grass" },
            { "TL_DeadLeaves", "Leaves" },
            { "TL_ForestFloor", "Leaves" },     // litiere de foret : feuilles et brindilles
            { "TL_DirtPath", "DirtyGround" },
        };

        /// <summary>Mot du nom de materiau -> surface (eglise, futures habitations).</summary>
        private static readonly string[,] MaterialSurfaceRules =
        {
            { "Tile", "Tile" },
            { "Stone", "Rock" },
            { "Concrete", "Rock" },
            { "Wood", "Wood" },
            { "Metal", "Metal" },
            { "Ground", "DirtyGround" },
        };

        private static void AddFootsteps(GameObject player, PlayerCharacter character, PlayerMotor motor)
        {
            GameObject feet = new GameObject("Footsteps");
            feet.transform.SetParent(player.transform, false);
            AudioSource source = feet.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // ses propres pas : pas de spatialisation

            PlayerFootstepAudio footsteps = player.AddComponent<PlayerFootstepAudio>();
            SerializedObject so = new SerializedObject(footsteps);
            so.FindProperty("player").objectReferenceValue = character;
            so.FindProperty("motor").objectReferenceValue = motor;
            so.FindProperty("source").objectReferenceValue = source;

            SerializedProperty surfaces = so.FindProperty("surfaces");
            surfaces.arraySize = FootstepSurfaces.Length;

            for (int i = 0; i < FootstepSurfaces.Length; i++)
            {
                string name = FootstepSurfaces[i];
                string folder = FootstepsPack + "Footsteps_" + name + "/";
                SerializedProperty s = surfaces.GetArrayElementAtIndex(i);
                s.FindPropertyRelative("name").stringValue = name;
                SetClips(s.FindPropertyRelative("walk"), LoadClips(folder + "Footsteps_" + name + "_Walk", null));
                SetClips(s.FindPropertyRelative("run"), LoadClips(folder + "Footsteps_" + name + "_Run", null));
                // Sauts dans "_Jump" (ou "_Land" pour DirtyGround) : Start = impulsion, Land = reception.
                string jumpFolder = folder + "Footsteps_" + name + "_Jump";
                if (!AssetDatabase.IsValidFolder(jumpFolder)) jumpFolder = folder + "Footsteps_" + name + "_Land";
                SetClips(s.FindPropertyRelative("jump"), LoadClips(jumpFolder, "Start"));
                SetClips(s.FindPropertyRelative("land"), LoadClips(jumpFolder, "Land"));
            }

            SetRules(so.FindProperty("terrainLayerRules"), TerrainSurfaceRules);
            SetRules(so.FindProperty("materialRules"), MaterialSurfaceRules);
            so.FindProperty("defaultSurface").stringValue = "DirtyGround";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Clips d'un dossier du pack, filtres par un mot du nom (Start / Land), tries par nom.</summary>
        private static List<AudioClip> LoadClips(string folder, string contains)
        {
            List<AudioClip> clips = new List<AudioClip>();

            if (!AssetDatabase.IsValidFolder(folder))
            {
                return clips;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (contains == null || Path.GetFileName(path).IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(path));
                }
            }

            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return clips;
        }

        private static void SetClips(SerializedProperty array, List<AudioClip> clips)
        {
            array.arraySize = clips.Count;

            for (int i = 0; i < clips.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }
        }

        private static void SetRules(SerializedProperty array, string[,] rules)
        {
            int count = rules.GetLength(0);
            array.arraySize = count;

            for (int i = 0; i < count; i++)
            {
                SerializedProperty rule = array.GetArrayElementAtIndex(i);
                rule.FindPropertyRelative("match").stringValue = rules[i, 0];
                rule.FindPropertyRelative("surface").stringValue = rules[i, 1];
            }
        }

        /// <summary>Fond sonore de la foret, en boucle, non spatialise.</summary>
        private static void CreateAmbience()
        {
            // 6.5 Mo de MP3 : lu en streaming plutot que decompresse en memoire.
            AudioImporter importer = AssetImporter.GetAtPath(ForestAmbiencePath) as AudioImporter;

            if (importer == null)
            {
                Debug.LogWarning("[Level] Ambiance introuvable : " + ForestAmbiencePath);
                return;
            }

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;

            if (settings.loadType != AudioClipLoadType.Streaming)
            {
                settings.loadType = AudioClipLoadType.Streaming;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }

            GameObject ambience = new GameObject("[Ambience]");
            AudioSource source = ambience.AddComponent<AudioSource>();
            source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ForestAmbiencePath);
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 0f;
            source.volume = 0.45f;
            source.priority = 32; // ne jamais etre coupe au profit d'un autre son
        }

        /// <summary>
        /// Nuit sans lune visible : lune tres faible, ambiante quasi noire, brouillard plus
        /// dense et plus sombre, ciel plus noir. Ne modifie que cette scene (le ciel est une
        /// copie : M_NightSky du prototype reste intact).
        /// </summary>
        private static void DarkenNight(Transform lighting)
        {
            Transform moonTransform = lighting.Find("Moon");
            Light moon = moonTransform != null ? moonTransform.GetComponent<Light>() : null;

            if (moon != null)
            {
                moon.intensity = 0.05f;
                moon.color = new Color(0.55f, 0.63f, 0.85f);
                moon.shadowStrength = 1f;
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.006f, 0.007f, 0.011f);

            // Les reflets d'environnement viennent d'un ciel par defaut clair : sur les
            // feuilles (cartes vues de biais, Fresnel) ils donnaient un feuillage gris lumineux.
            RenderSettings.reflectionIntensity = 0.08f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.028f;
            RenderSettings.fogColor = new Color(0.004f, 0.005f, 0.008f);

            Material source = RenderSettings.skybox;

            if (source != null)
            {
                Material sky = AssetDatabase.LoadAssetAtPath<Material>(ForestSkyPath);

                if (sky == null)
                {
                    sky = new Material(source);
                    AssetDatabase.CreateAsset(sky, ForestSkyPath);
                }

                sky.CopyPropertiesFromMaterial(source);
                sky.SetFloat("_Exposure", 0.025f);
                sky.SetColor("_SkyTint", new Color(0.06f, 0.08f, 0.14f));
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
            }
        }

        private static void SetSceneNames(GameManager gm)
        {
            SerializedObject so = new SerializedObject(gm);
            so.FindProperty("mainMenuSceneName").stringValue = Path.GetFileNameWithoutExtension(MainMenuScenePath);
            so.FindProperty("gameplaySceneName").stringValue = SceneName;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// "Nouvelle partie" charge la scene nommee dans le GameManager du menu :
        /// on la fait pointer vers la foret.
        /// </summary>
        private static void PointMainMenuToForest()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                Debug.LogWarning("[Level] Menu introuvable : " + MainMenuScenePath);
                return;
            }

            Scene menu = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Additive);
            GameManager gm = null;

            foreach (GameObject root in menu.GetRootGameObjects())
            {
                gm = root.GetComponentInChildren<GameManager>(true);

                if (gm != null)
                {
                    break;
                }
            }

            if (gm != null)
            {
                SetSceneNames(gm);
                EditorSceneManager.MarkSceneDirty(menu);
                EditorSceneManager.SaveScene(menu);
                Debug.Log("[Level] 'Nouvelle partie' charge maintenant : " + SceneName);
            }
            else
            {
                Debug.LogWarning("[Level] Aucun GameManager dans " + MainMenuScenePath);
            }

            EditorSceneManager.CloseScene(menu, true);
        }

        /// <summary>Menu en premier (scene de demarrage de la build), puis la foret.</summary>
        private static void RegisterInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == MainMenuScenePath || s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(MainMenuScenePath, true));
            scenes.Insert(1, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ------------------------------------------------------------------
        // Terrain
        // ------------------------------------------------------------------

        private static Terrain BuildTerrain(Transform parent, LandscapeMeta meta)
        {
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);

            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, TerrainDataPath);
            }

            data.heightmapResolution = meta.resolution;
            data.alphamapResolution = 1024;
            data.baseMapResolution = 1024;
            data.SetDetailResolution(1024, 32);
            data.size = new Vector3(meta.size, meta.heightRange, meta.size);

            float[,] clearings = LoadMask(ClearingsPath, meta.resolution);
            float[,] paths = LoadMask(PathsPath, meta.resolution);
            float pathRange = meta.pathMaskMeters;

            LoadHeights(data, meta.resolution);
            BuildLayers(data, clearings, paths, pathRange);
            BuildTrees(data, clearings, paths, pathRange);
            BuildDetails(data, clearings, paths, pathRange);

            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terrain_Forest_2k";
            terrainObject.transform.SetParent(parent, false);
            terrainObject.transform.position = Vector3.zero;

            Terrain terrain = terrainObject.GetComponent<Terrain>();

            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;

            if (pipeline != null && pipeline.defaultTerrainMaterial != null)
            {
                terrain.materialTemplate = pipeline.defaultTerrainMaterial;
            }

            // Les arbres du pack n'ont pas de billboard : au-dela de treeDistance ils
            // disparaissent, le brouillard doit donc les cacher avant cette distance.
            terrain.treeDistance = 220f;
            terrain.treeBillboardDistance = 220f;
            terrain.treeCrossFadeLength = 15f;
            terrain.treeMaximumFullLODCount = 400;
            terrain.detailObjectDistance = 70f;
            terrain.detailObjectDensity = 0.7f;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 250f;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = ShadowCastingMode.TwoSided;

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            return terrain;
        }

        /// <summary>RAW 16 bits little-endian, ligne 0 = bord sud (z = 0).</summary>
        private static void LoadHeights(TerrainData data, int res)
        {
            byte[] bytes = File.ReadAllBytes(HeightPath);
            float[,] heights = new float[res, res];

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = (y * res + x) * 2;
                    heights[y, x] = (bytes[i] | (bytes[i + 1] << 8)) / 65535f;
                }
            }

            data.SetHeights(0, 0, heights);
        }

        /// <summary>
        /// Masque 8 bits. Clairieres : 1 = clairiere (aucun arbre), 0 = foret.
        /// Chemins : 1 = axe du chemin, 0 = a pathMaskMeters ou plus.
        /// </summary>
        private static float[,] LoadMask(string path, int res)
        {
            float[,] mask = new float[res, res];

            if (!File.Exists(path))
            {
                Debug.LogWarning("[Level] Masque introuvable : " + path);
                return mask;
            }

            byte[] bytes = File.ReadAllBytes(path);

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    mask[y, x] = bytes[y * res + x] / 255f;
                }
            }

            return mask;
        }

        /// <summary>Lecture bilineaire du masque en coordonnees normalisees (0..1).</summary>
        private static float SampleMask(float[,] mask, float u, float v)
        {
            int res = mask.GetLength(0);
            float fx = Mathf.Clamp01(u) * (res - 1);
            float fy = Mathf.Clamp01(v) * (res - 1);
            int x0 = Mathf.Min((int)fx, res - 2);
            int y0 = Mathf.Min((int)fy, res - 2);
            float tx = fx - x0;
            float ty = fy - y0;

            float a = Mathf.Lerp(mask[y0, x0], mask[y0, x0 + 1], tx);
            float b = Mathf.Lerp(mask[y0 + 1, x0], mask[y0 + 1, x0 + 1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        /// <summary>Distance (m) a l'axe du chemin le plus proche, plafonnee a pathRange.</summary>
        private static float PathDistance(float[,] paths, float pathRange, float u, float v)
        {
            return (1f - SampleMask(paths, u, v)) * pathRange;
        }

        // ------------------------------------------------------------------
        // Textures
        // ------------------------------------------------------------------

        private static void BuildLayers(TerrainData data, float[,] clearings, float[,] paths, float pathRange)
        {
            // Couches deja matifiees par PrototypeTerrainBuilder.
            string[] names = { "TL_ForestFloor", "TL_DeadLeaves", "TL_Grass", "TL_DirtPath" };
            TerrainLayer[] layers = new TerrainLayer[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                layers[i] = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayersFolder + names[i] + ".terrainlayer");

                if (layers[i] == null)
                {
                    Debug.LogWarning("[Level] TerrainLayer manquant : " + names[i] + ". Lance d'abord 'Build Prototype Level'.");
                    return;
                }
            }

            data.terrainLayers = layers;

            int res = data.alphamapResolution;
            float size = data.size.x;
            float[,,] maps = new float[res, res, layers.Length];

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / (res - 1);
                    float v = (float)y / (res - 1);
                    float wx = u * size;
                    float wz = v * size;

                    float open = SampleMask(clearings, u, v);
                    float slope = data.GetSteepness(u, v);

                    // Sol de foret + plaques de feuilles mortes a deux echelles.
                    float leafNoise = Mathf.PerlinNoise(wx * 0.012f + 3f, wz * 0.012f + 8f) * 0.6f
                                    + Mathf.PerlinNoise(wx * 0.07f + 21f, wz * 0.07f + 5f) * 0.4f;
                    float wLeaves = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, leafNoise));

                    // Herbe dans les clairieres, qui deborde un peu en lisiere.
                    float grassNoise = Mathf.PerlinNoise(wx * 0.05f + 11f, wz * 0.05f + 2f);
                    float wGrass = Mathf.Clamp01(open * 1.3f) * (0.6f + grassNoise * 0.4f);

                    // Terre nue sur les pentes raides et sur les chemins (bord irregulier).
                    float wDirt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 36f, slope));
                    float edge = PathHalfWidth + (Mathf.PerlinNoise(wx * 0.35f + 40f, wz * 0.35f + 7f) - 0.5f) * 1.2f;
                    float wPath = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge * 0.6f, edge + 0.8f, PathDistance(paths, pathRange, u, v)));
                    wDirt = Mathf.Max(wDirt, wPath);

                    float forest = (1f - wGrass) * (1f - wDirt);
                    float[] w = { forest * (1f - wLeaves), forest * wLeaves, wGrass * (1f - wDirt), wDirt };

                    float sum = w[0] + w[1] + w[2] + w[3];
                    if (sum <= 0.0001f) { w[0] = 1f; sum = 1f; }

                    for (int i = 0; i < layers.Length; i++)
                    {
                        maps[y, x, i] = w[i] / sum;
                    }
                }
            }

            data.SetAlphamaps(0, 0, maps);
        }

        // ------------------------------------------------------------------
        // Arbres
        // ------------------------------------------------------------------

        private static void BuildTrees(TerrainData data, float[,] clearings, float[,] paths, float pathRange)
        {
            List<TreePrototype> prototypes = new List<TreePrototype>();

            for (int i = 1; i <= 4; i++)
            {
                // Variantes a capsule creees par PrototypeTerrainBuilder, sinon le prefab du pack.
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreeVariantsFolder + "Tree " + i + " (Collision).prefab");

                if (prefab == null)
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JpPrefabs + "Tree " + i + ".prefab");
                }

                if (prefab == null)
                {
                    continue;
                }

                TreePrototype prototype = new TreePrototype();
                prototype.prefab = prefab;
                prototype.bendFactor = 0f;
                prototypes.Add(prototype);
            }

            if (prototypes.Count == 0)
            {
                Debug.LogWarning("[Level] Aucun prefab d'arbre trouve.");
                return;
            }

            data.treePrototypes = prototypes.ToArray();

            System.Random rng = new System.Random(Seed);
            float size = data.size.x;
            int cells = Mathf.FloorToInt(size / TreeSpacing);
            List<TreeInstance> instances = new List<TreeInstance>(cells * cells);

            for (int cy = 0; cy < cells; cy++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    float wx = (cx + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.9f) * TreeSpacing;
                    float wz = (cy + 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.9f) * TreeSpacing;
                    float u = wx / size;
                    float v = wz / size;
                    float roll = (float)rng.NextDouble();

                    if (u < 0.002f || v < 0.002f || u > 0.998f || v > 0.998f)
                    {
                        continue;
                    }

                    float open = SampleMask(clearings, u, v);

                    if (open > 0.5f || data.GetSteepness(u, v) > MaxTreeSlope
                        || PathDistance(paths, pathRange, u, v) < PathTreeClearance)
                    {
                        continue;
                    }

                    // Densite variable : bosquets serres et zones un peu plus claires,
                    // lisiere progressive autour des clairieres.
                    float density = 0.35f + Mathf.PerlinNoise(wx * 0.006f + 77f, wz * 0.006f + 13f) * 0.75f;
                    density *= 1f - open * 1.6f;

                    if (roll > density)
                    {
                        continue;
                    }

                    float scale = Mathf.Lerp(0.75f, 1.3f, (float)rng.NextDouble());

                    TreeInstance tree = new TreeInstance();
                    tree.position = new Vector3(u, 0f, v);
                    tree.prototypeIndex = rng.Next(prototypes.Count);
                    tree.widthScale = scale * Mathf.Lerp(0.9f, 1.1f, (float)rng.NextDouble());
                    tree.heightScale = scale;
                    tree.rotation = (float)rng.NextDouble() * Mathf.PI * 2f;
                    tree.color = Color.Lerp(Color.white, new Color(0.85f, 0.9f, 0.8f), (float)rng.NextDouble());
                    tree.lightmapColor = Color.white;

                    instances.Add(tree);
                }
            }

            data.SetTreeInstances(instances.ToArray(), true);
            Debug.Log("[Level] Arbres places : " + instances.Count);
        }

        // ------------------------------------------------------------------
        // Herbe et sous-bois
        // ------------------------------------------------------------------

        private static void BuildDetails(TerrainData data, float[,] clearings, float[,] paths, float pathRange)
        {
            string[] names = { "Grass Patch Small", "Grass Patch Large", "Foliage 1", "Foliage 3" };
            List<DetailPrototype> prototypes = new List<DetailPrototype>();
            List<int> kinds = new List<int>();

            for (int i = 0; i < names.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JpPrefabs + names[i] + ".prefab");

                if (prefab == null)
                {
                    continue;
                }

                // Les patchs du pack font ~1.5 m de haut : on les ramene a hauteur de
                // genou, sinon ils masquent la vue du joueur (yeux a 1.65 m).
                bool grassKind = i < 2;

                DetailPrototype prototype = new DetailPrototype();
                prototype.prototype = prefab;
                prototype.usePrototypeMesh = true;
                prototype.renderMode = DetailRenderMode.VertexLit;
                prototype.useInstancing = true;
                prototype.minWidth = grassKind ? 0.5f : 0.6f;
                prototype.maxWidth = grassKind ? 0.8f : 0.95f;
                prototype.minHeight = grassKind ? 0.3f : 0.5f;
                prototype.maxHeight = grassKind ? 0.55f : 0.8f;
                prototype.noiseSpread = 0.3f;
                prototype.healthyColor = Color.white;
                prototype.dryColor = new Color(0.85f, 0.85f, 0.8f);

                prototypes.Add(prototype);
                kinds.Add(i < 2 ? 0 : 1); // 0 = herbe, 1 = sous-bois
            }

            if (prototypes.Count == 0)
            {
                return;
            }

            data.detailPrototypes = prototypes.ToArray();
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

            System.Random rng = new System.Random(Seed + 1);
            int res = data.detailResolution;
            float size = data.size.x;

            for (int layer = 0; layer < prototypes.Count; layer++)
            {
                int[,] map = new int[res, res];
                bool isGrass = kinds[layer] == 0;

                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float u = (float)x / (res - 1);
                        float v = (float)y / (res - 1);
                        float wx = u * size;
                        float wz = v * size;

                        float open = SampleMask(clearings, u, v);
                        float noise = Mathf.PerlinNoise(wx * 0.04f + layer * 9f, wz * 0.04f + layer * 4f);
                        float roll = (float)rng.NextDouble();
                        float toPath = PathDistance(paths, pathRange, u, v);

                        // Chemin degage : herbe seulement sur les bords, sous-bois plus loin.
                        if (toPath < (isGrass ? PathHalfWidth * 0.8f : PathHalfWidth + 1.5f))
                        {
                            continue;
                        }

                        if (isGrass)
                        {
                            // Herbe dans les clairieres, touffes eparses sous les arbres.
                            float chance = open > 0.2f ? 0.45f : (noise > 0.55f ? 0.2f : 0f);

                            if (roll < chance)
                            {
                                map[y, x] = 1;
                            }
                        }
                        else
                        {
                            // Sous-bois en groupes, surtout en foret et en lisiere.
                            float chance = noise > 0.5f ? (1f - open) * 0.25f : 0f;

                            if (roll < chance)
                            {
                                map[y, x] = 1;
                            }
                        }
                    }
                }

                data.SetDetailLayer(0, 0, layer, map);
            }
        }

        // ------------------------------------------------------------------
        // Reperes des clairieres
        // ------------------------------------------------------------------

        /// <summary>
        /// Un GameObject par clairiere, au centre et a la bonne altitude : il suffit
        /// d'y glisser une habitation. Le rayon est dans le nom.
        /// </summary>
        private static void CreateClearingMarkers(LandscapeMeta meta)
        {
            Transform root = new GameObject("--- Clearings ---").transform;

            if (meta.clearings == null)
            {
                return;
            }

            foreach (ClearingInfo c in meta.clearings)
            {
                GameObject marker = new GameObject(c.name + " (R " + Mathf.RoundToInt(c.radius) + " m)");
                marker.transform.SetParent(root, false);
                marker.transform.position = new Vector3(c.x, c.y, c.z);
            }
        }

        // ------------------------------------------------------------------
        // Batiments : eglise (church_builder.py), cimetiere (cemetery_builder.py)
        // ------------------------------------------------------------------

        /// <summary>Batiment modelise dans Blender (entree vers -Z local, origine au seuil).</summary>
        private class BuildingSpec
        {
            public string label;           // "Church", "Cemetery"
            public string fbxPath;
            public Rect footprint;          // emprise locale : x = largeur, y du Rect = z local
            public float centerZ;           // z local pose au centre de la clairiere
            public string leafPrefix;       // battants ouvrables : "Church_DoorLeaf_"...
            public string doorName;
            public float doorAngle;
            public float doorOpenSpeed;
            public float doorCloseSpeed;
        }

        private static readonly BuildingSpec Church = new BuildingSpec
        {
            label = "Church",
            fbxPath = ManorAssetPostprocessor.ChurchFbxPath,
            footprint = Rect.MinMaxRect(-7.2f, -8.6f, 7.2f, 30.8f),
            centerZ = 11f,
            leafPrefix = "Church_DoorLeaf_",
            doorName = "Porte de l'eglise",
            doorAngle = 90f,
            doorOpenSpeed = 110f,   // lourd battant de chene
            doorCloseSpeed = 130f,
        };

        private static readonly BuildingSpec Cemetery = new BuildingSpec
        {
            label = "Cemetery",
            fbxPath = ManorAssetPostprocessor.CemeteryFbxPath,
            footprint = Rect.MinMaxRect(-14.6f, -2.2f, 14.6f, 30.2f),
            centerZ = 14f,
            leafPrefix = "Cemetery_GateLeaf_",
            doorName = "Grille du cimetiere",
            doorAngle = 100f,
            doorOpenSpeed = 95f,    // vieille grille qui resiste
            doorCloseSpeed = 120f,
        };

        /// <summary>
        /// Eglise dans la clairiere assez grande (>= 40 m) la plus proche du depart, porte vers
        /// le joueur ; cimetiere dans la clairiere (>= 30 m) la plus proche de l'eglise, grille
        /// tournee vers elle. Renvoie l'eglise (pour la carte) et remplit la liste des lieux batis.
        /// </summary>
        private static GameObject PlaceBuildings(Terrain terrain, LandscapeMeta meta, Transform parent, Dictionary<string, string> builtSites)
        {
            if (meta.clearings == null || meta.clearings.Length < 3)
            {
                return null;
            }

            ClearingInfo spawn = meta.clearings[0];
            ClearingInfo churchSite = NearestClearing(meta, new Vector2(spawn.x, spawn.z), 40f, spawn);
            GameObject church = null;

            if (churchSite != null)
            {
                church = PlaceBuilding(Church, terrain, churchSite, new Vector2(spawn.x, spawn.z), parent);

                if (church != null)
                {
                    builtSites[churchSite.name] = "Church";
                    CreateBellZone(church.transform, churchSite);
                }
            }

            Vector2 from = churchSite != null ? new Vector2(churchSite.x, churchSite.z) : new Vector2(spawn.x, spawn.z);
            ClearingInfo cemeterySite = NearestClearing(meta, from, 30f, spawn, churchSite);

            GameObject cemetery = cemeterySite != null ? PlaceBuilding(Cemetery, terrain, cemeterySite, from, parent) : null;

            if (cemetery != null)
            {
                builtSites[cemeterySite.name] = "Cemetery";
                CreateCemeteryBellZone(cemetery.transform);
            }

            return church;
        }

        private static ClearingInfo NearestClearing(LandscapeMeta meta, Vector2 from, float minRadius, params ClearingInfo[] exclude)
        {
            ClearingInfo site = null;
            float best = float.MaxValue;

            foreach (ClearingInfo c in meta.clearings)
            {
                if (c.radius < minRadius || System.Array.IndexOf(exclude, c) >= 0)
                {
                    continue;
                }

                float d = Vector2.Distance(new Vector2(c.x, c.z), from);

                if (d < best)
                {
                    best = d;
                    site = c;
                }
            }

            return site;
        }

        /// <summary>
        /// Pose un batiment au centre d'une clairiere, entree tournee vers <paramref name="facing"/>.
        /// Sol aplani sous l'emprise, herbe et arbres retires, collisions sur tout le batiment,
        /// battants transformes en portes (Door, touche d'interaction).
        /// </summary>
        private static GameObject PlaceBuilding(BuildingSpec spec, Terrain terrain, ClearingInfo site, Vector2 facing, Transform parent)
        {
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(spec.fbxPath);

            if (fbx == null)
            {
                Debug.LogWarning("[Level] " + spec.label + " non place : " + spec.fbxPath + " introuvable (lance le script Blender).");
                return null;
            }

            Vector3 center = new Vector3(site.x, 0f, site.z);
            Vector3 toFacing = new Vector3(facing.x - site.x, 0f, facing.y - site.z).normalized;
            Quaternion rotation = Quaternion.LookRotation(-toFacing, Vector3.up);
            Vector3 origin = center - rotation * new Vector3(0f, 0f, spec.centerZ);

            origin.y = FlattenUnderFootprint(terrain, origin, rotation, spec);
            ClearFootprint(terrain, origin, rotation, spec);

            GameObject building = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(building, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            building.name = spec.label + " (" + site.name + ")";
            building.transform.SetParent(parent, false);
            building.transform.SetPositionAndRotation(origin, rotation);

            Vector3 inside = building.transform.TransformPoint(new Vector3(0f, 1.5f, spec.centerZ));
            int doors = 0;

            foreach (MeshRenderer renderer in building.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.name.StartsWith(spec.leafPrefix))
                {
                    BuildLeafDoor(renderer.gameObject, building.transform, inside, spec);
                    doors++;
                    continue;
                }

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                MeshCollider collider = renderer.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }

            Debug.Log("[Level] " + spec.label + " pose dans " + site.name + " (" + doors + " battants).");
            return building;
        }

        private const string ChurchBellsPath = "Assets/_Game/Audio/horror/church-bells.mp3";
        private const string CemeteryBellPath = "Assets/_Game/Audio/horror/clochette-cimetiere.mp3";

        /// <summary>
        /// Clochette quand le joueur entre dans l'enclos du cimetiere (interieur du mur,
        /// pas toute la clairiere). Le son vient du fond, pres du mausolee.
        /// </summary>
        private static void CreateCemeteryBellZone(Transform cemetery)
        {
            AudioClip bell = AssetDatabase.LoadAssetAtPath<AudioClip>(CemeteryBellPath);

            if (bell == null)
            {
                Debug.LogWarning("[Level] Son de la clochette introuvable : " + CemeteryBellPath);
                return;
            }

            GameObject zone = new GameObject("Cemetery_BellZone");
            zone.transform.SetParent(cemetery, false);
            zone.transform.localPosition = new Vector3(0f, 3f, 26f); // devant le mausolee
            zone.transform.localRotation = Quaternion.identity;

            // Interieur du mur d'enceinte (x -13.5..13.5, z 0.5..29.5), sur 5 m de haut.
            BoxCollider trigger = zone.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, -0.5f, -11f);
            trigger.size = new Vector3(27f, 5f, 29f);

            AudioSource source = zone.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.volume = 0.9f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 20f;   // bien audible dans tout l'enclos
            source.maxDistance = 150f;
            source.dopplerLevel = 0f;

            SoundTriggerZone sound = zone.AddComponent<SoundTriggerZone>();
            SerializedObject so = new SerializedObject(sound);
            so.FindProperty("soundToPlay").objectReferenceValue = bell;
            so.FindProperty("playOnce").boolValue = false;   // a chaque entree dans le cimetiere
            so.FindProperty("stopWhenExit").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Les cloches sonnent quand le joueur entre dans la clairiere de l'eglise
        /// (SoundTriggerZone). Le son part du beffroi, en 3D : on l'entend venir du clocher.
        /// </summary>
        private static void CreateBellZone(Transform church, ClearingInfo site)
        {
            AudioClip bells = AssetDatabase.LoadAssetAtPath<AudioClip>(ChurchBellsPath);

            if (bells == null)
            {
                Debug.LogWarning("[Level] Son des cloches introuvable : " + ChurchBellsPath);
                return;
            }

            GameObject zone = new GameObject("Church_BellZone");
            zone.transform.SetParent(church, false);
            zone.transform.localPosition = new Vector3(0f, 15.3f, -3f); // beffroi
            zone.transform.rotation = Quaternion.identity;

            // Trigger vertical couvrant la clairiere (centre au sol, au centre de la clairiere).
            CapsuleCollider trigger = zone.AddComponent<CapsuleCollider>();
            trigger.isTrigger = true;
            trigger.direction = 1;
            trigger.radius = site.radius;
            trigger.height = 80f;
            Vector3 clearingCenter = new Vector3(site.x, zone.transform.position.y - 15.3f, site.z);
            trigger.center = zone.transform.InverseTransformPoint(clearingCenter);

            AudioSource source = zone.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.volume = 1f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 30f;   // pleine puissance dans toute la clairiere
            source.maxDistance = 500f;
            source.dopplerLevel = 0f;

            SoundTriggerZone sound = zone.AddComponent<SoundTriggerZone>();
            SerializedObject so = new SerializedObject(sound);
            so.FindProperty("soundToPlay").objectReferenceValue = bells;
            so.FindProperty("playOnce").boolValue = false;   // a chaque arrivee dans la clairiere
            so.FindProperty("stopWhenExit").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Battant importe (origine sur le gond) -> hierarchie DoorBase :
        /// X_Door_L (Door) > Hinge (pivot) > Panel (mesh + BoxCollider). S'ouvre vers l'interieur.
        /// </summary>
        private static void BuildLeafDoor(GameObject leaf, Transform building, Vector3 inside, BuildingSpec spec)
        {
            float angle = spec.doorAngle;
            string side = leaf.name.Substring(leaf.name.Length - 1);

            GameObject rootGo = new GameObject(spec.label + "_Door_" + side);
            rootGo.transform.SetParent(leaf.transform.parent, false);
            rootGo.transform.SetPositionAndRotation(leaf.transform.position, building.rotation);

            GameObject hingeGo = new GameObject("Hinge");
            hingeGo.transform.SetParent(rootGo.transform, false);

            // Bord libre du battant : deux fois le centre du mesh (le gond est a l'origine).
            MeshFilter filter = leaf.GetComponent<MeshFilter>();
            Vector3 lever = leaf.transform.TransformVector(filter.sharedMesh.bounds.center) * 2f;
            lever.y = 0f;
            Vector3 hingePos = rootGo.transform.position;
            Vector3 endPlus = hingePos + Quaternion.AngleAxis(angle, Vector3.up) * lever;
            Vector3 endMinus = hingePos + Quaternion.AngleAxis(-angle, Vector3.up) * lever;

            // Une rotation positive doit emmener le battant vers la nef.
            if ((endMinus - inside).sqrMagnitude < (endPlus - inside).sqrMagnitude)
            {
                hingeGo.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            }

            leaf.transform.SetParent(hingeGo.transform, true);
            leaf.name = "Panel";

            BoxCollider box = leaf.AddComponent<BoxCollider>();
            box.center = filter.sharedMesh.bounds.center;
            box.size = filter.sharedMesh.bounds.size;

            Door door = rootGo.AddComponent<Door>();
            EditorSetupUtility.SetObjectField(door, "hinge", hingeGo.transform);

            SerializedObject so = new SerializedObject(door);
            so.FindProperty("openAngle").floatValue = angle;
            so.FindProperty("openAwayFromInstigator").boolValue = false;
            so.FindProperty("displayName").stringValue = spec.doorName;
            so.FindProperty("openSpeed").floatValue = spec.doorOpenSpeed;
            so.FindProperty("closeSpeed").floatValue = spec.doorCloseSpeed;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool InFootprint(Vector3 world, Vector3 origin, Quaternion rotation, BuildingSpec spec, float margin)
        {
            Rect f = spec.footprint;
            Vector3 local = Quaternion.Inverse(rotation) * (world - origin);
            return local.x >= f.xMin - margin && local.x <= f.xMax + margin
                && local.z >= f.yMin - margin && local.z <= f.yMax + margin;
        }

        /// <summary>Distance (m) a l'emprise, 0 a l'interieur.</summary>
        private static float FootprintDistance(Vector3 world, Vector3 origin, Quaternion rotation, BuildingSpec spec)
        {
            Rect f = spec.footprint;
            Vector3 local = Quaternion.Inverse(rotation) * (world - origin);
            float dx = Mathf.Max(f.xMin - local.x, 0f, local.x - f.xMax);
            float dz = Mathf.Max(f.yMin - local.z, 0f, local.z - f.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Aplani le terrain sous l'emprise a l'altitude moyenne (fondu sur 10 m autour)
        /// et renvoie cette altitude : c'est le niveau du pied des murs.
        /// </summary>
        private static float FlattenUnderFootprint(Terrain terrain, Vector3 origin, Quaternion rotation, BuildingSpec spec)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            float cell = data.size.x / (res - 1);
            const float fade = 10f;

            // demi-diagonale de l'emprise + fondu
            float reach = 0.5f * Mathf.Sqrt(spec.footprint.width * spec.footprint.width + spec.footprint.height * spec.footprint.height) + 6f + fade;
            Vector3 mid = origin + rotation * new Vector3(spec.footprint.center.x, 0f, spec.footprint.center.y);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((mid.x - reach) / cell), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((mid.x + reach) / cell), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((mid.z - reach) / cell), 0, res - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((mid.z + reach) / cell), 0, res - 1);

            float[,] heights = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);

            float sum = 0f;
            int count = 0;

            for (int z = 0; z <= z1 - z0; z++)
            {
                for (int x = 0; x <= x1 - x0; x++)
                {
                    Vector3 p = new Vector3((x0 + x) * cell, 0f, (z0 + z) * cell);

                    if (InFootprint(p, origin, rotation, spec, 0f))
                    {
                        sum += heights[z, x];
                        count++;
                    }
                }
            }

            float target = count > 0 ? sum / count : heights[0, 0];

            for (int z = 0; z <= z1 - z0; z++)
            {
                for (int x = 0; x <= x1 - x0; x++)
                {
                    Vector3 p = new Vector3((x0 + x) * cell, 0f, (z0 + z) * cell);
                    float w = 1f - Mathf.SmoothStep(0f, 1f, FootprintDistance(p, origin, rotation, spec) / fade);
                    heights[z, x] = Mathf.Lerp(heights[z, x], target, w);
                }
            }

            data.SetHeights(x0, z0, heights);
            return target * data.size.y + terrain.transform.position.y;
        }

        /// <summary>Retire l'herbe (elle traverserait le dallage) et les arbres de l'emprise.</summary>
        private static void ClearFootprint(Terrain terrain, Vector3 origin, Quaternion rotation, BuildingSpec spec)
        {
            TerrainData data = terrain.terrainData;
            float size = data.size.x;

            int res = data.detailResolution;
            float cell = size / res;

            for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            {
                int[,] map = data.GetDetailLayer(0, 0, res, res, layer);
                bool changed = false;

                for (int z = 0; z < res; z++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        if (map[z, x] == 0)
                        {
                            continue;
                        }

                        Vector3 p = new Vector3((x + 0.5f) * cell, 0f, (z + 0.5f) * cell);

                        if (InFootprint(p, origin, rotation, spec, 2f))
                        {
                            map[z, x] = 0;
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    data.SetDetailLayer(0, 0, layer, map);
                }
            }

            List<TreeInstance> kept = new List<TreeInstance>(data.treeInstanceCount);

            foreach (TreeInstance tree in data.treeInstances)
            {
                Vector3 p = new Vector3(tree.position.x * size, 0f, tree.position.z * size);

                if (!InFootprint(p, origin, rotation, spec, 4f))
                {
                    kept.Add(tree);
                }
            }

            data.SetTreeInstances(kept.ToArray(), true);
        }

        // ------------------------------------------------------------------
        // Noms des lieux
        // ------------------------------------------------------------------

        private const string LocationsPath = "Assets/_Game/Settings/ForestLocations.asset";

        /// <summary>Noms proposes pour les clairieres sans batiment (a renommer librement ensuite).</summary>
        private static readonly string[] DefaultPlaceNames =
        {
            "La Clairière des Murmures", "Le Chêne Pendu", "La Mare Noire", "Le Cercle de Pierres",
            "La Clairière aux Corbeaux", "Le Vallon des Brumes", "La Croix Brisée", "Le Puits Oublié",
            "La Clairière du Bûcheron", "Les Souches Grises", "La Lande Morte", "Le Gué des Pleurs",
            "L'Ermitage", "Le Bois des Pendus", "La Butte aux Loups", "La Fosse",
            "Le Refuge", "La Clairière Silencieuse", "Les Ronces", "Le Vieux Moulin",
            "Le Camp Abandonné", "La Clairière des Poupées", "Les Tertres", "Le Rocher Fendu",
            "La Source Froide", "Le Nid", "La Clairière aux Chouettes", "Le Sentier des Ombres",
            "La Hêtraie", "Le Val Sombre", "La Clairière des Cendres", "Le Carrefour du Diable",
        };

        /// <summary>
        /// Cree ForestLocations.asset au premier build, puis n'y ajoute que les clairieres
        /// manquantes : les noms modifies a la main ne sont jamais ecrases.
        /// </summary>
        private static ForestLocations EnsureLocations(LandscapeMeta meta, Dictionary<string, string> builtSites)
        {
            ForestLocations asset = AssetDatabase.LoadAssetAtPath<ForestLocations>(LocationsPath);

            if (asset == null)
            {
                EnsureFolder("Assets/_Game/Settings");
                asset = ScriptableObject.CreateInstance<ForestLocations>();
                AssetDatabase.CreateAsset(asset, LocationsPath);
            }

            if (meta.clearings != null)
            {
                int free = 0;

                for (int i = 0; i < meta.clearings.Length; i++)
                {
                    ClearingInfo c = meta.clearings[i];
                    string kind;
                    string name;

                    if (i == 0)
                    {
                        name = "La Clairière du Départ";
                    }
                    else if (builtSites.TryGetValue(c.name, out kind))
                    {
                        name = kind == "Church" ? "L'Église" : kind == "Cemetery" ? "Le Cimetière" : c.name;
                    }
                    else
                    {
                        name = DefaultPlaceNames[free % DefaultPlaceNames.Length];
                        free++;
                    }

                    asset.EditorEnsure(c.name, name, new Vector3(c.x, c.y, c.z), c.radius);
                }
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        // ------------------------------------------------------------------
        // Carte 3D (touche M)
        // ------------------------------------------------------------------

        private const string MapFolder = "Assets/_Game/Art/Landscape/Map";

        /// <summary>
        /// La maquette est rangee 2 km sous le niveau : la camera du joueur (220 m)
        /// ne la voit jamais, pas besoin de layer dedie.
        /// </summary>
        private static readonly Vector3 MapOrigin = new Vector3(0f, -2000f, 0f);

        private const float MapScale = 0.1f;          // 2 km -> 200 unites
        private const float MapExaggeration = 2f;     // relief x2 pour la lisibilite
        private const int MapMeshResolution = 513;    // un sommet tous les ~3.9 m
        private const int MapTextureResolution = 1024;
        private const float MapBaseDepth = 6f;        // epaisseur du socle de la maquette

        private static void BuildMap3D(Terrain terrain, LandscapeMeta meta, Transform environment, ForestLocations locations)
        {
            EnsureFolder(MapFolder);

            TerrainData data = terrain.terrainData;
            float[,] clearings = LoadMask(ClearingsPath, meta.resolution);
            float[,] paths = LoadMask(PathsPath, meta.resolution);
            float pathRange = meta.pathMaskMeters;

            Mesh mesh = LoadOrCreate(MapFolder + "/ForestMap_Mesh.asset", () => new Mesh());
            BuildMapMesh(mesh, data, clearings, paths, pathRange);

            Texture2D texture = LoadOrCreate(MapFolder + "/ForestMap_Albedo.asset",
                () => new Texture2D(MapTextureResolution, MapTextureResolution, TextureFormat.RGBA32, true));
            BakeMapTexture(texture, data, clearings, paths, pathRange);

            Material top = MapMaterial("M_ForestMap", Color.white, texture);
            Material side = MapMaterial("M_ForestMap_Base", new Color(0.11f, 0.085f, 0.065f), null);
            Material playerMat = MapMaterial("M_ForestMap_Player", new Color(1f, 0.32f, 0.12f), null);
            Material pinMat = MapMaterial("M_ForestMap_Clearing", new Color(1f, 0.85f, 0.42f), null);

            AssetDatabase.SaveAssets();

            // --- Hierarchie ---------------------------------------------------
            GameObject root = new GameObject("[Map3D]");

            GameObject dioramaObject = new GameObject("Diorama");
            dioramaObject.transform.SetParent(root.transform, false);
            dioramaObject.transform.position = MapOrigin;
            dioramaObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = dioramaObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { top, side };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Visee de la teleportation (ForestMap3D teste uniquement ce collider).
            MeshCollider dioramaCollider = dioramaObject.AddComponent<MeshCollider>();
            dioramaCollider.sharedMesh = mesh;

            // Une balise par clairiere (futurs lieux d'habitation).
            if (meta.clearings != null)
            {
                foreach (ClearingInfo c in meta.clearings)
                {
                    Vector3 local = new Vector3(c.x * MapScale, c.y * MapScale * MapExaggeration, c.z * MapScale);
                    Transform pin = CreatePin(c.name, pinMat).transform;
                    pin.SetParent(dioramaObject.transform, false);
                    pin.localPosition = local;
                }
            }

            // Batiments en miniature : reperes visibles de loin (hauteur x2 comme le relief).
            Material buildingMat = MapMaterial("M_ForestMap_Church", new Color(0.86f, 0.82f, 0.74f), null);

            foreach (Transform building in environment)
            {
                BuildingSpec spec = building.name.StartsWith(Church.label + " (") ? Church
                                  : building.name.StartsWith(Cemetery.label + " (") ? Cemetery
                                  : null;
                GameObject fbx = spec != null ? AssetDatabase.LoadAssetAtPath<GameObject>(spec.fbxPath) : null;

                if (fbx == null)
                {
                    continue;
                }

                GameObject mini = (GameObject)Object.Instantiate(fbx);
                mini.name = spec.label + "_Mini";
                mini.transform.SetParent(dioramaObject.transform, false);
                Vector3 p = building.position;
                mini.transform.localPosition = new Vector3(p.x * MapScale, p.y * MapScale * MapExaggeration, p.z * MapScale);
                mini.transform.localRotation = building.rotation;
                mini.transform.localScale = new Vector3(MapScale, MapScale * MapExaggeration, MapScale);

                foreach (Renderer r in mini.GetComponentsInChildren<Renderer>())
                {
                    Material[] mats = new Material[r.sharedMaterials.Length];
                    for (int k = 0; k < mats.Length; k++) mats[k] = buildingMat;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }

            GameObject marker = CreatePlayerMarker(playerMat);
            marker.transform.SetParent(root.transform, false);
            marker.transform.position = MapOrigin + Vector3.up * 20f;

            // Point d'arrivee de la teleportation : balise cyan, masquee tant que la carte est fermee.
            GameObject teleportCursor = CreatePin("TeleportCursor", MapMaterial("M_ForestMap_Teleport", new Color(0.35f, 0.95f, 1f), null));
            teleportCursor.transform.SetParent(root.transform, false);
            teleportCursor.transform.localScale = Vector3.one * 0.8f;
            teleportCursor.SetActive(false);

            GameObject cameraObject = new GameObject("MapCamera");
            cameraObject.transform.SetParent(root.transform, false);
            Camera mapCamera = cameraObject.AddComponent<Camera>();
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = new Color(0.03f, 0.035f, 0.05f);
            mapCamera.fieldOfView = 50f;
            mapCamera.nearClipPlane = 0.3f;
            mapCamera.farClipPlane = 1200f; // ne voit jamais le vrai terrain, 1.7 km plus haut
            mapCamera.depth = 10f;
            mapCamera.enabled = false;

            ForestMap3D map = root.AddComponent<ForestMap3D>();
            EditorSetupUtility.SetObjectField(map, "mapCamera", mapCamera);
            EditorSetupUtility.SetObjectField(map, "diorama", dioramaObject.transform);
            EditorSetupUtility.SetObjectField(map, "playerMarker", marker.transform);
            EditorSetupUtility.SetObjectField(map, "dioramaCollider", dioramaCollider);
            EditorSetupUtility.SetObjectField(map, "teleportCursor", teleportCursor.transform);
            EditorSetupUtility.SetObjectField(map, "locations", locations);

            SerializedObject so = new SerializedObject(map);
            so.FindProperty("mapScale").floatValue = MapScale;
            so.FindProperty("heightExaggeration").floatValue = MapExaggeration;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>0 = sol nu (clairiere, chemin, falaise), 1 = sous la canopee.</summary>
        private static float Forestness(TerrainData data, float[,] clearings, float[,] paths, float pathRange, float u, float v)
        {
            if (data.GetSteepness(u, v) > MaxTreeSlope)
            {
                return 0f;
            }

            float f = 1f - Mathf.Clamp01(SampleMask(clearings, u, v) * 2f);
            f *= Mathf.Clamp01((PathDistance(paths, pathRange, u, v) - 2.5f) / 2.5f);
            return f;
        }

        private static void BuildMapMesh(Mesh mesh, TerrainData data, float[,] clearings, float[,] paths, float pathRange)
        {
            int n = MapMeshResolution;
            float size = data.size.x;
            float baseY = -MapBaseDepth;

            List<Vector3> vertices = new List<Vector3>(n * n + n * 8);
            List<Vector2> uvs = new List<Vector2>(n * n + n * 8);
            List<int> topTris = new List<int>((n - 1) * (n - 1) * 6);
            List<int> sideTris = new List<int>(n * 24);

            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float u = (float)i / (n - 1);
                    float v = (float)j / (n - 1);
                    float h = data.GetInterpolatedHeight(u, v);

                    // Canopee : 11 a 18 m au-dessus du sol, en houppiers irreguliers.
                    float crowns = Mathf.PerlinNoise(u * size * 0.09f + 5f, v * size * 0.09f + 3f);
                    float canopy = Forestness(data, clearings, paths, pathRange, u, v) * (11f + crowns * 7f);

                    vertices.Add(new Vector3(u * size * MapScale, (h + canopy) * MapScale * MapExaggeration, v * size * MapScale));
                    uvs.Add(new Vector2(u, v));
                }
            }

            for (int j = 0; j < n - 1; j++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i;
                    topTris.Add(a); topTris.Add(a + n); topTris.Add(a + 1);
                    topTris.Add(a + 1); topTris.Add(a + n); topTris.Add(a + n + 1);
                }
            }

            // Socle : quatre bords descendus jusqu'a baseY (materiau double face).
            int[][] edges =
            {
                BuildEdge(n, (k) => k),                    // sud
                BuildEdge(n, (k) => (n - 1) * n + k),      // nord
                BuildEdge(n, (k) => k * n),                // ouest
                BuildEdge(n, (k) => k * n + n - 1)         // est
            };

            foreach (int[] edge in edges)
            {
                int start = vertices.Count;

                for (int k = 0; k < edge.Length; k++)
                {
                    Vector3 top = vertices[edge[k]];
                    vertices.Add(top);
                    vertices.Add(new Vector3(top.x, baseY, top.z));
                    uvs.Add(uvs[edge[k]]);
                    uvs.Add(uvs[edge[k]]);
                }

                for (int k = 0; k < edge.Length - 1; k++)
                {
                    int t0 = start + k * 2;
                    sideTris.Add(t0); sideTris.Add(t0 + 1); sideTris.Add(t0 + 2);
                    sideTris.Add(t0 + 2); sideTris.Add(t0 + 1); sideTris.Add(t0 + 3);
                }
            }

            // Fond du socle.
            int b = vertices.Count;
            float w = size * MapScale;
            vertices.Add(new Vector3(0f, baseY, 0f)); vertices.Add(new Vector3(w, baseY, 0f));
            vertices.Add(new Vector3(0f, baseY, w)); vertices.Add(new Vector3(w, baseY, w));
            for (int k = 0; k < 4; k++) uvs.Add(Vector2.zero);
            sideTris.Add(b); sideTris.Add(b + 1); sideTris.Add(b + 2);
            sideTris.Add(b + 2); sideTris.Add(b + 1); sideTris.Add(b + 3);

            mesh.Clear();
            mesh.name = "ForestMap_Mesh";
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(topTris, 0);
            mesh.SetTriangles(sideTris, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }

        private static int[] BuildEdge(int n, System.Func<int, int> index)
        {
            int[] edge = new int[n];
            for (int k = 0; k < n; k++) edge[k] = index(k);
            return edge;
        }

        /// <summary>
        /// Texture "carte" : couleurs du terrain, relief ombre (lumiere nord-ouest)
        /// et courbes de niveau tous les 10 m. Materiau Unlit : lisible de nuit.
        /// </summary>
        private static void BakeMapTexture(Texture2D texture, TerrainData data, float[,] clearings, float[,] paths, float pathRange)
        {
            int res = MapTextureResolution;

            if (texture.width != res || texture.height != res)
            {
                texture.Reinitialize(res, res, TextureFormat.RGBA32, true);
            }

            float size = data.size.x;
            Vector3 light = new Vector3(-0.55f, 0.75f, 0.45f).normalized;
            Color32[] pixels = new Color32[res * res];

            Color forestDark = new Color(0.06f, 0.14f, 0.06f);
            Color forestLight = new Color(0.14f, 0.26f, 0.10f);
            Color meadowDark = new Color(0.40f, 0.50f, 0.22f);
            Color meadowLight = new Color(0.54f, 0.60f, 0.31f);
            Color rock = new Color(0.40f, 0.37f, 0.32f);
            Color dirt = new Color(0.66f, 0.53f, 0.34f);

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / (res - 1);
                    float v = (float)y / (res - 1);
                    float wx = u * size;
                    float wz = v * size;

                    float forest = Forestness(data, clearings, paths, pathRange, u, v);
                    float slope = data.GetSteepness(u, v);
                    float height = data.GetInterpolatedHeight(u, v);

                    float crowns = Mathf.PerlinNoise(wx * 0.12f + 9f, wz * 0.12f + 4f);
                    float patches = Mathf.PerlinNoise(wx * 0.01f + 2f, wz * 0.01f + 6f);

                    Color c = Color.Lerp(Color.Lerp(meadowDark, meadowLight, patches),
                                         Color.Lerp(forestDark, forestLight, crowns * 0.7f + patches * 0.3f), forest);

                    c = Color.Lerp(c, rock, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 42f, slope)));

                    float toPath = PathDistance(paths, pathRange, u, v);
                    c = Color.Lerp(c, dirt, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.6f, 3.4f, toPath)));

                    // Relief ombre.
                    Vector3 normal = data.GetInterpolatedNormal(u, v);
                    float shade = Mathf.Clamp01(Vector3.Dot(normal, light));
                    c *= 0.45f + 0.75f * shade;

                    // Courbes de niveau (discretes sous la canopee, absentes sur le plat).
                    float toContour = Mathf.Abs(height / 10f - Mathf.Round(height / 10f)) * 10f;
                    float line = toContour < 0.45f ? Mathf.Clamp01(slope / 4f) * (0.3f - forest * 0.18f) : 0f;
                    c *= 1f - line;

                    c.a = 1f;
                    pixels[y * res + x] = c;
                }
            }

            texture.name = "ForestMap_Albedo";
            texture.SetPixels32(pixels);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            texture.Apply(true);
            EditorUtility.SetDirty(texture);
        }

        private static Material MapMaterial(string name, Color color, Texture2D texture)
        {
            string path = MapFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Cull", 0f); // double face : le socle n'a pas a etre bien oriente
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Balise de clairiere : mat fin + boule, sans collider.</summary>
        private static GameObject CreatePin(string name, Material material)
        {
            GameObject pin = new GameObject(name);

            GameObject mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.name = "Mast";
            Object.DestroyImmediate(mast.GetComponent<Collider>());
            mast.transform.SetParent(pin.transform, false);
            mast.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            mast.transform.localScale = new Vector3(0.18f, 2.5f, 0.18f);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(pin.transform, false);
            head.transform.localPosition = new Vector3(0f, 5.4f, 0f);
            head.transform.localScale = Vector3.one * 1.1f;

            foreach (Renderer r in pin.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            return pin;
        }

        /// <summary>Fleche 3D (pointe vers +Z) et fil vertical jusqu'au sol de la maquette.</summary>
        private static GameObject CreatePlayerMarker(Material material)
        {
            GameObject marker = new GameObject("PlayerMarker");

            Mesh arrow = LoadOrCreate(MapFolder + "/ForestMap_Arrow.asset", () => new Mesh());
            arrow.Clear();
            arrow.name = "ForestMap_Arrow";
            arrow.vertices = new[]
            {
                new Vector3(0f, 0f, 2.6f),     // 0 pointe
                new Vector3(-1.6f, 0f, -1.4f), // 1 arriere gauche
                new Vector3(0f, 0f, -0.6f),    // 2 encoche
                new Vector3(1.6f, 0f, -1.4f),  // 3 arriere droite
                new Vector3(0f, 0.9f, 0.1f)    // 4 sommet
            };
            arrow.triangles = new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4, 0, 3, 2, 0, 2, 1 };
            arrow.RecalculateNormals();
            arrow.RecalculateBounds();
            EditorUtility.SetDirty(arrow);

            GameObject head = new GameObject("Arrow");
            head.transform.SetParent(marker.transform, false);
            head.AddComponent<MeshFilter>().sharedMesh = arrow;
            head.AddComponent<MeshRenderer>().sharedMaterial = material;

            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            line.name = "Line";
            Object.DestroyImmediate(line.GetComponent<Collider>());
            line.transform.SetParent(marker.transform, false);
            line.transform.localPosition = new Vector3(0f, -2f, 0f);
            line.transform.localScale = new Vector3(0.12f, 2f, 0.12f);
            line.GetComponent<Renderer>().sharedMaterial = material;

            foreach (Renderer r in marker.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            return marker;
        }

        private static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset == null)
            {
                asset = create();
                AssetDatabase.CreateAsset(asset, path);
            }

            return asset;
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
