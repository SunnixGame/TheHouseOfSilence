using System.Collections.Generic;
using System.Globalization;
using HouseOfSilence.Core;
using HouseOfSilence.Doors;
using HouseOfSilence.Level;
using HouseOfSilence.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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

        private bool IsManor { get { return assetPath.Replace('\\', '/') == FbxPath; } }

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
            mat.SetFloat("_Smoothness", name == "M_Glass_Dirty" ? 0.7f : 0.15f);

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

        private const StaticEditorFlags StaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                                                      StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic |
                                                      StaticEditorFlags.ReflectionProbeStatic;

        private static readonly HashSet<string> MeshColliderTypes = new HashSet<string>
        {
            "wall_ext", "wall_int", "floor", "ceiling", "stairs", "column", "fireplace", "closet", "roof", "decor", "railing",
        };

        [MenuItem("Tools/House of Silence/Level/Manor/1 - Reimport Manor FBX", false, 300)]
        public static void ReimportFbx()
        {
            ManorAssetPostprocessor.EnsureMaterials();
            AssetDatabase.ImportAsset(ManorAssetPostprocessor.FbxPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[Manor] FBX reimporte : " + ManorAssetPostprocessor.FbxPath);
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

            GameObject manor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(manor, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            manor.name = "Manor";

            ManorStats stats = new ManorStats();
            Dictionary<string, Transform> roomMarkers = CollectRoomMarkers(manor.transform);

            SetupStaticsAndColliders(manor.transform, stats);
            SetupDoors(manor.transform, roomMarkers, stats);
            SetupRooms(manor.transform, stats);
            CleanupMarkers(manor.transform);

            BuildGround(manor.transform);

            Transform lighting = new GameObject("--- Lighting ---").transform;
            PrototypeLevelBuilder.SetupNight(lighting);

            Vector3 spawn;
            Quaternion facing;
            FindSpawn(manor.transform, out spawn, out facing);
            PrototypeLevelBuilder.SetupSystems(spawn, facing);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(string.Format("[Manor] Scene construite : {0} colliders mesh, {1} colliders boite, {2} portes ({3} cassables), {4} pieces, {5} objets statiques. Spawn {6}.",
                stats.meshColliders, stats.boxColliders, stats.doors, stats.breakable, stats.rooms, stats.statics, spawn));
        }

        // ------------------------------------------------------------------

        private class ManorStats
        {
            public int meshColliders, boxColliders, doors, breakable, rooms, statics;
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
            // Placards, trappe, portes secretes et cloisons ont un sens impose : on oriente la
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

                // bord libre du battant : le long de +X local, sauf la trappe (vers le sud, -Y local)
                Vector3 lever = kind == "trapdoor" ? -panel.transform.up * 0.6f : panel.transform.right * width;
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

            DoorBase door;

            if (kind == "breakable")
            {
                door = rootGo.AddComponent<BreakableDoor>();
                stats.breakable++;
            }
            else
            {
                door = rootGo.AddComponent<Door>();
            }

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
                    string rooms = meta.Get("hos_rooms");
                    int sep = rooms.IndexOf('|');
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

        private static void BuildGround(Transform manor)
        {
            // Sol plat provisoire au niveau du terrain du manoir (-1.4 m), troue sous l'emprise de la
            // maison et sous l'abri du tunnel (sinon il traverse le sous-sol). Le vrai terrain vient plus tard.
            const float groundY = -1.4f;
            List<Rect> pieces = new List<Rect> { new Rect(-150f, -150f, 400f, 400f) };   // x, z, largeur, profondeur

            foreach (Rect hole in new[] { new Rect(0f, 0f, 52f, 30f), new Rect(89.6f, 34.6f, 4.8f, 9.8f) })
            {
                List<Rect> next = new List<Rect>();

                foreach (Rect r in pieces)
                {
                    next.AddRange(SubtractRect(r, hole));
                }

                pieces = next;
            }

            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();
            List<Vector2> uvs = new List<Vector2>();

            foreach (Rect r in pieces)
            {
                int i = verts.Count;
                verts.Add(new Vector3(r.xMin, groundY, r.yMin));
                verts.Add(new Vector3(r.xMin, groundY, r.yMax));
                verts.Add(new Vector3(r.xMax, groundY, r.yMax));
                verts.Add(new Vector3(r.xMax, groundY, r.yMin));
                uvs.Add(new Vector2(r.xMin, r.yMin) * 0.1f);
                uvs.Add(new Vector2(r.xMin, r.yMax) * 0.1f);
                uvs.Add(new Vector2(r.xMax, r.yMax) * 0.1f);
                uvs.Add(new Vector2(r.xMax, r.yMin) * 0.1f);
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            Mesh mesh = new Mesh();
            mesh.name = "Ground_Temp";
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject ground = new GameObject("Ground_Temp");
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = ManorAssetPostprocessor.GetOrCreateMaterial("M_Ground");
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(ground, StaticFlags);
        }

        /// <summary>Soustrait un rectangle d'un autre ; retourne jusqu'a quatre rectangles.</summary>
        private static IEnumerable<Rect> SubtractRect(Rect r, Rect hole)
        {
            float ix0 = Mathf.Max(r.xMin, hole.xMin), ix1 = Mathf.Min(r.xMax, hole.xMax);
            float iy0 = Mathf.Max(r.yMin, hole.yMin), iy1 = Mathf.Min(r.yMax, hole.yMax);

            if (ix1 - ix0 <= 1e-4f || iy1 - iy0 <= 1e-4f)
            {
                yield return r;
                yield break;
            }

            if (iy0 > r.yMin) yield return Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, iy0);
            if (iy1 < r.yMax) yield return Rect.MinMaxRect(r.xMin, iy1, r.xMax, r.yMax);
            if (ix0 > r.xMin) yield return Rect.MinMaxRect(r.xMin, iy0, ix0, iy1);
            if (ix1 < r.xMax) yield return Rect.MinMaxRect(ix1, iy0, r.xMax, iy1);
        }

        private static void FindSpawn(Transform root, out Vector3 spawn, out Quaternion facing)
        {
            Transform perron = FindChild(root, "GP_Spawn_Perron");
            Transform hall = FindChild(root, "GP_Spawn_Hall");
            spawn = perron != null ? perron.position + Vector3.up * 0.2f : Vector3.up;
            Vector3 dir = hall != null ? hall.position - spawn : Vector3.forward;
            dir.y = 0f;
            facing = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir.normalized) : Quaternion.identity;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
