using HouseOfSilence.Doors;
using HouseOfSilence.Interaction;
using HouseOfSilence.Inventory;
using HouseOfSilence.Player;
using HouseOfSilence.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Construit un joueur FPS complet et correctement cable, en un clic.
    /// Menu : Tools > House of Silence > Create Player
    /// </summary>
    public static class PlayerSetupMenu
    {
        private const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";
        private const string BodyPrefabPath = "Assets/Floreswa/Prefabs/char01_1.prefab";
        private const float BodyTargetHeight = 1.75f;

        [MenuItem("Tools/House of Silence/Create Player", false, 20)]
        public static void CreatePlayer()
        {
            PlayerCharacter existing = Object.FindAnyObjectByType<PlayerCharacter>(FindObjectsInactive.Include);

            if (existing != null)
            {
                Debug.LogWarning("[Setup] Un joueur existe deja dans la scene : " + existing.name, existing);
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            // --- Hierarchie ------------------------------------------------
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 0.1f, 0f);

            Undo.RegisterCreatedObjectUndo(player, "Create Player");

            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.65f, 0f);

            GameObject cameraObject = new GameObject("PlayerCamera");
            cameraObject.transform.SetParent(pivot.transform, false);
            cameraObject.tag = "MainCamera";

            // --- Composants ------------------------------------------------
            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 46f;
            controller.stepOffset = 0.35f;
            controller.skinWidth = 0.02f;
            controller.minMoveDistance = 0f;

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 200f;

            cameraObject.AddComponent<AudioListener>();

            InputReader inputReader = player.AddComponent<InputReader>();
            PlayerStamina stamina = player.AddComponent<PlayerStamina>();
            PlayerMotor motor = player.AddComponent<PlayerMotor>();
            PlayerLook look = player.AddComponent<PlayerLook>();
            player.AddComponent<PlayerHealth>();
            PlayerCharacter character = player.AddComponent<PlayerCharacter>();
            PlayerInteractor interactor = player.AddComponent<PlayerInteractor>();
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            player.AddComponent<PlayerDebugCommands>();

            DoorDebugCommands doorDebug = player.AddComponent<DoorDebugCommands>();

            // --- Cablage des references privees ----------------------------
            InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);

            if (inputAsset == null)
            {
                Debug.LogWarning("[Setup] " + InputAssetPath + " introuvable : assigne l'InputActionAsset a la main sur l'InputReader.");
            }
            else
            {
                SetObjectField(inputReader, "inputActions", inputAsset);
            }

            SetObjectField(look, "cameraPivot", pivot.transform);
            SetObjectField(look, "input", inputReader);

            SetObjectField(motor, "input", inputReader);
            SetObjectField(motor, "stamina", stamina);
            SetObjectField(motor, "cameraPivot", pivot.transform);

            SetObjectField(character, "playerCamera", camera);
            SetObjectField(character, "cameraPivot", pivot.transform);

            SetObjectField(interactor, "player", character);
            SetObjectField(interactor, "input", inputReader);
            SetObjectField(interactor, "rayOrigin", cameraObject.transform);

            SetObjectField(doorDebug, "interactor", interactor);

            SetObjectField(inventory, "player", character);
            SetObjectField(inventory, "input", inputReader);
            SetObjectField(inventory, "defaultDropPrefab", ItemSetupMenu.CreateDefaultPickupPrefab());

            // --- Corps provisoire (pack hypercasual) -----------------------
            AttachBodyMesh(player);

            // --- HUD temporaire (interaction + inventaire) -----------------
            EnsurePromptOverlay();
            ItemSetupMenu.EnsureInventoryHud();

            // --- Nettoyage de la scene -------------------------------------
            DisableOtherAudioListeners(cameraObject);
            DisableOtherCameras(camera);

            Selection.activeGameObject = player;
            EditorSceneManager.MarkSceneDirty(player.scene);

            Debug.Log("[Setup] Joueur FPS cree et cable. Verifie qu'un [GameSystems] est present dans la scene.", player);
        }

        // ------------------------------------------------------------------

        private static void AttachBodyMesh(GameObject player)
        {
            GameObject bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath);

            if (bodyPrefab == null)
            {
                Debug.LogWarning("[Setup] Mesh de corps introuvable : " + BodyPrefabPath + " (etape ignoree).");
                return;
            }

            GameObject body = PrefabUtility.InstantiatePrefab(bodyPrefab) as GameObject;

            if (body == null)
            {
                return;
            }

            body.name = "Body";
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;

            Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                Debug.LogWarning("[Setup] Le mesh de corps ne contient aucun Renderer.", body);
                return;
            }

            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            if (bounds.size.y > 0.01f)
            {
                float scale = BodyTargetHeight / bounds.size.y;
                body.transform.localScale = Vector3.one * scale;
            }

            // Recalcule apres mise a l'echelle pour poser les pieds au sol.
            bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float feetOffset = bounds.min.y - player.transform.position.y;
            body.transform.localPosition = new Vector3(0f, -feetOffset, 0f);

            // En vue FPS on ne doit pas voir son propre corps, mais on garde son ombre.
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }

            Debug.Log("[Setup] Corps provisoire 'char01' attache (rendu en ombre seule pour la vue FPS).", body);
        }

        private static void DisableOtherAudioListeners(GameObject keep)
        {
            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include);

            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] == null || listeners[i].gameObject == keep)
                {
                    continue;
                }

                Undo.RecordObject(listeners[i], "Disable AudioListener");
                listeners[i].enabled = false;
                Debug.Log("[Setup] AudioListener desactive sur '" + listeners[i].name + "' (un seul est autorise).", listeners[i]);
            }
        }

        private static void DisableOtherCameras(Camera keep)
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);

            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] == null || cameras[i] == keep)
                {
                    continue;
                }

                Undo.RecordObject(cameras[i].gameObject, "Disable Camera");
                cameras[i].gameObject.SetActive(false);
                Debug.Log("[Setup] Camera '" + cameras[i].name + "' desactivee au profit de la camera du joueur.", cameras[i]);
            }
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
            Debug.Log("[Setup] InteractionPromptOverlay ajoute sur '" + host.name + "'.", host);
        }

        /// <summary>Raccourci vers l'utilitaire partage.</summary>
        private static void SetObjectField(Object target, string fieldName, Object value)
        {
            EditorSetupUtility.SetObjectField(target, fieldName, value);
        }
    }
}
