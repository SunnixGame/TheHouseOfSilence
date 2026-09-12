using HouseOfSilence.Horror;
using HouseOfSilence.Lights;
using HouseOfSilence.Player;
using HouseOfSilence.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Outils de la Phase 7.
    /// Menu : Tools > House of Silence > ...
    /// </summary>
    public static class FearSetupMenu
    {
        /// <summary>Met a niveau un joueur existant avec le systeme de peur complet.</summary>
        [MenuItem("Tools/House of Silence/Add Fear To Player", false, 23)]
        public static void AddFearToPlayer()
        {
            PlayerCharacter player = Object.FindAnyObjectByType<PlayerCharacter>(FindObjectsInactive.Include);

            if (player == null)
            {
                Debug.LogWarning("[Setup] Aucun joueur dans la scene. Utilise d'abord Tools > House of Silence > Create Player.");
                return;
            }

            AddFearComponents(player);
            EnsureFearHud();

            Selection.activeGameObject = player.gameObject;
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);

            Debug.Log("[Setup] Systeme de peur ajoute sur '" + player.name + "' : FearSystem, LightLevelSensor, FearEffects, FearAudioFeedback.", player);
        }

        /// <summary>Pose tous les composants de peur sur un joueur (utilise aussi par Create Player).</summary>
        public static void AddFearComponents(PlayerCharacter player)
        {
            if (player == null)
            {
                return;
            }

            GameObject go = player.gameObject;

            LightLevelSensor sensor = EditorSetupUtility.EnsureComponent<LightLevelSensor>(go);

            if (player.Camera != null)
            {
                EditorSetupUtility.SetObjectField(sensor, "samplePoint", player.Camera.transform);
            }

            EditorSetupUtility.EnsureComponent<FearSystem>(go);

            FearEffects effects = EditorSetupUtility.EnsureComponent<FearEffects>(go);
            EditorSetupUtility.SetObjectField(effects, "player", player);
            EditorSetupUtility.SetObjectField(effects, "look", go.GetComponent<PlayerLook>());

            FearAudioFeedback audio = EditorSetupUtility.EnsureComponent<FearAudioFeedback>(go);
            EditorSetupUtility.SetObjectField(audio, "player", player);
        }

        /// <summary>Ajoute la jauge de peur temporaire si elle manque.</summary>
        public static void EnsureFearHud()
        {
            FearHudOverlay overlay = Object.FindAnyObjectByType<FearHudOverlay>(FindObjectsInactive.Include);

            if (overlay != null)
            {
                return;
            }

            GameObject host = GameObject.Find("[GameSystems]");

            if (host == null)
            {
                host = GameObject.Find("[HUD]");
            }

            if (host == null)
            {
                host = new GameObject("[HUD]");
                Undo.RegisterCreatedObjectUndo(host, "Create HUD");
            }

            Undo.AddComponent<FearHudOverlay>(host);
            Debug.Log("[Setup] FearHudOverlay ajoute sur '" + host.name + "' (HUD temporaire, remplace en Phase 15).", host);
        }

        // ------------------------------------------------------------------

        /// <summary>Cree une zone sure devant la vue de scene.</summary>
        [MenuItem("Tools/House of Silence/Create Safe Zone", false, 103)]
        public static void CreateSafeZone()
        {
            GameObject zone = new GameObject("SafeZone");

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 position = view != null ? view.pivot : Vector3.zero;
            position.y = 0f;
            zone.transform.position = position;

            Undo.RegisterCreatedObjectUndo(zone, "Create Safe Zone");

            BoxCollider box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(5f, 3f, 5f);
            box.center = new Vector3(0f, 1.5f, 0f);

            zone.AddComponent<SafeZone>();

            Selection.activeGameObject = zone;
            EditorSceneManager.MarkSceneDirty(zone.scene);

            Debug.Log("[Setup] Zone sure creee : la peur y redescend rapidement.", zone);
        }
    }
}
