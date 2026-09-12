using HouseOfSilence.Core;
using HouseOfSilence.Core.Debugging;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Outils d'edition de la Phase 1.
    /// Menu : Tools > House of Silence > ...
    /// Ce fichier est dans un dossier "Editor" : il n'est jamais inclus dans la build.
    ///
    /// Deux objets distincts par scene :
    ///  - "[Core]" : GameManager, SceneLoader, DebugManager (+ commandes et logger).
    ///    Persistant (DontDestroyOnLoad) : il traverse les changements de scene.
    ///    Quand une scene chargee en apporte un second, le doublon est detruit.
    ///  - "[GameSystems]" : managers lies a la scene (objectifs, lumieres,
    ///    horreur, HUD...). Detruit avec la scene, comme il se doit.
    /// Les melanger ferait survivre les managers de niveau au retour au menu.
    /// </summary>
    public static class CoreSetupMenu
    {
        public const string CoreObjectName = "[Core]";
        public const string SystemsObjectName = "[GameSystems]";

        [MenuItem("Tools/House of Silence/Create Core Systems", false, 0)]
        public static void CreateCoreSystems()
        {
            GameObject core = EnsureCore();
            GameObject systems = EnsureSystemsHost();

            Selection.activeGameObject = core;
            EditorSceneManager.MarkSceneDirty(core.scene);

            Debug.Log("[Setup] " + CoreObjectName + " (GameManager, SceneLoader, DebugManager, CoreDebugCommands, CoreEventLogger) et " + SystemsObjectName + " prets.", systems);
        }

        /// <summary>Cree "[Core]" s'il manque et garantit ses cinq composants.</summary>
        public static GameObject EnsureCore()
        {
            GameManager existing = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            GameObject core;

            if (existing != null)
            {
                core = existing.gameObject;
            }
            else
            {
                core = new GameObject(CoreObjectName);
                Undo.RegisterCreatedObjectUndo(core, "Create Core Systems");
            }

            EditorSetupUtility.EnsureComponent<GameManager>(core);
            EditorSetupUtility.EnsureComponent<SceneLoader>(core);
            EditorSetupUtility.EnsureComponent<DebugManager>(core);
            EditorSetupUtility.EnsureComponent<CoreDebugCommands>(core);
            EditorSetupUtility.EnsureComponent<CoreEventLogger>(core);

            return core;
        }

        /// <summary>Cree l'hote "[GameSystems]" des managers de scene s'il manque.</summary>
        public static GameObject EnsureSystemsHost()
        {
            GameObject host = GameObject.Find(SystemsObjectName);

            if (host != null)
            {
                return host;
            }

            host = new GameObject(SystemsObjectName);
            Undo.RegisterCreatedObjectUndo(host, "Create GameSystems");
            return host;
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

        /// <summary>
        /// Migration des anciennes scenes ou tout vivait sur "[GameSystems]" :
        /// deplace les composants du noyau vers un "[Core]" separe.
        /// </summary>
        [MenuItem("Tools/House of Silence/Split Core From GameSystems", false, 2)]
        public static void SplitCoreFromGameSystems()
        {
            GameManager manager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);

            if (manager == null)
            {
                Debug.LogWarning("[Setup] Aucun GameManager dans la scene : rien a separer.");
                return;
            }

            GameObject host = manager.gameObject;

            if (host.name == CoreObjectName)
            {
                Debug.Log("[Setup] Le noyau est deja sur " + CoreObjectName + ".");
                return;
            }

            GameObject core = new GameObject(CoreObjectName);
            Undo.RegisterCreatedObjectUndo(core, "Split Core");

            MoveComponent<GameManager>(host, core);
            MoveComponent<SceneLoader>(host, core);
            MoveComponent<DebugManager>(host, core);
            MoveComponent<CoreDebugCommands>(host, core);
            MoveComponent<CoreEventLogger>(host, core);

            if (host.name != SystemsObjectName)
            {
                host.name = SystemsObjectName;
            }

            EditorSceneManager.MarkSceneDirty(core.scene);
            Debug.Log("[Setup] Noyau deplace de '" + host.name + "' vers " + CoreObjectName + ".", core);
        }

        private static void MoveComponent<T>(GameObject from, GameObject to) where T : Component
        {
            T source = from.GetComponent<T>();

            if (source == null)
            {
                EditorSetupUtility.EnsureComponent<T>(to);
                return;
            }

            // Copie avec ses valeurs d'Inspector, puis suppression de l'original.
            if (ComponentUtility.CopyComponent(source) && ComponentUtility.PasteComponentAsNew(to))
            {
                Undo.DestroyObjectImmediate(source);
            }
            else
            {
                Debug.LogWarning("[Setup] Impossible de copier " + typeof(T).Name + ", composant recree avec les valeurs par defaut.");
                EditorSetupUtility.EnsureComponent<T>(to);
                Undo.DestroyObjectImmediate(source);
            }
        }
    }
}
