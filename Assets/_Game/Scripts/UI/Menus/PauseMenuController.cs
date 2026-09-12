using HouseOfSilence.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HouseOfSilence.UI.Menus
{
    /// <summary>
    /// Menu de pause et ecran de fin de partie, dans la scene de jeu.
    ///
    /// Il ne decide de rien : il ecoute GameStateChangedEvent et s'affiche
    /// quand le GameManager passe en Paused, GameOver ou Victory. La touche
    /// Echap est geree par le GameManager (TogglePause), le timeScale et le
    /// curseur aussi. Les boutons appellent simplement l'API du GameManager.
    ///
    /// Reprendre n'est visible qu'en pause, Rejouer qu'en fin de partie.
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenuController : MonoBehaviour
    {
        [Header("Racine")]
        [Tooltip("Objet active/desactive avec le menu (fond sombre + panneau).")]
        [SerializeField] private GameObject root;

        [Header("Textes")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text hintText;

        [Header("Boutons")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private Button quitButton;

        [Header("Libelles")]
        [SerializeField] private string pauseTitle = "PAUSE";
        [SerializeField] private string pauseHint = "Echap pour reprendre";
        [SerializeField] private string gameOverTitle = "VOUS N'AVEZ PAS SURVECU";
        [SerializeField] private string victoryTitle = "VOUS AVEZ SURVECU";

        private bool _visible;

        private void Awake()
        {
            if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(MainMenu);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);

            Hide();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        }

        private void Start()
        {
            // Si le menu arrive alors que la partie est deja en pause (ajout tardif).
            if (GameManager.HasInstance)
            {
                Apply(GameManager.Instance.State);
            }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
        }

        private void Update()
        {
            if (!_visible)
            {
                return;
            }

            // Garde une selection pour la navigation clavier / manette.
            EventSystem eventSystem = EventSystem.current;

            if (eventSystem != null && eventSystem.currentSelectedGameObject == null)
            {
                Button first = resumeButton != null && resumeButton.gameObject.activeSelf ? resumeButton
                    : (restartButton != null && restartButton.gameObject.activeSelf ? restartButton : mainMenuButton);

                if (first != null)
                {
                    eventSystem.SetSelectedGameObject(first.gameObject);
                }
            }
        }

        private void OnDestroy()
        {
            if (resumeButton != null) resumeButton.onClick.RemoveListener(Resume);
            if (restartButton != null) restartButton.onClick.RemoveListener(Restart);
            if (mainMenuButton != null) mainMenuButton.onClick.RemoveListener(MainMenu);
            if (quitButton != null) quitButton.onClick.RemoveListener(Quit);
        }

        // ------------------------------------------------------------------

        private void OnGameStateChanged(GameStateChangedEvent evt)
        {
            Apply(evt.Current);
        }

        private void Apply(GameState state)
        {
            switch (state)
            {
                case GameState.Paused:
                    Show(pauseTitle, pauseHint, true, false);
                    break;

                case GameState.GameOver:
                    Show(gameOverTitle, FormatPlayTime(), false, true);
                    break;

                case GameState.Victory:
                    Show(victoryTitle, FormatPlayTime(), false, true);
                    break;

                default:
                    Hide();
                    break;
            }
        }

        private void Show(string title, string hint, bool canResume, bool canRestart)
        {
            if (root != null)
            {
                root.SetActive(true);
            }

            if (titleText != null) titleText.text = title;
            if (hintText != null) hintText.text = hint;
            if (resumeButton != null) resumeButton.gameObject.SetActive(canResume);
            if (restartButton != null) restartButton.gameObject.SetActive(canRestart);

            _visible = true;

            // Bouton par defaut pour clavier / manette.
            Button first = canResume ? resumeButton : (canRestart ? restartButton : mainMenuButton);

            if (EventSystem.current != null && first != null)
            {
                EventSystem.current.SetSelectedGameObject(first.gameObject);
            }
        }

        private void Hide()
        {
            if (root != null)
            {
                root.SetActive(false);
            }

            _visible = false;
        }

        private static string FormatPlayTime()
        {
            if (!GameManager.HasInstance)
            {
                return string.Empty;
            }

            float seconds = GameManager.Instance.PlayTime;
            int minutes = Mathf.FloorToInt(seconds / 60f);
            int rest = Mathf.FloorToInt(seconds - minutes * 60f);
            return "Temps de survie : " + minutes.ToString("00") + ":" + rest.ToString("00");
        }

        // ------------------------------------------------------------------
        // Boutons
        // ------------------------------------------------------------------

        public void Resume()
        {
            if (!_visible || !GameManager.HasInstance)
            {
                return;
            }

            GameManager.Instance.Resume();
        }

        public void Restart()
        {
            if (!_visible || !GameManager.HasInstance)
            {
                return;
            }

            Hide();
            GameManager.Instance.RestartSession();
        }

        public void MainMenu()
        {
            if (!_visible || !GameManager.HasInstance)
            {
                return;
            }

            Hide();
            GameManager.Instance.GoToMainMenu();
        }

        public void Quit()
        {
            if (!GameManager.HasInstance)
            {
                Application.Quit();
                return;
            }

            GameManager.Instance.QuitGame();
        }
    }
}
