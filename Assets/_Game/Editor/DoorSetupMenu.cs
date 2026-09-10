using HouseOfSilence.Doors;
using HouseOfSilence.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Outils de la Phase 5 : generation de portes pivotantes correctement
    /// montees (dormant + charniere + battant), impossibles a rater a la main.
    /// Menu : Tools > House of Silence > Doors > ...
    /// </summary>
    public static class DoorSetupMenu
    {
        private const float PanelWidth = 0.9f;
        private const float PanelHeight = 2.05f;
        private const float PanelThickness = 0.06f;

        [MenuItem("Tools/House of Silence/Doors/Create Normal Door", false, 80)]
        public static void CreateNormalDoor()
        {
            GameObject door = BuildDoor<Door>("Door_Normal", GetSpawnPosition());
            Finish(door);
        }

        [MenuItem("Tools/House of Silence/Doors/Create Key Door", false, 81)]
        public static void CreateKeyDoor()
        {
            GameObject door = BuildDoor<KeyDoor>("Door_Key", GetSpawnPosition());
            Finish(door);
        }

        [MenuItem("Tools/House of Silence/Doors/Create Objective Door", false, 82)]
        public static void CreateObjectiveDoor()
        {
            GameObject door = BuildDoor<ObjectiveDoor>("Door_Objective", GetSpawnPosition());
            Finish(door);
        }

        [MenuItem("Tools/House of Silence/Doors/Create Breakable Door", false, 83)]
        public static void CreateBreakableDoor()
        {
            GameObject door = BuildDoor<BreakableDoor>("Door_Breakable", GetSpawnPosition());
            Finish(door);
        }

        /// <summary>Pose les quatre types de portes cote a cote, pour tester la phase.</summary>
        [MenuItem("Tools/House of Silence/Doors/Create Test Doors", false, 90)]
        public static void CreateTestDoors()
        {
            GameObject existing = GameObject.Find("TestDoors");

            if (existing != null)
            {
                Debug.LogWarning("[Setup] 'TestDoors' existe deja. Supprime-le avant d'en recreer un.", existing);
                Selection.activeGameObject = existing;
                return;
            }

            GameObject root = new GameObject("TestDoors");
            Undo.RegisterCreatedObjectUndo(root, "Create Test Doors");

            // 1. Porte normale
            GameObject normal = BuildDoor<Door>("Door_Normal", new Vector3(-6f, 0f, -2f));
            normal.transform.SetParent(root.transform, true);

            // 2. Porte verrouillee sans cle : deverrouillable uniquement par script
            GameObject locked = BuildDoor<Door>("Door_Locked", new Vector3(-3f, 0f, -2f));
            locked.transform.SetParent(root.transform, true);
            SetBool(locked.GetComponent<Door>(), "startLocked", true);
            SetString(locked.GetComponent<Door>(), "lockedPromptText", "Verrouillee de l'autre cote");

            // 3. Porte a cle : serrure "office" (= Item_Key_Office)
            GameObject key = BuildDoor<KeyDoor>("Door_Key_Office", new Vector3(0f, 0f, -2f));
            key.transform.SetParent(root.transform, true);
            SetString(key.GetComponent<KeyDoor>(), "lockId", "office");

            // 4. Porte d'objectif
            GameObject objective = BuildDoor<ObjectiveDoor>("Door_Objective", new Vector3(3f, 0f, -2f));
            objective.transform.SetParent(root.transform, true);
            SetString(objective.GetComponent<ObjectiveDoor>(), "requiredObjectiveId", "restore_power");

            // 5. Porte cassable
            GameObject breakable = BuildDoor<BreakableDoor>("Door_Breakable", new Vector3(6f, 0f, -2f));
            breakable.transform.SetParent(root.transform, true);

            EnsureDoorDebugOnPlayer();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[Setup] 5 portes de test creees en z = -2 : normale, verrouillee, a cle (office), objectif, cassable.", root);
        }

        /// <summary>Ajoute les raccourcis de debug des portes au joueur present dans la scene.</summary>
        public static void EnsureDoorDebugOnPlayer()
        {
            PlayerInteractor interactor = Object.FindAnyObjectByType<PlayerInteractor>(FindObjectsInactive.Include);

            if (interactor == null)
            {
                return;
            }

            DoorDebugCommands commands = EditorSetupUtility.EnsureComponent<DoorDebugCommands>(interactor.gameObject);
            EditorSetupUtility.SetObjectField(commands, "interactor", interactor);
        }

        // ------------------------------------------------------------------

        private static GameObject BuildDoor<T>(string doorName, Vector3 position) where T : DoorBase
        {
            GameObject root = new GameObject(doorName);
            root.transform.position = position;

            Undo.RegisterCreatedObjectUndo(root, "Create Door");

            // --- Dormant (immobile) ---------------------------------------
            GameObject frame = new GameObject("Frame");
            frame.transform.SetParent(root.transform, false);

            EditorSetupUtility.CreateBox(frame.transform, "Post_Left",
                new Vector3(-0.5f, PanelHeight * 0.5f, 0f),
                new Vector3(0.1f, PanelHeight + 0.05f, 0.14f));

            EditorSetupUtility.CreateBox(frame.transform, "Post_Right",
                new Vector3(0.5f, PanelHeight * 0.5f, 0f),
                new Vector3(0.1f, PanelHeight + 0.05f, 0.14f));

            EditorSetupUtility.CreateBox(frame.transform, "Lintel",
                new Vector3(0f, PanelHeight + 0.1f, 0f),
                new Vector3(1.1f, 0.12f, 0.14f));

            // --- Charniere (c'est elle qui tourne) ------------------------
            GameObject hinge = new GameObject("Hinge");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(-PanelWidth * 0.5f, 0f, 0f);

            // --- Battant ---------------------------------------------------
            GameObject panel = EditorSetupUtility.CreateBox(hinge.transform, "Panel",
                new Vector3(PanelWidth * 0.5f, PanelHeight * 0.5f, 0f),
                new Vector3(PanelWidth, PanelHeight, PanelThickness));

            panel.AddComponent<InteractableHighlight>();

            // --- Composants -----------------------------------------------
            T door = root.AddComponent<T>();
            EditorSetupUtility.SetObjectField(door, "hinge", hinge.transform);

            AudioSource source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1.5f;
            source.maxDistance = 20f;
            source.rolloffMode = AudioRolloffMode.Linear;

            EditorSetupUtility.SetObjectField(door, "audioSource", source);

            return root;
        }

        private static void Finish(GameObject door)
        {
            Selection.activeGameObject = door;
            EditorSceneManager.MarkSceneDirty(door.scene);

            Debug.Log("[Setup] Porte '" + door.name + "' creee. Le +Z bleu indique la face avant : la porte s'ouvrira toujours a l'oppose de celui qui la pousse.", door);
        }

        /// <summary>Pose la porte devant la vue de la scene, au niveau du sol.</summary>
        private static Vector3 GetSpawnPosition()
        {
            SceneView view = SceneView.lastActiveSceneView;

            if (view == null)
            {
                return Vector3.zero;
            }

            Vector3 pivot = view.pivot;
            pivot.y = 0f;
            return pivot;
        }

        private static void SetBool(Object target, string fieldName, bool value)
        {
            if (target == null)
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning("[Setup] Champ '" + fieldName + "' introuvable sur " + target.GetType().Name + ".");
                return;
            }

            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string fieldName, string value)
        {
            if (target == null)
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning("[Setup] Champ '" + fieldName + "' introuvable sur " + target.GetType().Name + ".");
                return;
            }

            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
