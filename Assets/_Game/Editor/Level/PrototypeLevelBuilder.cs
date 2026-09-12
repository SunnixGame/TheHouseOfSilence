using System.Collections.Generic;
using HouseOfSilence.EditorTools.UI;
using HouseOfSilence.Horror;
using HouseOfSilence.Lights;
using HouseOfSilence.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HouseOfSilence.EditorTools.Level
{
    /// <summary>
    /// Construit le niveau prototype complet dans une nouvelle scene
    /// "Prototype_House" : foret, terrain, maison, ambiance nocturne,
    /// systemes de jeu et joueur.
    ///
    /// Menu : Tools > House of Silence > Level > Build Prototype Level
    /// Menu : Tools > House of Silence > Level > Rebuild House Only
    ///        (remplace la maison dans la scene ouverte, garde le reste intact)
    ///
    /// Ordre : conversion des materiaux, scene vide, terrain, maison, rochers,
    /// lumiere et brouillard, volume de post-traitement, systemes, joueur,
    /// sauvegarde et inscription dans la Scene List.
    /// </summary>
    public static class PrototypeLevelBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/Prototype_House.unity";

        private const string SettingsFolder = "Assets/_Game/Settings";
        private const string VolumeProfilePath = SettingsFolder + "/Night_VolumeProfile.asset";
        private const string SkyMaterialPath = "Assets/_Game/Materials/M_NightSky.mat";
        private const string JpPrefabs = "Assets/JP Environmental Asset Pack/Prefabs/";

        private static readonly Color FogColor = new Color(0.018f, 0.022f, 0.032f);

        [MenuItem("Tools/House of Silence/Level/Build Prototype Level", false, 210)]
        public static void BuildPrototypeLevel()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Level] Arrete le Play Mode avant de construire le niveau.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            // 1. Les materiaux du pack doivent etre en URP, sinon tout est magenta.
            EnvironmentMaterialConverter.ConvertEnvironmentMaterials();

            // 2. Scene vide.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Transform environment = new GameObject("--- Environment ---").transform;
            Transform lighting = new GameObject("--- Lighting ---").transform;

            // 3. Terrain et foret.
            Terrain terrain = PrototypeTerrainBuilder.Build(environment);

            // 4. Maison sur le plateau central.
            Vector2 center = PrototypeTerrainBuilder.Center;
            Vector3 houseOrigin = new Vector3(
                center.x - PrototypeHouseBuilder.Width * 0.5f,
                PrototypeTerrainBuilder.PlateauHeight - 0.05f, // murs enterres de 5 cm, sol fini 5 cm au dessus du terrain
                center.y - PrototypeHouseBuilder.Depth * 0.5f);

            GameObject house = PrototypeHouseBuilder.Build(houseOrigin);

            CutTerrainUnderHouse(terrain, houseOrigin);

            // 5. Rochers et decor exterieur.
            ScatterRocks(environment, terrain, houseOrigin);
            AddHorrorDressing(house.transform, houseOrigin);

            // 6. Nuit, lune, brouillard, post-traitement.
            SetupNight(lighting);

            // 7. Systemes de jeu et joueur.
            SetupSystems(terrain);

            // 8. Sauvegarde et Scene List.
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            RegisterInBuildSettings();

            Debug.Log("[Level] Niveau prototype construit et sauvegarde : " + ScenePath);
        }

        /// <summary>
        /// Reconstruit uniquement la maison dans la scene ouverte, a la meme position,
        /// en conservant le terrain, les systemes, le joueur et tout ce qui a ete
        /// ajoute a la main. L'habillage horrifique (HorrorDressing) est reparente
        /// sous la nouvelle maison.
        /// </summary>
        [MenuItem("Tools/House of Silence/Level/Rebuild House Only", false, 211)]
        public static void RebuildHouseOnly()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Level] Arrete le Play Mode avant de reconstruire la maison.");
                return;
            }

            GameObject oldHouse = GameObject.Find("House");

            if (oldHouse == null)
            {
                Debug.LogError("[Level] Aucun objet 'House' dans la scene ouverte : utilise Build Prototype Level.");
                return;
            }

            Vector3 houseOrigin = oldHouse.transform.position;
            Transform dressing = oldHouse.transform.Find("HorrorDressing");

            if (dressing != null)
            {
                dressing.SetParent(null, true);
            }

            Undo.DestroyObjectImmediate(oldHouse);

            GameObject house = PrototypeHouseBuilder.Build(houseOrigin);
            Undo.RegisterCreatedObjectUndo(house, "Rebuild House");

            if (dressing != null)
            {
                dressing.SetParent(house.transform, true);
            }
            else
            {
                AddHorrorDressing(house.transform, houseOrigin);
            }

            // L'emprise de la maison ou de l'abri a pu changer : on redecoupe le terrain.
            Terrain terrain = Object.FindAnyObjectByType<Terrain>();

            if (terrain != null)
            {
                CutTerrainUnderHouse(terrain, houseOrigin);
                EditorUtility.SetDirty(terrain.terrainData);
            }

            EditorSceneManager.MarkSceneDirty(house.scene);
            Debug.Log("[Level] Maison reconstruite en place a " + houseOrigin + ". Pense a sauvegarder la scene.");
        }

        // ------------------------------------------------------------------
        // Terrain : trous sous la maison et sous l'abri de sortie
        // ------------------------------------------------------------------

        private static void CutTerrainUnderHouse(Terrain terrain, Vector3 houseOrigin)
        {
            TerrainData data = terrain.terrainData;
            int res = data.holesResolution;
            bool[,] holes = new bool[res, res];

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    holes[y, x] = true;
                }
            }

            // Sous la maison, en retrait de 0.6 m pour que la dalle recouvre le bord du trou.
            CutRect(holes, res, terrain,
                houseOrigin.x + 0.6f, houseOrigin.x + PrototypeHouseBuilder.Width - 0.6f,
                houseOrigin.z + 0.6f, houseOrigin.z + PrototypeHouseBuilder.Depth - 0.6f);

            // Sous l'abri de la sortie de secours, en L : les deux dalles du tunnel (12,3)-(13,3)
            // et la dalle de sortie (13,2) au sud de la cage.
            CutRect(holes, res, terrain,
                houseOrigin.x + PrototypeHouseBuilder.ShedMinX + 0.35f, houseOrigin.x + PrototypeHouseBuilder.ShedMaxX - 0.35f,
                houseOrigin.z + PrototypeHouseBuilder.TunnelMinZ + 0.35f, houseOrigin.z + PrototypeHouseBuilder.TunnelMaxZ - 0.35f);
            CutRect(holes, res, terrain,
                houseOrigin.x + PrototypeHouseBuilder.ShedMinX + PrototypeHouseBuilder.Tile + 0.35f, houseOrigin.x + PrototypeHouseBuilder.ShedMaxX - 0.35f,
                houseOrigin.z + PrototypeHouseBuilder.ShedMinZ + 0.35f, houseOrigin.z + PrototypeHouseBuilder.TunnelMinZ + 0.35f);

            data.SetHoles(0, 0, holes);

            // Les trous ne retirent pas l'herbe instanciee : on vide aussi les couches de
            // details sous l'abri (la maison est deja couverte par le rayon d'exclusion du terrain).
            ClearDetailsInRect(terrain,
                houseOrigin.x + PrototypeHouseBuilder.ShedMinX, houseOrigin.x + PrototypeHouseBuilder.ShedMaxX,
                houseOrigin.z + PrototypeHouseBuilder.TunnelMinZ, houseOrigin.z + PrototypeHouseBuilder.TunnelMaxZ);
            ClearDetailsInRect(terrain,
                houseOrigin.x + PrototypeHouseBuilder.ShedMinX + PrototypeHouseBuilder.Tile, houseOrigin.x + PrototypeHouseBuilder.ShedMaxX,
                houseOrigin.z + PrototypeHouseBuilder.ShedMinZ, houseOrigin.z + PrototypeHouseBuilder.TunnelMinZ);
        }

        /// <summary>Supprime les details (herbe, buissons) du terrain dans un rectangle monde, avec 0.5 m de marge.</summary>
        private static void ClearDetailsInRect(Terrain terrain, float x0, float x1, float z0, float z1)
        {
            TerrainData data = terrain.terrainData;
            int res = data.detailResolution;
            Vector3 size = data.size;
            Vector3 origin = terrain.transform.position;

            int ix0 = Mathf.Clamp(Mathf.FloorToInt((x0 - 0.5f - origin.x) / size.x * res), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((x1 + 0.5f - origin.x) / size.x * res), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt((z0 - 0.5f - origin.z) / size.z * res), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt((z1 + 0.5f - origin.z) / size.z * res), 0, res - 1);
            int w = ix1 - ix0 + 1, h = iz1 - iz0 + 1;

            for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            {
                data.SetDetailLayer(ix0, iz0, layer, new int[h, w]);
            }
        }

        private static void CutRect(bool[,] holes, int res, Terrain terrain, float x0, float x1, float z0, float z1)
        {
            Vector3 size = terrain.terrainData.size;
            Vector3 origin = terrain.transform.position;

            int ix0 = Mathf.Clamp(Mathf.RoundToInt((x0 - origin.x) / size.x * res), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.RoundToInt((x1 - origin.x) / size.x * res), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.RoundToInt((z0 - origin.z) / size.z * res), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.RoundToInt((z1 - origin.z) / size.z * res), 0, res - 1);

            for (int z = iz0; z <= iz1; z++)
            {
                for (int x = ix0; x <= ix1; x++)
                {
                    holes[z, x] = false;
                }
            }
        }

        // ------------------------------------------------------------------
        // Rochers
        // ------------------------------------------------------------------

        private static void ScatterRocks(Transform environment, Terrain terrain, Vector3 houseOrigin)
        {
            Transform rocks = new GameObject("Rocks").transform;
            rocks.SetParent(environment, false);

            string[] names = { "Large Rock 1", "Large Rock 2", "Rock Group", "Small Rock 1", "Small Rock 2", "Small Rock 3", "Small Rock 4 Moss" };
            List<GameObject> prefabs = new List<GameObject>();

            foreach (string n in names)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JpPrefabs + n + ".prefab");

                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            if (prefabs.Count == 0)
            {
                return;
            }

            Random.InitState(4242);
            int placed = 0;

            for (int attempt = 0; attempt < 400 && placed < 90; attempt++)
            {
                float x = Random.Range(8f, PrototypeTerrainBuilder.TerrainSize - 8f);
                float z = Random.Range(8f, PrototypeTerrainBuilder.TerrainSize - 8f);

                float toCenter = Vector2.Distance(new Vector2(x, z), PrototypeTerrainBuilder.Center);

                if (toCenter < PrototypeTerrainBuilder.PlateauRadius + 2f || PrototypeTerrainBuilder.DistanceToPath(x, z) < 3.5f)
                {
                    continue;
                }

                GameObject prefab = prefabs[Random.Range(0, prefabs.Count)];
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

                if (instance == null)
                {
                    continue;
                }

                float y = PrototypeTerrainBuilder.SampleHeight(terrain, x, z) - 0.1f;

                instance.transform.SetParent(rocks, false);
                instance.transform.position = new Vector3(x, y, z);
                instance.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                instance.transform.localScale = Vector3.one * Random.Range(0.8f, 1.5f);

                GameObjectUtility.SetStaticEditorFlags(instance, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                placed++;
            }

            // Quelques rochers autour de la maison, sur le plateau.
            for (int i = 0; i < 10; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float radius = Random.Range(14f, PrototypeTerrainBuilder.PlateauRadius - 2f);
                float x = PrototypeTerrainBuilder.Center.x + Mathf.Cos(angle) * radius;
                float z = PrototypeTerrainBuilder.Center.y + Mathf.Sin(angle) * radius;

                if (PrototypeTerrainBuilder.DistanceToPath(x, z) < 3.5f)
                {
                    continue;
                }

                GameObject prefab = prefabs[Random.Range(3, prefabs.Count)];
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

                if (instance == null)
                {
                    continue;
                }

                instance.transform.SetParent(rocks, false);
                instance.transform.position = new Vector3(x, PrototypeTerrainBuilder.PlateauHeight - 0.05f, z);
                instance.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }

            Debug.Log("[Level] Rochers places : " + rocks.childCount);
        }

        // ------------------------------------------------------------------
        // Mise en scene : spots et props dans la maison
        // ------------------------------------------------------------------

        private static void AddHorrorDressing(Transform house, Vector3 houseOrigin)
        {
            Transform dressing = new GameObject("HorrorDressing").transform;
            dressing.SetParent(house, false);

            // Spots (coordonnees locales a la maison, grille de 2.5 m).
            Spot(dressing, "Spot_Phone_Hall", new Vector3(8.05f, 0.95f, 2.75f), "phone");
            Spot(dressing, "Spot_Radio_Kitchen", new Vector3(13.5f, 1.05f, 11.95f), "radio");
            Spot(dressing, "Spot_Silhouette_CorridorNorth", new Vector3(8.75f, 0.1f, 8.75f), "silhouette");
            Spot(dressing, "Spot_Silhouette_Corridor1", new Vector3(8.75f, 2.6f, 6.25f), "silhouette");
            Spot(dressing, "Spot_Silhouette_Tunnel", new Vector3(27f, -2.4f, 8.75f), "silhouette");
            Spot(dressing, "Spot_Silhouette_Attic", new Vector3(13.75f, 5.1f, 6.25f), "silhouette");
            Spot(dressing, "Spot_Radio_Workshop", new Vector3(12.5f, -1.4f, 0.75f), "radio");

            // Props physiques : livres, boites, bocaux (poses sur le mobilier).
            Prop(dressing, "Prop_Book_Office", new Vector3(0.9f, 0.9f, 4f), new Vector3(0.22f, 0.05f, 0.3f), PrimitiveType.Cube);
            Prop(dressing, "Prop_Book_Living", new Vector3(17.15f, 2.35f, 2f), new Vector3(0.2f, 0.05f, 0.28f), PrimitiveType.Cube);
            Prop(dressing, "Prop_Can_Kitchen", new Vector3(12.5f, 1.15f, 11.95f), new Vector3(0.12f, 0.18f, 0.12f), PrimitiveType.Cylinder);
            Prop(dressing, "Prop_Vase_Pantry", new Vector3(17.15f, 2.25f, 8.5f), new Vector3(0.14f, 0.22f, 0.14f), PrimitiveType.Cylinder);
            Prop(dressing, "Prop_Box_Attic", new Vector3(16.3f, 6.6f, 10.8f), new Vector3(0.3f, 0.3f, 0.3f), PrimitiveType.Cube);
            Prop(dressing, "Prop_Jar_Storage", new Vector3(9.65f, -0.25f, 1.5f), new Vector3(0.12f, 0.16f, 0.12f), PrimitiveType.Cylinder);
            Prop(dressing, "Prop_Jar_Storage2", new Vector3(9.65f, -0.25f, 3.5f), new Vector3(0.12f, 0.16f, 0.12f), PrimitiveType.Cylinder);
            Prop(dressing, "Prop_Tool_Workshop", new Vector3(11.5f, -1.4f, 0.75f), new Vector3(0.08f, 0.08f, 0.35f), PrimitiveType.Cube);
            Prop(dressing, "Prop_Bottle_Cellar", new Vector3(1.1f, -0.15f, 6f), new Vector3(0.09f, 0.3f, 0.09f), PrimitiveType.Cylinder);
        }

        private static void Spot(Transform parent, string name, Vector3 localPosition, string tag)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            HorrorSpot spot = go.AddComponent<HorrorSpot>();
            SerializedObject so = new SerializedObject(spot);
            so.FindProperty("spotTag").stringValue = tag;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Prop(Transform parent, string name, Vector3 localPosition, Vector3 size, PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = 0.5f;
            body.linearDamping = 0.3f;
            body.angularDamping = 1.5f;

            go.AddComponent<HorrorProp>();
        }

        // ------------------------------------------------------------------
        // Nuit
        // ------------------------------------------------------------------

        internal static void SetupNight(Transform lighting)
        {
            // Lune : basse, froide, ombres douces.
            GameObject moonObject = new GameObject("Moon");
            moonObject.transform.SetParent(lighting, false);
            moonObject.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

            Light moon = moonObject.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.62f, 0.72f, 0.95f);
            moon.intensity = 0.2f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.92f;
            moon.shadowBias = 0.03f;
            moon.shadowNormalBias = 0.5f;

            // Ambiante presque noire : la lune et les ampoules font tout le travail.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.02f, 0.025f, 0.04f);
            RenderSettings.sun = moon;

            // Brouillard : au dela de 80 m, tout disparait dans la nuit.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.02f;
            RenderSettings.fogColor = FogColor;

            RenderSettings.skybox = CreateNightSky();

            CreateGlobalVolume(lighting);
        }

        private static Material CreateNightSky()
        {
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);

            if (sky == null)
            {
                Shader shader = Shader.Find("Skybox/Procedural");

                if (shader == null)
                {
                    return null;
                }

                sky = new Material(shader);
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }

            // Reglages appliques a chaque construction : reproductible.
            sky.SetFloat("_SunSize", 0f);
            sky.SetFloat("_SunSizeConvergence", 1f);
            sky.SetFloat("_AtmosphereThickness", 0.3f);
            sky.SetColor("_SkyTint", new Color(0.12f, 0.16f, 0.28f));
            sky.SetColor("_GroundColor", new Color(0.02f, 0.022f, 0.03f));
            sky.SetFloat("_Exposure", 0.1f);
            EditorUtility.SetDirty(sky);

            return sky;
        }

        private static void CreateGlobalVolume(Transform lighting)
        {
            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game", "Settings");
            }

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);

                ColorAdjustments color = profile.Add<ColorAdjustments>(true);
                color.postExposure.Override(-0.15f);
                color.contrast.Override(12f);
                color.saturation.Override(-28f);

                WhiteBalance balance = profile.Add<WhiteBalance>(true);
                balance.temperature.Override(-14f);

                Vignette vignette = profile.Add<Vignette>(true);
                vignette.intensity.Override(0.28f);
                vignette.smoothness.Override(0.45f);

                FilmGrain grain = profile.Add<FilmGrain>(true);
                grain.type.Override(FilmGrainLookup.Medium1);
                grain.intensity.Override(0.35f);

                Bloom bloom = profile.Add<Bloom>(true);
                bloom.intensity.Override(0.45f);
                bloom.threshold.Override(1.05f);

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            GameObject volumeObject = new GameObject("GlobalVolume_Night");
            volumeObject.transform.SetParent(lighting, false);

            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        // ------------------------------------------------------------------
        // Systemes et joueur
        // ------------------------------------------------------------------

        private static void SetupSystems(Terrain terrain)
        {
            Vector2 spawn = PrototypeTerrainBuilder.PlayerSpawnXZ;
            float y = PrototypeTerrainBuilder.SampleHeight(terrain, spawn.x, spawn.y) + 0.15f;
            SetupSystems(new Vector3(spawn.x, y, spawn.y), Quaternion.identity); // face au nord : la maison
        }

        /// <summary>Noyau, managers de niveau, menu de pause et joueur place en <paramref name="spawnPosition"/>. Reutilise par le manoir.</summary>
        internal static void SetupSystems(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            CoreSetupMenu.CreateCoreSystems();
            ObjectiveSetupMenu.CreateObjectiveManager();

            LightManager lights = LightSetupMenu.AddLightManager();
            SerializedObject lightsSo = new SerializedObject(lights);
            lightsSo.FindProperty("startPowered").boolValue = false;
            lightsSo.FindProperty("startWithFuse").boolValue = false;
            lightsSo.ApplyModifiedPropertiesWithoutUndo();

            HorrorSetupMenu.AddManager();

            // Menu de pause / fin de partie (Echap), EventSystem Input System.
            MenuSetupMenu.AddPauseMenu();

            PlayerSetupMenu.CreatePlayer();

            PlayerCharacter player = Object.FindAnyObjectByType<PlayerCharacter>(FindObjectsInactive.Include);

            if (player != null)
            {
                player.transform.position = spawnPosition;
                player.transform.rotation = spawnRotation;

                DoorSetupMenu.EnsureDoorDebugOnPlayer();

                // La camera du joueur voit loin dans le noir, mais pas plus que le brouillard.
                if (player.Camera != null)
                {
                    player.Camera.farClipPlane = 220f;
                    player.Camera.backgroundColor = FogColor;
                }
            }
        }

        private static void RegisterInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (s.path == ScenePath)
                {
                    MenuSetupMenu.EnsureSceneListOrder();
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Level] Scene ajoutee a la Scene List : " + ScenePath);

            // Le menu principal doit rester la premiere scene de la build.
            MenuSetupMenu.EnsureSceneListOrder();
        }
    }
}
