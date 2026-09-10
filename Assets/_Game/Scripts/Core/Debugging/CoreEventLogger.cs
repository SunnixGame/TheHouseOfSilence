using UnityEngine;

namespace HouseOfSilence.Core.Debugging
{
    /// <summary>
    /// Ecoute les evenements du Core et les affiche dans la console.
    /// Sert de test de bon fonctionnement de l'EventBus et d'exemple
    /// d'abonnement / desabonnement propre.
    ///
    /// Peut etre desactive ou supprime a tout moment : aucun autre systeme
    /// ne depend de lui.
    /// </summary>
    [DisallowMultipleComponent]
    public class CoreEventLogger : MonoBehaviour
    {
        [SerializeField] private bool logGameState = true;
        [SerializeField] private bool logSceneLoading = true;

        private void OnEnable()
        {
            EventBus.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
            EventBus.Subscribe<GameStartedEvent>(OnGameStarted);
            EventBus.Subscribe<GamePauseChangedEvent>(OnPauseChanged);
            EventBus.Subscribe<GameEndedEvent>(OnGameEnded);
            EventBus.Subscribe<SceneLoadStartedEvent>(OnSceneLoadStarted);
            EventBus.Subscribe<SceneLoadCompletedEvent>(OnSceneLoadCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
            EventBus.Unsubscribe<GameStartedEvent>(OnGameStarted);
            EventBus.Unsubscribe<GamePauseChangedEvent>(OnPauseChanged);
            EventBus.Unsubscribe<GameEndedEvent>(OnGameEnded);
            EventBus.Unsubscribe<SceneLoadStartedEvent>(OnSceneLoadStarted);
            EventBus.Unsubscribe<SceneLoadCompletedEvent>(OnSceneLoadCompleted);
        }

        private void OnGameStateChanged(GameStateChangedEvent evt)
        {
            if (!logGameState)
            {
                return;
            }

            Debug.Log("[EVENT] GameStateChanged : " + evt.Previous + " -> " + evt.Current);
        }

        private void OnGameStarted(GameStartedEvent evt)
        {
            if (!logGameState)
            {
                return;
            }

            Debug.Log("[EVENT] GameStarted : " + evt.Mode + " / " + evt.Role);
        }

        private void OnPauseChanged(GamePauseChangedEvent evt)
        {
            if (!logGameState)
            {
                return;
            }

            Debug.Log("[EVENT] PauseChanged : " + (evt.IsPaused ? "PAUSE" : "REPRISE"));
        }

        private void OnGameEnded(GameEndedEvent evt)
        {
            if (!logGameState)
            {
                return;
            }

            Debug.Log("[EVENT] GameEnded : " + evt.Result + " apres " + evt.PlayTime.ToString("F1") + "s");
        }

        private void OnSceneLoadStarted(SceneLoadStartedEvent evt)
        {
            if (!logSceneLoading)
            {
                return;
            }

            Debug.Log("[EVENT] SceneLoadStarted : " + evt.SceneName);
        }

        private void OnSceneLoadCompleted(SceneLoadCompletedEvent evt)
        {
            if (!logSceneLoading)
            {
                return;
            }

            Debug.Log("[EVENT] SceneLoadCompleted : " + evt.SceneName + " (success = " + evt.Success + ")");
        }
    }
}
