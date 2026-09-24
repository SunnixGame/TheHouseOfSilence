using System.Collections.Generic;
using System.Globalization;
using HouseOfSilence.Core;
using HouseOfSilence.Doors;
using HouseOfSilence.Level;
using HouseOfSilence.Lights;
using HouseOfSilence.Player;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace HouseOfSilence.EditorTools.Level
{
    /// <summary>
    /// Import du manoir modelise dans Blender (Blender/manor_builder.py -> manor_export.py).
    ///
    /// A l'import du FBX :
    ///  - reglages du ModelImporter (pas d'animation, UV2 de lightmap, axes bakes) ;
    ///  - materiaux remplaces par des materiaux URP du projet portant le meme nom
    ///    (Assets/_Game/Materials/Manor), crees au besoin ;
    ///  - les proprietes personnalisees "hos_*" de chaque objet sont copiees dans un
    ///    composant ManorMeta.
    /// </summary>
    public class ManorAssetPostprocessor : AssetPostprocessor
    {
        public const string FbxPath = "Assets/_Game/Art/Manor/Manor.fbx";

        /// <summary>Eglise de la foret (Blender/church_builder.py) : memes reglages et memes materiaux.</summary>
        public const string ChurchFbxPath = "Assets/_Game/Art/Church/Church.fbx";

        /// <summary>Cimetiere de la foret (Blender/cemetery_builder.py).</summary>
        public const string CemeteryFbxPath = "Assets/_Game/Art/Cemetery/Cemetery.fbx";

        public const string MaterialsFolder = "Assets/_Game/Materials/Manor";

        private static readonly Dictionary<string, Color> BaseColors = new Dictionary<string, Color>
        {
            { "M_Stone_Old", new Color(0.42f, 0.40f, 0.36f) },
            { "M_Wood_Dark", new Color(0.20f, 0.12f, 0.07f) },
            { "M_Wood_Worn", new Color(0.36f, 0.27f, 0.17f) },
            { "M_Plaster_Old", new Color(0.72f, 0.68f, 0.58f) },
            { "M_Wallpaper_Old", new Color(0.45f, 0.38f, 0.30f) },
            { "M_Metal_Rust", new Color(0.35f, 0.20f, 0.12f) },
            { "M_WoodFloor", new Color(0.33f, 0.22f, 0.12f) },
            { "M_Tile_Old", new Color(0.55f, 0.52f, 0.45f) },
            { "M_Concrete_Wet", new Color(0.25f, 0.26f, 0.25f) },
            { "M_Roof_Tiles", new Color(0.22f, 0.15f, 0.13f) },
            { "M_Glass_Dirty", new Color(0.55f, 0.65f, 0.65f, 0.45f) },
            { "M_Ground", new Color(0.12f, 0.16f, 0.10f) },
        };

        private const string PackTextures = "Assets/ModularHousePack1/Art/Textures/";

        /// <summary>Textures du ModularHousePack1 reutilisees : (albedo, normale, occlusion, echelle en m par repetition).</summary>
        private static readonly Dictionary<string, string[]> Textures = new Dictionary<string, string[]>
        {
            { "M_Stone_Old",     new[] { "BC_Concrete_002", "Nr_Concrete_002", "AO_Concrete_002", "2.0" } },
            { "M_Wood_Dark",     new[] { "BC_Wood_007", "Nr_Wood_007", "AO_Wood_007", "1.5" } },
            { "M_Wood_Worn",     new[] { "BC_Wood_001", "Nr_Wood_001", "AO_Wood_001", "1.5" } },
            { "M_Plaster_Old",   new[] { null, "Nr_Plaster_001", "AO_Plaster_001", "2.0" } },
            { "M_Wallpaper_Old", new[] { "BC_Clay_001", "Nr_Clay_001", null, "1.2" } },
            { "M_Metal_Rust",    new[] { "BC_Iron_002", "Nr_Iron_001", "AO_Iron_001", "1.0" } },
            { "M_WoodFloor",     new[] { "BC_Wood_001", "Nr_Wood_001", "AO_Wood_001", "2.0" } },
            { "M_Tile_Old",      new[] { "BC_Tiles_001", "Nr_Tiles_001", "AO_Tiles_001", "1.5" } },
            { "M_Concrete_Wet",  new[] { "BC_Concrete_001", "Nr_Concrete_001", "AO_Concrete_001", "2.5" } },
            { "M_Roof_Tiles",    new[] { "BC_Asphalt_001", "Nr_Asphalt_001", "AO_Asphalt_001", "1.5" } },   // grain sombre = ardoise vue de loin (Ext_Tile a des zones transparentes)
        };

        /// <summary>Teinte appliquee par-dessus l'albedo (les textures du pack sont neutres).</summary>
        private static readonly Dictionary<string, Color> Tints = new Dictionary<string, Color>
        {
            { "M_Stone_Old", new Color(0.62f, 0.58f, 0.52f) },
            { "M_Wood_Dark", new Color(0.45f, 0.32f, 0.22f) },
            { "M_Wood_Worn", new Color(0.7f, 0.6f, 0.48f) },
            { "M_Wallpaper_Old", new Color(0.55f, 0.45f, 0.36f) },
            { "M_Metal_Rust", new Color(0.7f, 0.5f, 0.38f) },
            { "M_WoodFloor", new Color(0.55f, 0.4f, 0.26f) },
            { "M_Tile_Old", new Color(0.75f, 0.72f, 0.65f) },
            { "M_Concrete_Wet", new Color(0.5f, 0.52f, 0.5f) },
            { "M_Roof_Tiles", new Color(0.42f, 0.44f, 0.5f) },
        };

        private bool IsManor
        {
            get
            {
                string path = assetPath.Replace('\\', '/');
                return path == FbxPath || path == ChurchFbxPath || path == CemeteryFbxPath;
            }
        }

        private void OnPreprocessModel()
        {
            if (!IsManor)
            {
                return;
            }

            ModelImporter importer = (ModelImporter)assetImporter;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.generateSecondaryUV = true;          // UV2 de lightmap : les meshes Blender n'ont pas d'UV
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.bakeAxisConversion = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.preserveHierarchy = true;
            importer.addCollider = false;
        }

        private Material OnAssignMaterialModel(Material material, Renderer renderer)
        {
            if (!IsManor)
            {
                return null;
            }

            // Interdit de creer des assets pendant un import : les materiaux sont crees a l'avance
            // par EnsureMaterials() (menu 1 - Reimport) ; ici on ne fait que les retrouver.
            return AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/" + material.name + ".mat");
        }

        /// <summary>Cree (hors import) les materiaux URP du manoir s'ils manquent.</summary>
        public static void EnsureMaterials()
        {
            foreach (string name in BaseColors.Keys)
            {
                GetOrCreateMaterial(name);
            }

            AssetDatabase.SaveAssets();
        }

        private void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] propNames, object[] values)
        {
            if (!IsManor)
            {
                return;
            }

            List<string> keys = new List<string>();
            List<string> vals = new List<string>();

            for (int i = 0; i < propNames.Length; i++)
            {
                if (!propNames[i].StartsWith("hos_"))
                {
                    continue;
                }

                keys.Add(propNames[i]);
                vals.Add(System.Convert.ToString(values[i], CultureInfo.InvariantCulture));
            }

            if (keys.Count > 0)
            {
                go.AddComponent<ManorMeta>().Set(keys, vals);
            }
        }

        public static Material GetOrCreateMaterial(string name)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat != null)
            {
                return mat;
            }

            if (!AssetDatabase.IsValidFolder("Assets/_Game/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/_Game", "Materials");
            }

            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game/Materials", "Manor");
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader != null ? shader : Shader.Find("Standard"));
            mat.name = name;

            Color color;
            if (!BaseColors.TryGetValue(name, out color))
            {
                color = Color.gray;
            }

            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", name == "M_Glass_Dirty" ? 0.7f : (name == "M_Concrete_Wet" ? 0.45f : 0.15f));
            ApplyTextures(mat, name);

            if (name == "M_Glass_Dirty")
            {
                // Transparence URP (Surface Type = Transparent)
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Albedo / normale / occlusion du pack modulaire, tuilees en espace monde (les UV du manoir sont en metres).</summary>
        private static void ApplyTextures(Material mat, string name)
        {
            string[] tex;

            if (!Textures.TryGetValue(name, out tex))
            {
                return;
            }

            float meters = float.Parse(tex[3], CultureInfo.InvariantCulture);
            Vector2 tiling = Vector2.one / meters;

            if (tex[0] != null)
            {
                Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(PackTextures + tex[0] + ".png");

                if (albedo != null)
                {
                    mat.SetTexture("_BaseMap", albedo);
                    mat.SetTextureScale("_BaseMap", tiling);
                }
            }

            Color tint;
            if (Tints.TryGetValue(name, out tint))
            {
                mat.SetColor("_BaseColor", tint);
            }

            if (tex[1] != null)
            {
                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(PackTextures + tex[1] + ".png");

                if (normal != null)
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.SetTextureScale("_BumpMap", tiling);
                    mat.SetFloat("_BumpScale", 1f);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            if (tex[2] != null)
            {
                Texture2D occlusion = AssetDatabase.LoadAssetAtPath<Texture2D>(PackTextures + tex[2] + ".png");

                if (occlusion != null)
                {
                    mat.SetTexture("_OcclusionMap", occlusion);
                    mat.SetFloat("_OcclusionStrength", 0.8f);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                }
            }
        }

        /// <summary>Reapplique les textures aux materiaux existants (menu), utile apres modification de la table.</summary>
        public static void RefreshMaterialTextures()
        {
            foreach (string name in BaseColors.Keys)
            {
                Material mat = GetOrCreateMaterial(name);
                ApplyTextures(mat, name);
                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>
    /// Construit une scene de test autour du manoir importe : colliders, flags statiques,
    /// portes cablees sur DoorBase (charniere, sens, angle), volumes de pieces, sol,
    /// nuit, systemes de jeu et joueur au perron.
    ///
    /// Menu : Tools > House of Silence > Level > Manor > ...
    /// </summary>
    public static class ManorSceneBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/Manor_Test.unity";
        public const string TerrainDataPath = "Assets/_Game/Scenes/Manor_Terrain.asset";
        public const string NavMeshAssetPath = "Assets/_Game/Scenes/Manor_NavMesh.asset";
        public const string MonsterAgentName = "Monster";

        /// <summary>Emprise du manoir en coordonnees Blender : x 0..52 (est), y 0..30 (nord), abri en (90..94, 35..44).</summary>
        private const float ManorWidth = 52f, ManorDepth = 30f;

        private const StaticEditorFlags StaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                                                      StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic |
                                                      StaticEditorFlags.ReflectionProbeStatic;

        private static readonly HashSet<string> MeshColliderTypes = new HashSet<string>
        {
            "wall_ext", "wall_int", "floor", "ceiling", "stairs", "column", "fireplace", "closet", "roof", "decor", "railing",
        };

        /// <summary>Le manoir est centre sur le plateau du terrain ; son terrain local (-1.4) coincide avec le plateau (13).</summary>
        public static Vector3 ManorOrigin
        {
            get
            {
                Vector2 c = PrototypeTerrainBuilder.Center;
                return new Vector3(c.x - ManorWidth * 0.5f, PrototypeTerrainBuilder.PlateauHeight + 1.4f, c.y - ManorDepth * 0.5f);
            }
        }

        /// <summary>Coordonnees Blender (x est, y nord, z haut) -> locales Unity du manoir.</summary>
        private static Vector3 L(float x, float y, float z)
        {
            return new Vector3(x, z, y);
        }

        [MenuItem("Tools/House of Silence/Level/Manor/1 - Reimport Manor FBX", false, 300)]
        public static void ReimportFbx()
        {
            ManorAssetPostprocessor.EnsureMaterials();
            AssetDatabase.ImportAsset(ManorAssetPostprocessor.FbxPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[Manor] FBX reimporte : " + ManorAssetPostprocessor.FbxPath);
        }

        [MenuItem("Tools/House of Silence/Level/Manor/3 - Refresh Manor Material Textures", false, 302)]
        public static void RefreshTextures()
        {
            ManorAssetPostprocessor.RefreshMaterialTextures();
            Debug.Log("[Manor] Textures des materiaux reappliquees.");
        }

        [MenuItem("Tools/House of Silence/Level/Manor/2 - Build Manor Test Scene", false, 301)]
        public static void BuildScene()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Manor] Arrete le Play Mode avant de construire la scene.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            ManorAssetPostprocessor.EnsureMaterials();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManorAssetPostprocessor.FbxPath);

            if (prefab == null)
            {
                Debug.LogError("[Manor] FBX introuvable : exporte d'abord depuis Blender (manor_export.py).");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 1. Terrain et foret (meme generateur que le prototype, asset TerrainData separe).
            Transform environment = new GameObject("--- Environment ---").transform;
            Terrain terrain = PrototypeTerrainBuilder.Build(environment, TerrainDataPath);

            // 2. Le manoir sur le plateau.
            GameObject manor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(manor, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            manor.name = "Manor";
            manor.transform.position = ManorOrigin;

            ManorStats stats = new ManorStats();
            Dictionary<string, Transform> roomMarkers = CollectRoomMarkers(manor.transform);

            SetupStaticsAndColliders(manor.transform, stats);
            SetupDoors(manor.transform, roomMarkers, stats);
            SetupRooms(manor.transform, stats);
            CleanupMarkers(manor.transform);

            ShapeTerrain(terrain, manor.transform);

            // 3. Lumieres, interrupteurs, objectifs, objets.
            SetupLights(manor.transform, stats);
            SetupGameplay(manor.transform, stats);

            // 4. Nuit, systemes, joueur sur le chemin de la foret (face au manoir).
            Transform lighting = new GameObject("--- Lighting ---").transform;
            PrototypeLevelBuilder.SetupNight(lighting);

            Vector2 spawnXZ = PrototypeTerrainBuilder.PlayerSpawnXZ;
            float spawnY = PrototypeTerrainBuilder.SampleHeight(terrain, spawnXZ.x, spawnXZ.y) + 0.15f;
            PrototypeLevelBuilder.SetupSystems(new Vector3(spawnXZ.x, spawnY, spawnXZ.y), Quaternion.identity);

            // 5. NavMesh du monstre.
            BakeNavMesh(manor, stats);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(string.Format("[Manor] Scene construite : {0} colliders mesh, {1} colliders boite, {2} portes ({3} cassables), {4} pieces, " +
                                    "{5} ampoules, {6} interrupteurs, {7} objets statiques, navmesh {8}.",
                stats.meshColliders, stats.boxColliders, stats.doors, stats.breakable, stats.rooms, stats.bulbs, stats.switches, stats.statics, stats.navmesh));
        }

        // ------------------------------------------------------------------

        private class ManorStats
        {
            public int meshColliders, boxColliders, doors, breakable, rooms, statics, bulbs, switches;
            public string navmesh = "non";
        }

        private class RoomInfo
        {
            public string level, name;
            public float x0, x1, y0, y1, z, h;   // coordonnees Blender
            public Transform transform;
            public float Cx { get { return (x0 + x1) * 0.5f; } }
            public float Cy { get { return (y0 + y1) * 0.5f; } }
        }

        private static Dictionary<string, Transform> CollectRoomMarkers(Transform root)
        {
            Dictionary<string, Transform> markers = new Dictionary<string, Transform>();

            foreach (ManorMeta meta in root.GetComponentsInChildren<ManorMeta>(true))
            {
                if (meta.name.StartsWith("GP_Nav_") && meta.Has("hos_room"))
                {
                    markers[meta.Level + "/" + meta.Room] = meta.transform;
                }
            }

            return markers;
        }

        private static List<RoomInfo> CollectRooms(Transform root)
        {
            List<RoomInfo> rooms = new List<RoomInfo>();

            foreach (ManorMeta meta in root.GetComponentsInChildren<ManorMeta>(true))
            {
                if (!meta.name.StartsWith("Room_") || !meta.Has("hos_w"))
                {
                    continue;
                }

                Vector3 p = meta.transform.localPosition;   // (x, z_haut, y_nord) locale au manoir
                float w = meta.GetFloat("hos_w"), d = meta.GetFloat("hos_d");
                rooms.Add(new RoomInfo
                {
                    level = meta.Level, name = meta.Room, transform = meta.transform,
                    x0 = p.x - w * 0.5f, x1 = p.x + w * 0.5f, y0 = p.z - d * 0.5f, y1 = p.z + d * 0.5f,
                    z = p.y - 0.1f, h = meta.GetFloat("hos_h", 2.8f),
                });
            }

            return rooms;
        }

        private static void SetupStaticsAndColliders(Transform root, ManorStats stats)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("H_"))
                {
                    t.name = t.name.Substring(2);
                }
            }

            foreach (ManorMeta meta in root.GetComponentsInChildren<ManorMeta>(true))
            {
                string type = meta.Type;
                GameObject go = meta.gameObject;

                if (type == "door")
                {
                    continue;
                }

                MeshFilter filter = go.GetComponent<MeshFilter>();

                if (filter != null && filter.sharedMesh != null)
                {
                    if (MeshColliderTypes.Contains(type))
                    {
                        MeshCollider collider = go.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        stats.meshColliders++;
                    }
                    else if (type == "window")
                    {
                        BoxCollider box = go.AddComponent<BoxCollider>();
                        box.center = filter.sharedMesh.bounds.center;
                        box.size = filter.sharedMesh.bounds.size;
                        stats.boxColliders++;
                    }
                    else if (type == "stairs_ramp")
                    {
                        // rampe de collision invisible (echelles raides) : collider seul, pas de rendu
                        MeshCollider collider = go.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        go.GetComponent<MeshRenderer>().enabled = false;
                        stats.meshColliders++;
                    }

                    GameObjectUtility.SetStaticEditorFlags(go, StaticFlags);
                    stats.statics++;
                }
            }
        }

        // ------------------------------------------------------------------
        // Portes
        // ------------------------------------------------------------------

        private static void SetupDoors(Transform root, Dictionary<string, Transform> roomMarkers, ManorStats stats)
        {
            List<ManorMeta> doors = new List<ManorMeta>();

            foreach (ManorMeta meta in root.GetComponentsInChildren<ManorMeta>(true))
            {
                if (meta.Type == "door" && meta.GetComponent<MeshFilter>() != null)
                {
                    doors.Add(meta);
                }
            }

            foreach (ManorMeta meta in doors)
            {
                BuildDoor(meta, roomMarkers, stats);
            }
        }

        /// <summary>
        /// Transforme le battant importe (origine sur l'axe des gonds, battant le long de +X)
        /// en hierarchie DoorBase : Door (script) > Hinge (pivot) > Panel (mesh + BoxCollider).
        /// </summary>
        private static void BuildDoor(ManorMeta meta, Dictionary<string, Transform> roomMarkers, ManorStats stats)
        {
            GameObject panel = meta.gameObject;
            string kind = meta.DoorKind;
            float width = meta.GetFloat("hos_width", 0.9f);
            float angle = Mathf.Clamp(meta.GetFloat("hos_open_angle", 95f), 30f, 170f);

            // Le battant importe garde le repere Blender (Z local = hauteur, +Y local = normale du
            // battant, +X local = du gond vers le bord libre). La racine DoorBase, elle, doit etre
            // debout : Y = axe des gonds, forward = normale horizontale du battant.
            Vector3 normal = panel.transform.up;
            normal.y = 0f;

            if (normal.sqrMagnitude < 1e-4f)
            {
                normal = Vector3.forward;
            }

            GameObject rootGo = new GameObject(panel.name);
            rootGo.transform.SetParent(panel.transform.parent, false);
            rootGo.transform.SetPositionAndRotation(panel.transform.position, Quaternion.LookRotation(normal.normalized, Vector3.up));

            GameObject hingeGo = new GameObject("Hinge");
            hingeGo.transform.SetParent(rootGo.transform, false);

            // Les portes ordinaires s'ouvrent du cote oppose au joueur (comme dans le prototype).
            // Placards, trappes, portes secretes et cloisons ont un sens impose : on oriente la
            // charniere pour qu'une rotation +Y positive parte vers la piece cible.
            bool awayFromInstigator = kind != "closet" && kind != "trapdoor" && kind != "secret" && kind != "breakable";

            if (!awayFromInstigator)
            {
                Vector3 target;

                if (kind == "trapdoor")
                {
                    hingeGo.transform.localRotation = Quaternion.Euler(0f, 0f, -90f); // axe des gonds horizontal
                    target = rootGo.transform.position + Vector3.up * 3f;
                }
                else
                {
                    Transform marker;
                    target = roomMarkers.TryGetValue(meta.Level + "/" + meta.Get("hos_opens_into"), out marker)
                        ? marker.position
                        : rootGo.transform.position + rootGo.transform.forward;
                }

                // bord libre du battant : deux fois le centre du mesh (le gond est a l'origine), quel que soit le sens du battant
                Vector3 lever = panel.transform.TransformVector(panel.GetComponent<MeshFilter>().sharedMesh.bounds.center) * 2f;
                Vector3 axis = hingeGo.transform.up;
                Vector3 endPlus = rootGo.transform.position + Quaternion.AngleAxis(angle, axis) * lever;
                Vector3 endMinus = rootGo.transform.position + Quaternion.AngleAxis(-angle, axis) * lever;

                if ((endMinus - target).sqrMagnitude < (endPlus - target).sqrMagnitude)
                {
                    hingeGo.transform.localRotation = hingeGo.transform.localRotation * Quaternion.Euler(180f, 0f, 0f);
                }
            }

            panel.transform.SetParent(hingeGo.transform, true);
            panel.name = "Panel";

            MeshFilter filter = panel.GetComponent<MeshFilter>();
            BoxCollider box = panel.AddComponent<BoxCollider>();
            box.center = filter.sharedMesh.bounds.center;
            box.size = filter.sharedMesh.bounds.size;

            DoorBase door = AddDoorComponent(rootGo, kind, stats);

            EditorSetupUtility.SetObjectField(door, "hinge", hingeGo.transform);

            SerializedObject so = new SerializedObject(door);
            so.FindProperty("openAngle").floatValue = angle;
            so.FindProperty("openAwayFromInstigator").boolValue = awayFromInstigator;
            so.FindProperty("displayName").stringValue = DoorDisplayName(kind, meta);

            if (kind == "secret")
            {
                so.FindProperty("openPromptText").stringValue = "Pousser";
                so.FindProperty("openSpeed").floatValue = 60f;
                so.FindProperty("closeSpeed").floatValue = 60f;
            }
            else if (kind == "bars" || kind == "metal")
            {
                so.FindProperty("noiseRadius").floatValue = 14f;
                so.FindProperty("slamNoiseRadius").floatValue = 30f;
            }
            else if (kind == "closet")
            {
                so.FindProperty("noiseRadius").floatValue = 5f;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            // Le monstre traverse les portes (il les ouvre) : le battant ne coupe pas le navmesh.
            rootGo.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            // Les metadonnees remontent sur la racine (le battant garde son mesh seulement).
            ManorMeta rootMeta = rootGo.AddComponent<ManorMeta>();
            List<string> keys = new List<string>();
            List<string> values = new List<string>();

            foreach (KeyValuePair<string, string> kv in meta.All())
            {
                keys.Add(kv.Key);
                values.Add(kv.Value);
            }

            rootMeta.Set(keys, values);
            Object.DestroyImmediate(meta);
            stats.doors++;
        }

        /// <summary>Type de porte selon son role dans le scenario : cle du sous-sol, portes du symbole, cloison fragile.</summary>
        private static DoorBase AddDoorComponent(GameObject rootGo, string kind, ManorStats stats)
        {
            string name = rootGo.name;

            if (kind == "breakable")
            {
                stats.breakable++;
                return rootGo.AddComponent<BreakableDoor>();
            }

            if (name == "GF_Door_EscSousSol_CouloirService")
            {
                KeyDoor key = rootGo.AddComponent<KeyDoor>();
                PrototypeHouseBuilder.SetString(key, "lockId", "basement");
                PrototypeHouseBuilder.SetString(key, "missingKeyPrompt", "Verrouillee - il faut la cle du sous-sol");
                return key;
            }

            if (name == "B1_Door_CaveAVin_SalleRituelle" || name == "GF_Door_Bibliotheque_PassageSecret")
            {
                ObjectiveDoor objective = rootGo.AddComponent<ObjectiveDoor>();
                PrototypeHouseBuilder.SetString(objective, "requiredObjectiveId", "find_symbol");
                PrototypeHouseBuilder.SetString(objective, "blockedMessage", "Un symbole est grave dans le bois. Rien ne bouge.");
                return objective;
            }

            if (name == "SHED_Door_AbriSortie_ExtS")
            {
                Door exit = rootGo.AddComponent<Door>();
                PrototypeHouseBuilder.SetString(exit, "openPromptText", "Sortir");
                return exit;
            }

            return rootGo.AddComponent<Door>();
        }

        private static string DoorDisplayName(string kind, ManorMeta meta)
        {
            switch (kind)
            {
                case "bars": return "Grille";
                case "closet": return "Armoire";
                case "trapdoor": return "Trappe";
                case "secret": return "Rayonnage";
                case "breakable": return "Cloison fragile";
                case "hatch": return "Portillon";
                case "entrance": return "Porte d'entree";
                case "metal": return "Porte metallique";
                default:
                    string into = meta.Get("hos_opens_into");
                    return string.IsNullOrEmpty(into) ? "Porte" : "Porte : " + Prettify(into);
            }
        }

        private static string Prettify(string roomName)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for (int i = 0; i < roomName.Length; i++)
            {
                char c = roomName[i];

                if (i > 0 && char.IsUpper(c) && char.IsLower(roomName[i - 1]))
                {
                    sb.Append(' ');
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Pieces
        // ------------------------------------------------------------------

        private static void SetupRooms(Transform root, ManorStats stats)
        {
            foreach (ManorMeta meta in root.GetComponentsInChildren<ManorMeta>(true))
            {
                if (!meta.name.StartsWith("GP_Nav_") || !meta.Has("hos_w"))
                {
                    continue;
                }

                float w = meta.GetFloat("hos_w", 2f);
                float d = meta.GetFloat("hos_d", 2f);
                float h = meta.GetFloat("hos_h", 2.5f);

                GameObject go = meta.gameObject;
                go.name = "Room_" + meta.Level + "_" + meta.Room;

                BoxCollider box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = new Vector3(0f, h * 0.5f - 0.1f, 0f);
                box.size = new Vector3(w, h, d);

                RoomVolume room = go.AddComponent<RoomVolume>();
                room.Configure(Prettify(meta.Room), meta.GetInt("hos_floor_index", 0));
                stats.rooms++;
            }
        }

        private static void CleanupMarkers(Transform root)
        {
            // Les points de porte doublonnent les portes cablees : on les retire.
            List<GameObject> toRemove = new List<GameObject>();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("GP_Door_"))
                {
                    toRemove.Add(t.gameObject);
                }
            }

            foreach (GameObject go in toRemove)
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------
        // Terrain : trous sous la maison et l'abri, replat de l'abri, arbres et herbe retires
        // ------------------------------------------------------------------

        private static void ShapeTerrain(Terrain terrain, Transform manor)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = manor.position;

            // Trous (le terrain ne doit pas traverser le sous-sol ni la cage de l'abri).
            int res = data.holesResolution;
            bool[,] holes = data.GetHoles(0, 0, res, res);
            CutRect(holes, res, terrain, origin.x + 0.6f, origin.x + ManorWidth - 0.6f, origin.z + 0.6f, origin.z + ManorDepth - 0.6f);
            CutRect(holes, res, terrain, origin.x + 90.3f, origin.x + 93.7f, origin.z + 35.3f, origin.z + 43.7f);
            data.SetHoles(0, 0, holes);

            // Replat autour de l'abri de sortie (hors du plateau), au niveau du sol du manoir, et
            // remblai le long du tunnel : hors du plateau le terrain redescend et traverserait le tunnel.
            Vector3 shed = origin + L(92f, 39.5f, 0f);
            FlattenDisc(terrain, shed.x, shed.z, 12f, 22f, PrototypeTerrainBuilder.PlateauHeight);
            Vector3[] tunnel = { origin + L(52f, 27f, 0f), origin + L(75f, 27f, 0f), origin + L(75f, 39f, 0f), origin + L(92f, 39f, 0f) };

            for (int i = 0; i + 1 < tunnel.Length; i++)
            {
                FlattenSegment(terrain, tunnel[i], tunnel[i + 1], 6f, 11f, PrototypeTerrainBuilder.PlateauHeight);
            }

            // Pas d'arbre dans l'emprise (perron, terrasse et marches comprises) ni autour de l'abri.
            List<TreeInstance> trees = new List<TreeInstance>(data.treeInstances);
            Rect footprint = Rect.MinMaxRect(origin.x - 8f, origin.z - 10f, origin.x + ManorWidth + 12f, origin.z + ManorDepth + 6f);
            trees.RemoveAll(t =>
            {
                Vector3 p = Vector3.Scale(t.position, data.size) + terrain.transform.position;
                return footprint.Contains(new Vector2(p.x, p.z)) || Vector2.Distance(new Vector2(p.x, p.z), new Vector2(shed.x, shed.z)) < 13f;
            });
            data.SetTreeInstances(trees.ToArray(), true);

            // Ni herbe ni buissons sous la maison et l'abri.
            ClearDetails(terrain, origin.x - 1f, origin.x + ManorWidth + 1f, origin.z - 8f, origin.z + ManorDepth + 4f);
            ClearDetails(terrain, shed.x - 4f, shed.x + 4f, shed.z - 7f, shed.z + 7f);

            EditorUtility.SetDirty(data);
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

        private static void FlattenDisc(Terrain terrain, float cx, float cz, float flatRadius, float blendRadius, float height)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            Vector3 size = data.size;
            Vector3 origin = terrain.transform.position;
            int ix0 = Mathf.Clamp(Mathf.FloorToInt((cx - blendRadius - origin.x) / size.x * (res - 1)), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((cx + blendRadius - origin.x) / size.x * (res - 1)), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt((cz - blendRadius - origin.z) / size.z * (res - 1)), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt((cz + blendRadius - origin.z) / size.z * (res - 1)), 0, res - 1);
            float[,] heights = data.GetHeights(ix0, iz0, ix1 - ix0 + 1, iz1 - iz0 + 1);
            float target = (height - origin.y) / size.y;

            for (int z = 0; z < heights.GetLength(0); z++)
            {
                for (int x = 0; x < heights.GetLength(1); x++)
                {
                    float wx = origin.x + (ix0 + x) / (float)(res - 1) * size.x;
                    float wz = origin.z + (iz0 + z) / (float)(res - 1) * size.z;
                    float dist = Vector2.Distance(new Vector2(wx, wz), new Vector2(cx, cz));
                    float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flatRadius, blendRadius, dist));
                    heights[z, x] = Mathf.Lerp(heights[z, x], target, w);
                }
            }

            data.SetHeights(ix0, iz0, heights);
        }

        /// <summary>Remblai : le terrain est remonte a <paramref name="height"/> le long d'un segment (jamais abaisse).</summary>
        private static void FlattenSegment(Terrain terrain, Vector3 a, Vector3 b, float flatRadius, float blendRadius, float height)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            Vector3 size = data.size;
            Vector3 origin = terrain.transform.position;
            float minX = Mathf.Min(a.x, b.x) - blendRadius, maxX = Mathf.Max(a.x, b.x) + blendRadius;
            float minZ = Mathf.Min(a.z, b.z) - blendRadius, maxZ = Mathf.Max(a.z, b.z) + blendRadius;
            int ix0 = Mathf.Clamp(Mathf.FloorToInt((minX - origin.x) / size.x * (res - 1)), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((maxX - origin.x) / size.x * (res - 1)), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt((minZ - origin.z) / size.z * (res - 1)), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - origin.z) / size.z * (res - 1)), 0, res - 1);
            float[,] heights = data.GetHeights(ix0, iz0, ix1 - ix0 + 1, iz1 - iz0 + 1);
            float target = (height - origin.y) / size.y;
            Vector2 pa = new Vector2(a.x, a.z), pb = new Vector2(b.x, b.z);

            for (int z = 0; z < heights.GetLength(0); z++)
            {
                for (int x = 0; x < heights.GetLength(1); x++)
                {
                    Vector2 p = new Vector2(origin.x + (ix0 + x) / (float)(res - 1) * size.x, origin.z + (iz0 + z) / (float)(res - 1) * size.z);
                    Vector2 ab = pb - pa;
                    float t = Mathf.Clamp01(Vector2.Dot(p - pa, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    float dist = Vector2.Distance(p, pa + ab * t);
                    float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flatRadius, blendRadius, dist));
                    float raised = Mathf.Lerp(heights[z, x], target, w);
                    heights[z, x] = Mathf.Max(heights[z, x], raised);
                }
            }

            data.SetHeights(ix0, iz0, heights);
        }

        private static void ClearDetails(Terrain terrain, float x0, float x1, float z0, float z1)
        {
            TerrainData data = terrain.terrainData;
            int res = data.detailResolution;
            Vector3 size = data.size;
            Vector3 origin = terrain.transform.position;
            int ix0 = Mathf.Clamp(Mathf.FloorToInt((x0 - origin.x) / size.x * res), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((x1 - origin.x) / size.x * res), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt((z0 - origin.z) / size.z * res), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt((z1 - origin.z) / size.z * res), 0, res - 1);

            for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            {
                data.SetDetailLayer(ix0, iz0, layer, new int[iz1 - iz0 + 1, ix1 - ix0 + 1]);
            }
        }

        // ------------------------------------------------------------------
        // Lumieres : une ampoule (ou plusieurs) par piece, un interrupteur pres d'une porte
        // ------------------------------------------------------------------

        private static readonly HashSet<string> NoLightRooms = new HashSet<string>
        {
            "PieceSecrete", "PuitsSecret", "PassageSecret", "CombleOuest", "CombleEst", "CombleSud", "CombleSO", "CombleSE",
            "Cellule1", "Cellule2", "Cellule3", "Cellule4", "Cellule5", "Cellule6", "PieceCachee", "AbriEscalier",
        };

        private static readonly HashSet<string> CirculationRooms = new HashSet<string>
        {
            "GrandCouloir", "CouloirService", "LongCouloir", "CouloirServiceF1", "CouloirEtroit", "CouloirNord", "CouloirSud",
            "CouloirCellules", "Hall", "BalconS", "BalconW", "BalconE", "Pont", "GalerieW", "GalerieE", "Grenier", "GrenierOuest", "GrenierNord",
        };

        private static void SetupLights(Transform manor, ManorStats stats)
        {
            Transform lights = new GameObject("Lights").transform;
            lights.SetParent(manor, false);
            lights.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;   // les ampoules ne coupent pas le navmesh
            Transform switches = new GameObject("Switches").transform;
            switches.SetParent(manor, false);

            List<RoomInfo> rooms = CollectRooms(manor);
            List<DoorBase> doors = new List<DoorBase>(manor.GetComponentsInChildren<DoorBase>(true));

            foreach (RoomInfo room in rooms)
            {
                if (NoLightRooms.Contains(room.name))
                {
                    continue;
                }

                if (room.name.StartsWith("Tunnel") || room.name == "EscSecondaire")
                {
                    // Lampes de secours du tunnel : autonomes, faibles, capricieuses.
                    float step = 8f;
                    float length = Mathf.Max(room.x1 - room.x0, room.y1 - room.y0);
                    int n = Mathf.Max(1, Mathf.RoundToInt(length / step));

                    for (int i = 0; i < n; i++)
                    {
                        float t = (i + 0.5f) / n;
                        float x = room.x1 - room.x0 > room.y1 - room.y0 ? Mathf.Lerp(room.x0, room.x1, t) : room.Cx;
                        float y = room.x1 - room.x0 > room.y1 - room.y0 ? room.Cy : Mathf.Lerp(room.y0, room.y1, t);
                        PrototypeHouseBuilder.Bulb(lights, "Bulb_" + room.name + "_" + (i + 1), L(x, y, room.z + room.h - 0.2f),
                            new Color(1f, 0.75f, 0.45f), 0.55f, 6f, true, false, 3f);
                        stats.bulbs++;
                    }

                    continue;
                }

                Color color;
                float intensity, range, failures;
                bool startOn = true, requiresPower = true;

                switch (room.level)
                {
                    case "B1": color = new Color(1f, 0.82f, 0.55f); intensity = 0.9f; range = 6.5f; failures = 2f; break;
                    case "F1": color = new Color(1f, 0.8f, 0.55f); intensity = 1.0f; range = 7f; failures = 1f; break;
                    case "F2": color = new Color(1f, 0.7f, 0.45f); intensity = 0.7f; range = 6f; failures = 4f; startOn = false; break;
                    case "SHED": color = new Color(1f, 0.75f, 0.45f); intensity = 0.6f; range = 5f; failures = 3f; requiresPower = false; break;
                    default: color = new Color(1f, 0.85f, 0.6f); intensity = 1.2f; range = 8f; failures = 0.5f; break;
                }

                if (room.name == "SalleRituelle")
                {
                    // Bougies : jamais dependantes du courant, rouges, tremblantes.
                    color = new Color(1f, 0.35f, 0.2f); intensity = 0.8f; range = 6f; failures = 8f; requiresPower = false;
                }

                float w = room.x1 - room.x0, d = room.y1 - room.y0;
                float height = room.z + room.h - 0.4f;

                if (room.name == "Hall")
                {
                    // Lustre du hall a mi-hauteur du vide (7.4 m) + deux bougeoirs autonomes a l'entree.
                    PrototypeHouseBuilder.Bulb(lights, "Bulb_Hall_Chandelier", L(room.Cx, 8f, 6.2f), color, 2.2f, 16f, true, true, 0.3f);
                    PrototypeHouseBuilder.Bulb(lights, "Bulb_Hall_Candle_W", L(21f, 2f, 1.8f), new Color(1f, 0.6f, 0.3f), 0.45f, 5f, true, false, 6f);
                    PrototypeHouseBuilder.Bulb(lights, "Bulb_Hall_Candle_E", L(31f, 2f, 1.8f), new Color(1f, 0.6f, 0.3f), 0.45f, 5f, true, false, 6f);
                    stats.bulbs += 3;
                    continue;
                }

                // Longs couloirs et grandes pieces : une ampoule tous les 8 m le long du grand axe.
                float longSide = Mathf.Max(w, d);
                int count = longSide > 9f ? Mathf.Max(2, Mathf.RoundToInt(longSide / 8f)) : 1;

                for (int i = 0; i < count; i++)
                {
                    float t = (i + 0.5f) / count;
                    float x = w >= d ? Mathf.Lerp(room.x0, room.x1, t) : room.Cx;
                    float y = w >= d ? room.Cy : Mathf.Lerp(room.y0, room.y1, t);
                    string bulbName = "Bulb_" + room.level + "_" + room.name + (count > 1 ? "_" + (i + 1) : "");
                    PrototypeHouseBuilder.Bulb(lights, bulbName, L(x, y, height), color, intensity, range, startOn, requiresPower, failures);
                    stats.bulbs++;
                }

                if (requiresPower && !CirculationRooms.Contains(room.name))
                {
                    if (PlaceSwitch(switches, room, doors))
                    {
                        stats.switches++;
                    }
                }
            }

            // Lampe du porche (exterieure, autonome).
            PrototypeHouseBuilder.Bulb(lights, "Bulb_Porch", L(26f, -2.2f, 3.4f), new Color(1f, 0.75f, 0.45f), 1.0f, 8f, true, false, 2.5f);
            stats.bulbs++;

            // URP n'a qu'un atlas d'ombres pour les lumieres ponctuelles : plus d'une centaine
            // d'ampoules avec ombres douces le saturent. Seuls le lustre du hall et les bougies
            // de la salle rituelle gardent leurs ombres.
            foreach (Light light in lights.GetComponentsInChildren<Light>(true))
            {
                bool keepShadows = light.name == "Bulb_Hall_Chandelier" || light.name.Contains("SalleRituelle");
                light.shadows = keepShadows ? LightShadows.Soft : LightShadows.None;
            }
        }

        /// <summary>Interrupteur a cote du gond de la premiere porte qui ouvre sur la piece, cote piece.</summary>
        private static bool PlaceSwitch(Transform parent, RoomInfo room, List<DoorBase> doors)
        {
            foreach (DoorBase door in doors)
            {
                ManorMeta meta = door.GetComponent<ManorMeta>();

                if (meta == null || meta.Level != room.level || meta.Get("hos_opens_into") != room.name)
                {
                    continue;
                }

                string kind = meta.DoorKind;

                if (kind == "closet" || kind == "trapdoor" || kind == "bars" || kind == "secret" || kind == "breakable")
                {
                    continue;
                }

                Transform t = door.transform;
                Vector3 roomCenter = room.transform.position;
                float side = Vector3.Dot(t.forward, roomCenter - t.position) >= 0f ? 1f : -1f;
                Vector3 pos = t.position - t.right * 0.4f + t.forward * side * 0.1f + Vector3.up * 1.3f;

                GameObject sw = LightSetupMenu.BuildSwitch("Switch_" + room.level + "_" + room.name, Vector3.zero, new Vector3(0.06f, 0.16f, 0.1f));
                sw.transform.SetParent(parent, false);
                sw.transform.position = pos;
                sw.transform.rotation = Quaternion.LookRotation(t.forward * side, Vector3.up);

                LightSwitch component = sw.AddComponent<LightSwitch>();
                float radius = Mathf.Clamp(Mathf.Max(room.x1 - room.x0, room.y1 - room.y0) * 0.5f + 0.5f, 3f, 6.5f);
                LightSetupMenu.SetFloat(component, "autoRadius", radius);
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Gameplay : tableau electrique, objectifs, objets, mobilier minimal
        // ------------------------------------------------------------------

        private static void SetupGameplay(Transform manor, ManorStats stats)
        {
            Transform gameplay = new GameObject("Gameplay").transform;
            gameplay.SetParent(manor, false);
            PrototypeHouseBuilder.LoadMaterials();
            Material wood = PrototypeHouseBuilder.WoodMaterial;
            Material stone = PrototypeHouseBuilder.StoneMaterial;

            // --- Local technique (sous-sol, 12..26 x 24..30) : tableau + disjoncteur sur le mur ouest.
            GameObject fuseBox = LightSetupMenu.BuildSwitch("FuseBox", Vector3.zero, new Vector3(0.12f, 0.7f, 0.5f));
            PrototypeHouseBuilder.Attach(fuseBox, gameplay, L(12.25f, 26.5f, -3.2f + 1.5f));
            FuseBox fuse = fuseBox.AddComponent<FuseBox>();
            EditorSetupUtility.SetObjectField(fuse, "requiredItem", PrototypeHouseBuilder.LoadItem("Item_Fuse"));
            PrototypeHouseBuilder.AttachObjective(fuseBox, "Objective_03_RepairFusebox");

            GameObject breaker = LightSetupMenu.BuildSwitch("PowerSwitch", Vector3.zero, new Vector3(0.12f, 0.5f, 0.3f));
            PrototypeHouseBuilder.Attach(breaker, gameplay, L(12.25f, 27.8f, -3.2f + 1.5f));
            breaker.AddComponent<PowerSwitch>();
            PrototypeHouseBuilder.AttachObjective(breaker, "Objective_04_RestorePower");

            // --- Zones d'objectif.
            PrototypeHouseBuilder.Zone(gameplay, "Zone_ExploreBasement", L(26f, 22.5f, -3.2f + 1.4f), new Vector3(6f, 2.6f, 2.6f), "Objective_05_ExploreBasement");
            PrototypeHouseBuilder.Zone(gameplay, "Zone_FindSymbol", L(46f, 11.5f, -3.2f + 1.4f), new Vector3(4f, 2.6f, 4f), "Objective_06_FindSymbol");
            PrototypeHouseBuilder.Zone(gameplay, "Zone_ReturnToExit", L(92f, 32f, -1.4f + 1.5f), new Vector3(5f, 3f, 4f), "Objective_08_ReturnToExit");

            // Le symbole grave, sur le mur est des archives.
            PrototypeHouseBuilder.Box(gameplay, "Symbol_Archives", 51.7f, 51.76f, -3.2f + 1.0f, -3.2f + 2.0f, 11f, 12f,
                ManorAssetPostprocessor.GetOrCreateMaterial("M_Metal_Rust"));

            // --- Objets (positions Blender x, y ; hauteur au-dessus du sol du niveau).
            PrototypeHouseBuilder.Box(gameplay, "HallTable", 23.6f, 24.4f, 0.0f, 0.8f, 3.6f, 4.4f, wood);
            PrototypeHouseBuilder.Pickup(gameplay, "Item_Flashlight", L(24f, 4f, 0.9f), 1);

            PrototypeHouseBuilder.Box(gameplay, "Desk_Bureau", 13f, 15f, 0.0f, 0.8f, 13.5f, 14.3f, wood);
            PrototypeHouseBuilder.Pickup(gameplay, "Item_Key_Basement", L(14f, 13.9f, 0.9f), 1);

            PrototypeHouseBuilder.Box(gameplay, "KitchenCounter", 35f, 44f, 0.0f, 0.95f, 29.0f, 29.65f, wood);
            PrototypeHouseBuilder.Pickup(gameplay, "Item_Fuse", L(40f, 29.3f, 1.05f), 1);

            PrototypeHouseBuilder.Box(gameplay, "Shelf_Cellier", 0.35f, 0.85f, 0.0f, 2.0f, 25f, 29f, wood);
            PrototypeHouseBuilder.Pickup(gameplay, "Item_Battery", L(2f, 27f, 0.3f), 2);

            PrototypeHouseBuilder.Pickup(gameplay, "Item_Crowbar", L(26f, 5f, -3.2f + 0.3f), 1);
            PrototypeHouseBuilder.Pickup(gameplay, "Item_Key_Office", L(27f, 27f, 4.2f + 0.3f), 1);

            PrototypeHouseBuilder.Box(gameplay, "Altar", 5.4f, 6.6f, -3.2f, -3.2f + 0.9f, 6.5f, 7.5f, stone);
            PrototypeHouseBuilder.Pickup(gameplay, "Item_CursedObject", L(6f, 7f, -3.2f + 1.0f), 1);

            // Un peu de mobilier pour lire les pieces (tables, lits, etageres).
            PrototypeHouseBuilder.Box(gameplay, "DiningTable", 34f, 42f, 0.0f, 0.78f, 13.8f, 15.2f, wood);
            PrototypeHouseBuilder.Box(gameplay, "Bed_Master", 36f, 38.2f, 4.2f, 4.2f + 0.6f, 1.0f, 3.1f, wood);
            PrototypeHouseBuilder.Box(gameplay, "Bed_Child", 2f, 3.1f, 4.2f, 4.2f + 0.55f, 15f, 17f, wood);
            PrototypeHouseBuilder.Box(gameplay, "Workbench", 13f, 16f, -3.2f, -3.2f + 0.95f, 3.0f, 3.9f, wood);
            PrototypeHouseBuilder.Box(gameplay, "Boiler", 2f, 4.2f, -3.2f, -3.2f + 2.2f, 27f, 29.2f, stone);
            PrototypeHouseBuilder.Box(gameplay, "MorgueTable", 44f, 46f, -3.2f, -3.2f + 0.85f, 26.5f, 27.4f, stone);
        }

        // ------------------------------------------------------------------
        // NavMesh du monstre (AI Navigation) : agent "Monster", rayon 0.3, pente 55, marche 0.4
        // ------------------------------------------------------------------

        private static void BakeNavMesh(GameObject manor, ManorStats stats)
        {
            int agentTypeId = EnsureMonsterAgentType();

            NavMeshSurface surface = manor.AddComponent<NavMeshSurface>();
            surface.agentTypeID = agentTypeId;
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(50f, 5f, 15f);         // local au manoir : maison, abords (chemin compris), tunnel et abri
            surface.size = new Vector3(170f, 50f, 150f);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.1f;
            surface.overrideTileSize = true;
            surface.tileSize = 128;

            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                stats.navmesh = "echec";
                return;
            }

            NavMeshData existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshAssetPath);

            if (existing != null)
            {
                AssetDatabase.DeleteAsset(NavMeshAssetPath);
            }

            surface.navMeshData.name = "Manor_NavMesh";
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAssetPath);
            EditorUtility.SetDirty(surface);
            stats.navmesh = "ok (" + NavMeshAssetPath + ")";
        }

        /// <summary>Type d'agent "Monster" dans les reglages de navigation du projet (cree au besoin).</summary>
        private static int EnsureMonsterAgentType()
        {
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                NavMeshBuildSettings s = NavMesh.GetSettingsByIndex(i);

                if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == MonsterAgentName)
                {
                    return s.agentTypeID;
                }
            }

            NavMeshBuildSettings created = NavMesh.CreateSettings();
            int id = created.agentTypeID;

            // Les reglages vivent dans ProjectSettings/NavMeshAreas.asset : on edite la derniere entree.
            Object settingsAsset = Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings");
            SerializedObject so = new SerializedObject(settingsAsset);
            SerializedProperty list = so.FindProperty("m_Settings");
            SerializedProperty names = so.FindProperty("m_SettingNames");

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);

                if (entry.FindPropertyRelative("agentTypeID").intValue != id)
                {
                    continue;
                }

                entry.FindPropertyRelative("agentRadius").floatValue = 0.3f;
                entry.FindPropertyRelative("agentHeight").floatValue = 1.9f;
                entry.FindPropertyRelative("agentSlope").floatValue = 55f;
                entry.FindPropertyRelative("agentClimb").floatValue = 0.4f;

                if (names != null && i < names.arraySize)
                {
                    names.GetArrayElementAtIndex(i).stringValue = MonsterAgentName;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return id;
        }
    }
}
