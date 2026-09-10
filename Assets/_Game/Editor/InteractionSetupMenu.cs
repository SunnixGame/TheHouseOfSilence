using HouseOfSilence.Interaction;
using HouseOfSilence.Player;
using HouseOfSilence.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Outils de la Phase 3.
    /// Menu : Tools > House of Silence > ...
    /// </summary>
    public static class InteractionSetupMenu
    {
        private const string TestRootName = "TestInteractables";

        /// <summary>
        /// Met a niveau un joueur existant (cree en Phase 2) : ajoute le
        /// PlayerInteractor et l'affichage temporaire du prompt.
        /// </summary>
        [MenuItem("Tools/House of Silence/Add Interaction To Player", false, 21)]
        public static void AddInteractionToPlayer()
        {
            PlayerCharacter player = Object.FindAnyObjectByType<PlayerCharacter>(FindObjectsInactive.Include);

            if (player == null)
            {
                Debug.LogWarning("[Setup] Aucun joueur dans la scene. Utilise d'abord Tools > House of Silence > Create Player.");
                return;
            }

            PlayerInteractor interactor = EditorSetupUtility.EnsureComponent<PlayerInteractor>(player.gameObject);

            EditorSetupUtility.SetObjectField(interactor, "player", player);
            EditorSetupUtility.SetObjectField(interactor, "input", player.GetComponent<InputReader>());

            if (player.Camera != null)
            {
                EditorSetupUtility.SetObjectField(interactor, "rayOrigin", player.Camera.transform);
            }

            EnsurePromptOverlay();

            Selection.activeGameObject = player.gameObject;
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);

            Debug.Log("[Setup] PlayerInteractor ajoute et cable sur '" + player.name + "'.", player);
        }

        /// <summary>
        /// Cree quatre objets de test couvrant tous les cas du systeme :
        /// instantane, bascule, maintien, et verrouille.
        /// </summary>
        [MenuItem("Tools/House of Silence/Create Test Interactables", false, 41)]
        public static void CreateTestInteractables()
        {
            GameObject existing = GameObject.Find(TestRootName);

            if (existing != null)
            {
                Debug.LogWarning("[Setup] '" + TestRootName + "' existe deja. Supprime-le avant d'en recreer un.", existing);
                Selection.activeGameObject = existing;
                return;
            }

            GameObject root = new GameObject(TestRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Test Interactables");

            // 1. Interaction instantanee
            GameObject instant = EditorSetupUtility.CreateBox(root.transform, "Interact_Instant", new Vector3(-2f, 1.2f, 4f), new Vector3(0.5f, 0.5f, 0.5f));
            ConfigureSimple(instant, "Examiner l'objet", false, 0f, true);

            // 2. Interrupteur a bascule
            GameObject toggle = EditorSetupUtility.CreateBox(root.transform, "Interact_Toggle", new Vector3(0f, 1.2f, 4f), new Vector3(0.4f, 0.6f, 0.2f));
            SimpleInteractable toggleScript = ConfigureSimple(toggle, "Allumer", true, 0f, true);
            SetStringField(toggleScript, "alternatePromptText", "Eteindre");

            // 3. Interaction a maintien (2 s)
            GameObject hold = EditorSetupUtility.CreateBox(root.transform, "Interact_Hold", new Vector3(2f, 1.2f, 4f), new Vector3(0.6f, 0.6f, 0.4f));
            ConfigureSimple(hold, "Reparer", false, 2f, true);

            // 4. Objet verrouille : focalisable, mais refuse l'interaction
            GameObject locked = EditorSetupUtility.CreateBox(root.transform, "Interact_Locked", new Vector3(4f, 1.2f, 4f), new Vector3(0.5f, 0.9f, 0.3f));
            SimpleInteractable lockedScript = ConfigureSimple(locked, "Ouvrir", false, 0f, false);
            SetStringField(lockedScript, "blockedPromptText", "Verrouille");

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[Setup] 4 objets de test crees : instantane, bascule, maintien (2 s), verrouille.", root);
        }

        // ------------------------------------------------------------------

        private static SimpleInteractable ConfigureSimple(GameObject target, string prompt, bool toggleMode, float holdDuration, bool interactable)
        {
            SimpleInteractable script = Undo.AddComponent<SimpleInteractable>(target);
            EditorSetupUtility.EnsureComponent<InteractableHighlight>(target);

            SetStringField(script, "promptText", prompt);
            SetBoolField(script, "toggleMode", toggleMode);
            SetBoolField(script, "interactable", interactable);
            SetFloatField(script, "holdDuration", holdDuration);

            return script;
        }

        private static void EnsurePromptOverlay()
        {
            InteractionPromptOverlay overlay = Object.FindAnyObjectByType<InteractionPromptOverlay>(FindObjectsInactive.Include);

            if (overlay != null)
            {
                return;
            }

            GameObject host = GameObject.Find("[GameSystems]");

            if (host == null)
            {
                host = new GameObject("[HUD]");
                Undo.RegisterCreatedObjectUndo(host, "Create HUD");
            }

            Undo.AddComponent<InteractionPromptOverlay>(host);
            Debug.Log("[Setup] InteractionPromptOverlay ajoute sur '" + host.name + "' (HUD temporaire, remplace en Phase 15).", host);
        }

        private static void SetStringField(Object target, string fieldName, string value)
        {
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

        private static void SetBoolField(Object target, string fieldName, bool value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                return;
            }

            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloatField(Object target, string fieldName, float value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                return;
            }

            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
