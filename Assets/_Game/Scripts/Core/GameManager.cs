using HouseOfSilence.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HouseOfSilence.Core
{
    /// <summary>
    /// Chef d'orchestre de l'application : etat global, mode de jeu, pause,
    /// fin de partie, transitions de scenes.
    ///
    /// Aucun autre systeme ne doit modifier Time.timeScale ni le curseur :
    /// tout passe par ici.
    ///
    /// Dependance : com.unity.inputsystem (Input System) pour la touche Pause.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoSingleton<GameManager>
    {
        [Header("Scenes")]
        [Tooltip("Nom exact de la scene de menu principal.")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        [Tooltip("Nom exact de la scene de jeu principale.")]
        [SerializeField] private string gameplaySceneName = "Prototype_House";

        [Header("Session")]
        [SerializeField] private GameMode defaultGameMode = GameMode.SinglePlayer;

        [Tooltip("Si la scene de jeu est lancee directement depuis l'editeur, demarre automatiquement une session.")]
        [SerializeField] private bool autoStartInGameplayScene = true;

        [Header("Controles")]
        [Tooltip("Echap met le jeu en pause / reprend.")]
        [SerializeField] private bool handlePauseInput = true;

        [SerializeField] private bool lockCursorWhilePlaying = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = true;

        // ------------------------------------------------------------------
        // Etat
        // ------------------------------------------------------------------

        /// <summary>Etat global courant.</summary>
        public GameState State { get; private set; } = GameState.Boot;

        /// <summary>Mode de la session courante.</summary>
        public GameMode Mode { get; private set; } = GameMode.SinglePlayer;

        /// <summary>Role reseau (Offline tant que le multijoueur n'est pas branche).</summary>
        public NetworkRole Role { get; private set; } = NetworkRole.Offline;

        /// <summary>Resultat de la derniere session terminee.</summary>
        public GameResult LastResult { get; private set; } = GameResult.None;

        /// <summary>Temps de jeu ecoule dans la session courante, en secondes.</summary>
        public float PlayTime { get; private set; }

        public bool IsPlaying { get { return State == GameState.Playing; } }
        public bool IsPaused { get { return State == GameState.Paused; } }

        /// <summary>Vrai si cette machine simule l'IA, les objectifs et les evenements.</summary>
        public bool HasAuthority { get { return Role.HasAuthority(); } }

        public string MainMenuSceneName { get { return mainMenuSceneName; } }
        public string GameplaySceneName { get { return gameplaySceneName; } }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            Mode = defaultGameMode;
            Application.targetFrameRate = -1;

            if (verboseLogs)
            {
                Debug.Log("[GameManager] Initialise. Mode par defaut : " + Mode);
            }
        }

        private void Start()
        {
            if (State != GameState.Boot)
            {
                return;
            }

            string activeScene = SceneManager.GetActiveScene().name;

            if (activeScene == mainMenuSceneName)
            {
                SetState(GameState.MainMenu);
            }
            else if (autoStartInGameplayScene)
            {
                // Utile pour tester : on lance Play directement dans la scene de jeu.
                StartSession(defaultGameMode, NetworkRole.Offline);
            }
            else
            {
                SetState(GameState.MainMenu);
            }
        }

        private void Update()
        {
            if (IsPlaying)
            {
                PlayTime += Time.deltaTime;
            }

            if (!handlePauseInput)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return;
            }

            if (keyboard[Key.Escape].wasPressedThisFrame)
            {
                TogglePause();
            }
        }

        // ------------------------------------------------------------------
        // API publique
        // ------------------------------------------------------------------

        /// <summary>
        /// Demarre une session dans la scene deja chargee.
        /// </summary>
        public void StartSession(GameMode mode, NetworkRole role = NetworkRole.Offline)
        {
            Mode = mode;
            Role = role;
            PlayTime = 0f;
            LastResult = GameResult.None;

            if (!SetState(GameState.Playing))
            {
                return;
            }

            if (verboseLogs)
            {
                Debug.Log("[GameManager] Session demarree. Mode : " + mode + " / Role : " + role);
            }

            EventBus.Publish(new GameStartedEvent(mode, role));
        }

        /// <summary>
        /// Charge la scene de jeu puis demarre une session (utilise par le menu principal).
        /// </summary>
        public void LoadGameplayAndStart(GameMode mode, NetworkRole role = NetworkRole.Offline)
        {
            Mode = mode;
            Role = role;

            SetState(GameState.Loading);

            SceneLoader loader = SceneLoader.Instance;

            if (loader == null)
            {
                Debug.LogError("[GameManager] SceneLoader indisponible.", this);
                return;
            }

            loader.LoadScene(gameplaySceneName, OnGameplaySceneLoaded);
        }

        private void OnGameplaySceneLoaded()
        {
            StartSession(Mode, Role);
        }

        /// <summary>Retourne au menu principal.</summary>
        public void GoToMainMenu()
        {
            SetState(GameState.Loading);

            SceneLoader loader = SceneLoader.Instance;

            if (loader == null)
            {
                Debug.LogError("[GameManager] SceneLoader indisponible.", this);
                return;
            }

            loader.LoadScene(mainMenuSceneName, OnMainMenuSceneLoaded);
        }

        private void OnMainMenuSceneLoaded()
        {
            Role = NetworkRole.Offline;
            PlayTime = 0f;
            SetState(GameState.MainMenu);
        }

        /// <summary>Relance la scene courante et redemarre une session.</summary>
        public void RestartSession()
        {
            GameMode mode = Mode;
            NetworkRole role = Role;

            SetState(GameState.Loading);

            SceneLoader loader = SceneLoader.Instance;

            if (loader == null)
            {
                Debug.LogError("[GameManager] SceneLoader indisponible.", this);
                return;
            }

            string sceneToReload = SceneManager.GetActiveScene().name;

            loader.LoadScene(sceneToReload, delegate { StartSession(mode, role); });
        }

        /// <summary>Bascule pause / reprise. Sans effet hors d'une partie.</summary>
        public void TogglePause()
        {
            if (IsPlaying)
            {
                Pause();
            }
            else if (IsPaused)
            {
                Resume();
            }
        }

        public void Pause()
        {
            if (!IsPlaying)
            {
                return;
            }

            if (SetState(GameState.Paused))
            {
                EventBus.Publish(new GamePauseChangedEvent(true));
            }
        }

        public void Resume()
        {
            if (!IsPaused)
            {
                return;
            }

            if (SetState(GameState.Playing))
            {
                EventBus.Publish(new GamePauseChangedEvent(false));
            }
        }

        /// <summary>Termine la partie. Appele par le systeme de mort ou la porte de sortie.</summary>
        public void EndGame(GameResult result)
        {
            if (State != GameState.Playing && State != GameState.Paused)
            {
                return;
            }

            LastResult = result;

            GameState target = result == GameResult.Victory ? GameState.Victory : GameState.GameOver;

            if (!SetState(target))
            {
                return;
            }

            if (verboseLogs)
            {
                Debug.Log("[GameManager] Partie terminee : " + result + " (" + PlayTime.ToString("F1") + "s)");
            }

            EventBus.Publish(new GameEndedEvent(result, PlayTime));
        }

        /// <summary>Quitte l'application (arrete le Play Mode dans l'editeur).</summary>
        public void QuitGame()
        {
            SetState(GameState.Quitting);

            if (verboseLogs)
            {
                Debug.Log("[GameManager] Fermeture demandee.");
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------
        // Machine a etats
        // ------------------------------------------------------------------

        /// <summary>
        /// Change l'etat global. Renvoie false si la transition est refusee.
        /// </summary>
        public bool SetState(GameState next)
        {
            if (State == next)
            {
                return false;
            }

            if (!IsTransitionAllowed(State, next))
            {
                Debug.LogWarning("[GameManager] Transition refusee : " + State + " -> " + next, this);
                return false;
            }

            GameState previous = State;
            State = next;

            ApplyStateSideEffects(next);

            if (verboseLogs)
            {
                Debug.Log("[GameManager] Etat : " + previous + " -> " + next);
            }

            EventBus.Publish(new GameStateChangedEvent(previous, next));
            return true;
        }

        private static bool IsTransitionAllowed(GameState from, GameState to)
        {
            if (from == to)
            {
                return false;
            }

            switch (from)
            {
                case GameState.Boot:
                    return to == GameState.MainMenu || to == GameState.Loading || to == GameState.Playing;

                case GameState.MainMenu:
                    return to == GameState.Loading || to == GameState.Playing || to == GameState.Quitting;

                case GameState.Loading:
                    return to == GameState.Playing || to == GameState.MainMenu || to == GameState.Quitting;

                case GameState.Playing:
                    return to == GameState.Paused
                        || to == GameState.GameOver
                        || to == GameState.Victory
                        || to == GameState.Loading
                        || to == GameState.MainMenu
                        || to == GameState.Quitting;

                case GameState.Paused:
                    return to == GameState.Playing
                        || to == GameState.Loading
                        || to == GameState.MainMenu
                        || to == GameState.GameOver
                        || to == GameState.Victory
                        || to == GameState.Quitting;

                case GameState.GameOver:
                case GameState.Victory:
                    return to == GameState.Loading
                        || to == GameState.MainMenu
                        || to == GameState.Playing
                        || to == GameState.Quitting;

                case GameState.Quitting:
                    return false;

                default:
                    return true;
            }
        }

        private void ApplyStateSideEffects(GameState state)
        {
            switch (state)
            {
                case GameState.Playing:
                    Time.timeScale = 1f;
                    AudioListener.pause = false;
                    SetCursorLocked(lockCursorWhilePlaying);
                    break;

                case GameState.Paused:
                    Time.timeScale = 0f;
                    AudioListener.pause = true;
                    SetCursorLocked(false);
                    break;

                case GameState.Boot:
                case GameState.MainMenu:
                case GameState.Loading:
                case GameState.GameOver:
                case GameState.Victory:
                case GameState.Quitting:
                    Time.timeScale = 1f;
                    AudioListener.pause = false;
                    SetCursorLocked(false);
                    break;
            }
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        protected override void OnDestroy()
        {
            // Securite : ne jamais laisser le moteur fige si le manager disparait.
            Time.timeScale = 1f;
            AudioListener.pause = false;

            base.OnDestroy();
        }
    }
}
