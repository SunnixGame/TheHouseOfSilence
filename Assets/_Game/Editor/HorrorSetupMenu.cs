using System.IO;
using HouseOfSilence.Horror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Outils de la Phase 9 : catalogue d'evenements, manager, props physiques,
    /// emplacements, et equipement du sous-sol de test.
    /// Menu : Tools > House of Silence > Horror > ...
    /// </summary>
    public static class HorrorSetupMenu
    {
        private const string HorrorFolder = "Assets/_Game/ScriptableObjects/Horror";

        private struct EventDefinition
        {
            public string AssetName;
            public string Id;
            public string Name;
            public HorrorEventKind Kind;
            public float MinFear, MaxFear, MinGameTime;
            public int MinObjective, MaxObjective;
            public bool RequiresPower;
            public float Weight, Cooldown;
            public int MaxOccurrences;
            public float Fear, FearRadius, Noise, Duration;
            public HorrorPositionMode Position;
            public float MinDistance, MaxDistance;
            public string SpotTag;
            public string Notes;
        }

        /// <summary>
        /// Le catalogue du vertical slice. Les temps suivent la montee en tension
        /// du cahier des charges (§49) : subtil d'abord, franc ensuite.
        /// </summary>
        private static readonly EventDefinition[] Definitions =
        {
            new EventDefinition { AssetName = "HE_LightFlicker", Id = "light_flicker", Name = "Lumiere qui scintille", Kind = HorrorEventKind.LightFlicker,
                MinGameTime = 60f, MaxFear = 100f, Weight = 4f, Cooldown = 30f, Fear = 2f, FearRadius = 12f, Noise = 0f, Duration = 2f,
                MinDistance = 0f, MaxDistance = 12f, MaxObjective = -1, RequiresPower = false,
                Notes = "Le premier signe. Presque rien." },

            new EventDefinition { AssetName = "HE_ObjectFall", Id = "object_fall", Name = "Objet qui tombe", Kind = HorrorEventKind.ObjectFall,
                MinGameTime = 60f, MaxFear = 100f, Weight = 4f, Cooldown = 45f, Fear = 4f, FearRadius = 12f, Noise = 0f,
                MinDistance = 2f, MaxDistance = 14f, MaxObjective = -1,
                Notes = "Un livre, une boite. Le bruit vient de HorrorProp." },

            new EventDefinition { AssetName = "HE_DoorOpen", Id = "door_open", Name = "Porte qui s'ouvre seule", Kind = HorrorEventKind.DoorOpen,
                MinGameTime = 90f, MaxFear = 100f, Weight = 3f, Cooldown = 60f, Fear = 5f, FearRadius = 12f, Noise = 0f,
                MinDistance = 3f, MaxDistance = 16f, MaxObjective = -1,
                Notes = "Le grincement vient de la porte elle-meme." },

            new EventDefinition { AssetName = "HE_DoorSlam", Id = "door_slam", Name = "Porte qui claque", Kind = HorrorEventKind.DoorSlam,
                MinGameTime = 120f, MaxFear = 100f, Weight = 3f, Cooldown = 60f, Fear = 4f, FearRadius = 15f, Noise = 0f,
                MinDistance = 3f, MaxDistance = 18f, MaxObjective = -1,
                Notes = "Slam() emet deja un bruit de 22 m qui effraie : la peur ajoutee ici reste modeste." },

            new EventDefinition { AssetName = "HE_Whisper", Id = "whisper", Name = "Murmure", Kind = HorrorEventKind.Sound,
                MinGameTime = 120f, MinFear = 15f, MaxFear = 100f, Weight = 2f, Cooldown = 75f, Fear = 4f, FearRadius = 6f, Noise = 0f,
                Position = HorrorPositionMode.BehindPlayer, MinDistance = 1.5f, MaxDistance = 3f, MaxObjective = -1,
                Notes = "Emplacement audio prevu. Sans clip : peur seule." },

            new EventDefinition { AssetName = "HE_RadioVoice", Id = "radio_voice", Name = "La radio parle", Kind = HorrorEventKind.Sound,
                MinGameTime = 150f, MaxFear = 100f, Weight = 1.5f, Cooldown = 240f, MaxOccurrences = 2, Fear = 6f, FearRadius = 14f, Noise = 10f,
                Position = HorrorPositionMode.NearestSpot, SpotTag = "radio", MinDistance = 2f, MaxDistance = 25f, MaxObjective = -1,
                Notes = "Necessite un HorrorSpot 'radio'." },

            new EventDefinition { AssetName = "HE_LightBreak", Id = "light_break", Name = "Ampoule qui eclate", Kind = HorrorEventKind.LightBreak,
                MinGameTime = 150f, MinFear = 15f, MaxFear = 100f, Weight = 1.5f, Cooldown = 120f, MaxOccurrences = 3, Fear = 6f, FearRadius = 10f, Noise = 0f, Duration = 1.5f,
                MinDistance = 0f, MaxDistance = 10f, MaxObjective = -1,
                Notes = "La lampe emet son propre bruit et sa propre peur en eclatant." },

            new EventDefinition { AssetName = "HE_Footsteps", Id = "footsteps", Name = "Bruits de pas", Kind = HorrorEventKind.Sound,
                MinGameTime = 180f, MaxFear = 100f, Weight = 2f, Cooldown = 90f, Fear = 6f, FearRadius = 8f, Noise = 0f,
                Position = HorrorPositionMode.BehindPlayer, MinDistance = 3f, MaxDistance = 6f, MaxObjective = -1,
                Notes = "Minute 3-4 : le premier bruit impossible a expliquer." },

            new EventDefinition { AssetName = "HE_PhoneRing", Id = "phone_ring", Name = "Le telephone sonne", Kind = HorrorEventKind.Sound,
                MinGameTime = 200f, MaxFear = 100f, Weight = 1f, Cooldown = 300f, MaxOccurrences = 1, Fear = 8f, FearRadius = 20f, Noise = 14f, Duration = 6f,
                Position = HorrorPositionMode.NearestSpot, SpotTag = "phone", MinDistance = 2f, MaxDistance = 30f, MaxObjective = -1,
                Notes = "Necessite un HorrorSpot 'phone'. Une seule fois par partie." },

            new EventDefinition { AssetName = "HE_Silhouette", Id = "silhouette", Name = "Silhouette", Kind = HorrorEventKind.Apparition,
                MinGameTime = 240f, MinFear = 25f, MaxFear = 100f, Weight = 1f, Cooldown = 150f, Fear = 14f, FearRadius = 15f, Noise = 0f, Duration = 1.2f,
                Position = HorrorPositionMode.InFrontOfPlayer, MinDistance = 8f, MaxDistance = 14f, MaxObjective = -1,
                Notes = "Minute 4-5 : premiere apparition tres breve. Assigner un Visual Prefab (Phase 10 : la creature)." },

            new EventDefinition { AssetName = "HE_PowerCut", Id = "power_cut", Name = "Coupure de courant", Kind = HorrorEventKind.PowerCut,
                MinGameTime = 240f, MaxFear = 100f, Weight = 1f, Cooldown = 240f, MaxOccurrences = 2, Fear = 10f, FearRadius = 40f, Noise = 0f, Duration = 6f,
                MinObjective = 4, MaxObjective = -1, RequiresPower = true,
                Notes = "Seulement apres que le joueur a retabli le courant (objectif 4)." },

            new EventDefinition { AssetName = "HE_Scream", Id = "scream", Name = "Cri lointain", Kind = HorrorEventKind.Sound,
                MinGameTime = 300f, MinFear = 35f, MaxFear = 100f, Weight = 1f, Cooldown = 200f, MaxOccurrences = 2, Fear = 15f, FearRadius = 30f, Noise = 20f,
                Position = HorrorPositionMode.AroundPlayer, MinDistance = 10f, MaxDistance = 20f, MaxObjective = -1,
                Notes = "Un cri qui n'appartient a personne." }
        };

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Horror/Create Horror Event Assets", false, 140)]
        public static void CreateEventAssets()
        {
            EnsureFolder(HorrorFolder);

            int created = 0;

            for (int i = 0; i < Definitions.Length; i++)
            {
                EventDefinition d = Definitions[i];
                string path = HorrorFolder + "/" + d.AssetName + ".asset";

                if (AssetDatabase.LoadAssetAtPath<HorrorEventData>(path) != null)
                {
                    continue;
                }

                HorrorEventData asset = ScriptableObject.CreateInstance<HorrorEventData>();
                AssetDatabase.CreateAsset(asset, path);
                Apply(asset, d);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Setup] HorrorEventData : " + created + " cree(s), " + (Definitions.Length - created) + " deja present(s), dans " + HorrorFolder);
        }

        private static void Apply(HorrorEventData asset, EventDefinition d)
        {
            SerializedObject so = new SerializedObject(asset);

            so.FindProperty("eventId").stringValue = d.Id;
            so.FindProperty("displayName").stringValue = d.Name;
            so.FindProperty("kind").enumValueIndex = (int)d.Kind;
            so.FindProperty("designerNotes").stringValue = d.Notes ?? string.Empty;

            so.FindProperty("minFear").floatValue = d.MinFear;
            so.FindProperty("maxFear").floatValue = d.MaxFear <= 0f ? 100f : d.MaxFear;
            so.FindProperty("minGameTime").floatValue = d.MinGameTime;
            so.FindProperty("minObjectiveIndex").intValue = d.MinObjective;
            so.FindProperty("maxObjectiveIndex").intValue = d.MaxObjective;
            so.FindProperty("requiresPower").boolValue = d.RequiresPower;

            so.FindProperty("weight").floatValue = d.Weight <= 0f ? 1f : d.Weight;
            so.FindProperty("cooldown").floatValue = d.Cooldown;
            so.FindProperty("maxOccurrences").intValue = d.MaxOccurrences;

            so.FindProperty("fearImpact").floatValue = d.Fear;
            so.FindProperty("fearRadius").floatValue = d.FearRadius <= 0f ? 15f : d.FearRadius;
            so.FindProperty("noiseRadius").floatValue = d.Noise;
            so.FindProperty("duration").floatValue = d.Duration;

            so.FindProperty("positionMode").enumValueIndex = (int)d.Position;
            so.FindProperty("minDistance").floatValue = d.MinDistance;
            so.FindProperty("maxDistance").floatValue = d.MaxDistance <= 0f ? 15f : d.MaxDistance;
            so.FindProperty("spotTag").stringValue = d.SpotTag ?? string.Empty;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Horror/Add Horror Event Manager", false, 141)]
        public static HorrorEventManager AddManager()
        {
            CreateEventAssets();

            HorrorEventManager manager = Object.FindAnyObjectByType<HorrorEventManager>(FindObjectsInactive.Include);

            if (manager == null)
            {
                GameObject host = GameObject.Find("[GameSystems]");

                if (host == null)
                {
                    host = new GameObject("[GameSystems]");
                    Undo.RegisterCreatedObjectUndo(host, "Create GameSystems");
                }

                manager = Undo.AddComponent<HorrorEventManager>(host);
            }

            SerializedObject so = new SerializedObject(manager);
            SerializedProperty list = so.FindProperty("events");
            list.ClearArray();

            int added = 0;

            for (int i = 0; i < Definitions.Length; i++)
            {
                HorrorEventData asset = AssetDatabase.LoadAssetAtPath<HorrorEventData>(HorrorFolder + "/" + Definitions[i].AssetName + ".asset");

                if (asset == null)
                {
                    continue;
                }

                list.InsertArrayElementAtIndex(added);
                list.GetArrayElementAtIndex(added).objectReferenceValue = asset;
                added++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = manager.gameObject;
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

            Debug.Log("[Setup] HorrorEventManager pret avec " + added + " evenements sur '" + manager.gameObject.name + "'.", manager);
            return manager;
        }

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Horror/Create Horror Spot", false, 142)]
        public static void CreateSpot()
        {
            GameObject spot = new GameObject("HorrorSpot");

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 position = view != null ? view.pivot : Vector3.zero;
            position.y = Mathf.Max(position.y, 0f) + 1.2f;
            spot.transform.position = position;

            Undo.RegisterCreatedObjectUndo(spot, "Create Horror Spot");
            spot.AddComponent<HorrorSpot>();

            Selection.activeGameObject = spot;
            EditorSceneManager.MarkSceneDirty(spot.scene);

            Debug.Log("[Setup] HorrorSpot cree. Renseigne son Spot Tag : phone, radio, silhouette...", spot);
        }

        /// <summary>Pose des objets physiques pilotables dans la salle de test et le sous-sol.</summary>
        [MenuItem("Tools/House of Silence/Horror/Create Test Props", false, 143)]
        public static void CreateTestProps()
        {
            GameObject existing = GameObject.Find("TestProps");

            if (existing != null)
            {
                Debug.LogWarning("[Setup] 'TestProps' existe deja.", existing);
                Selection.activeGameObject = existing;
                return;
            }

            GameObject root = new GameObject("TestProps");
            Undo.RegisterCreatedObjectUndo(root, "Create Test Props");

            // Rez-de-chaussee : sur les caisses de la salle de test.
            Prop(root.transform, "Prop_Can_1", new Vector3(3f, 0.45f, -3f), new Vector3(0.12f, 0.18f, 0.12f), PrimitiveType.Cylinder);
            Prop(root.transform, "Prop_Book_1", new Vector3(4.5f, 0.72f, -3f), new Vector3(0.22f, 0.05f, 0.3f), PrimitiveType.Cube);
            Prop(root.transform, "Prop_Book_2", new Vector3(6f, 1.12f, -3f), new Vector3(0.2f, 0.05f, 0.28f), PrimitiveType.Cube);
            Prop(root.transform, "Prop_Box_1", new Vector3(-8f, 1.7f, -8.5f), new Vector3(0.3f, 0.3f, 0.3f), PrimitiveType.Cube);

            // Sous-sol : sur l'etagere du stockage et les caisses de la chaufferie.
            GameObject basement = GameObject.Find("TestBasement");

            if (basement != null)
            {
                Prop(root.transform, "Prop_Jar_1", new Vector3(-1.4f, -1.6f, -1.2f), new Vector3(0.12f, 0.16f, 0.12f), PrimitiveType.Cylinder);
                Prop(root.transform, "Prop_Jar_2", new Vector3(-1.4f, -1.6f, -2.6f), new Vector3(0.12f, 0.16f, 0.12f), PrimitiveType.Cylinder);
                Prop(root.transform, "Prop_Tool_1", new Vector3(-3f, -2.75f, 5.5f), new Vector3(0.08f, 0.08f, 0.35f), PrimitiveType.Cube);
                Prop(root.transform, "Prop_Box_2", new Vector3(-6.2f, -1.65f, 1.2f), new Vector3(0.25f, 0.25f, 0.25f), PrimitiveType.Cube);
            }

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[Setup] Props physiques crees (HorrorProp) : " + root.transform.childCount + ".", root);
        }

        /// <summary>Equipe le sous-sol de test : manager, props, spots telephone / radio / silhouette.</summary>
        [MenuItem("Tools/House of Silence/Horror/Setup Basement Horror", false, 150)]
        public static void SetupBasementHorror()
        {
            AddManager();
            CreateTestProps();

            GameObject basement = GameObject.Find("TestBasement");

            if (basement == null)
            {
                Debug.LogWarning("[Setup] Aucun 'TestBasement' : spots non crees.");
                return;
            }

            if (basement.transform.Find("HorrorSpots") != null)
            {
                Debug.LogWarning("[Setup] Les HorrorSpots du sous-sol existent deja.", basement);
                return;
            }

            GameObject spots = new GameObject("HorrorSpots");
            spots.transform.SetParent(basement.transform, false);
            Undo.RegisterCreatedObjectUndo(spots, "Create Horror Spots");

            // Bout du couloir : la silhouette se tient la, face a l'escalier.
            Spot(spots.transform, "Spot_Silhouette_CorridorEnd", new Vector3(-9f, -3.6f, -3.5f), Quaternion.Euler(0f, 0f, 0f), "silhouette");

            // Radio dans le stockage, telephone dans la chaufferie.
            Spot(spots.transform, "Spot_Radio_Storage", new Vector3(-2f, -2.4f, -3.5f), Quaternion.identity, "radio");
            Spot(spots.transform, "Spot_Phone_BoilerRoom", new Vector3(-2f, -2.4f, 6.5f), Quaternion.identity, "phone");

            // Un telephone au rez-de-chaussee aussi, pour qu'il sonne des le debut.
            Spot(spots.transform, "Spot_Phone_GroundFloor", new Vector3(10.5f, 1f, 10.5f), Quaternion.identity, "phone");

            Selection.activeGameObject = spots;
            EditorSceneManager.MarkSceneDirty(spots.scene);

            Debug.Log("[Setup] Sous-sol mis en scene : 4 spots (silhouette, radio, 2 telephones).", spots);
        }

        // ------------------------------------------------------------------

        private static void Prop(Transform parent, string name, Vector3 position, Vector3 size, PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = 0.5f;
            body.linearDamping = 0.3f;
            body.angularDamping = 1.5f;

            go.AddComponent<HorrorProp>();
        }

        private static void Spot(Transform parent, string name, Vector3 position, Quaternion rotation, string tag)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);

            HorrorSpot spot = go.AddComponent<HorrorSpot>();

            SerializedObject so = new SerializedObject(spot);
            so.FindProperty("spotTag").stringValue = tag;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace("\\", "/");
            string leaf = Path.GetFileName(folder);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
