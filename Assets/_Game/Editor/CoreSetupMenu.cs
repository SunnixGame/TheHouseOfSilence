using HouseOfSilence.Core;
using HouseOfSilence.Core.Debugging;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Outils d'edition de la Phase 1.
    /// Menu : Tools > House of Silence > ...
    /// Ce fichier est dans un dossier "Editor" : il n'est jamais inclus dans la build.
    /// </summary>
    public static class CoreSetupMenu
    {
        private const string SystemsObjectName = "[GameSystems]";

        [MenuItem("Tools/House of Silence/Create Core Systems", false, 0)]
        public static void CreateCoreSystems()
        {
            GameManager existing = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);

            if (existing != null)
            {
                Debug.LogWarning("[Setup] Un GameManager existe deja dans la scene : " + existing.name, existing);
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            GameObject systems = new GameObject(SystemsObjectName);

            Undo.RegisterCreatedObjectUndo(systems, "Create Core Systems");

            systems.AddComponent<GameManager>();
            systems.AddComponent<SceneLoader>();
            systems.AddComponent<DebugManager>();
            systems.AddComponent<CoreDebugCommands>();
            systems.AddComponent<CoreEventLogger>();

            Selection.activeGameObject = systems;
            EditorSceneManager.MarkSceneDirty(systems.scene);

            Debug.Log("[Setup] " + SystemsObjectName + " cree avec GameManager, SceneLoader, DebugManager, CoreDebugCommands, CoreEventLogger.", systems);
        }

        [MenuItem("Tools/House of Silence/Select Core Systems", false, 1)]
        public static void SelectCoreSystems()
        {
            GameManager existing = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);

            if (existing == null)
            {
                Debug.LogWarning("[Setup] Aucun GameManager dans la scene. Utilise Tools > House of Silence > Create Core Systems.");
                return;
            }

            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
        }
    }
}
