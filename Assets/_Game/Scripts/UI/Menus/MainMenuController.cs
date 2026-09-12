using HouseOfSilence.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HouseOfSilence.UI.Menus
{
    /// <summary>
    /// Menu principal : JOUER charge la scene de jeu et demarre une session,
    /// QUITTER ferme l'application.
    ///
    /// Le menu ne fait que parler au GameManager : c'est lui qui gere l'etat,
    /// le curseur et le chargement. Les boutons sont cables par code dans
    /// Awake, il n'y a rien a brancher dans l'Inspector a part les references.
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuController : MonoBehaviour
    {
        [Header("Boutons")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button quitButton;

        [Header("Session")]
        [SerializeField] private GameMode gameMode = GameMode.SinglePlayer;

        [Header("Textes")]
        [SerializeField] private Text versionText;

        private bool _launching;

        private void Awake()
        {
            if (playButton != null)
            {
                playButton.onClick.AddListener(Play);
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(Quit);
            }

            if (versionText != null)
            {
                versionText.text = "Prototype " + Application.version + "  -  Unity " + Application.unityVersion;
            }
        }

        private void Start()
        {
            // Le curseur est libere par le GameManager (etat MainMenu) ; on
            // preselectionne JOUER pour la navigation clavier / manette.
            if (EventSystem.current != null && playButton != null)
            {
                EventSystem.current.SetSelectedGameObject(playButton.gameObject);
            }
        }

        private void Update()
        {
            // Une selection est necessaire pour naviguer au clavier / a la manette ;
            // un clic dans le vide la perd, on la remet sur JOUER.
            EventSystem eventSystem = EventSystem.current;

            if (!_launching && eventSystem != null && eventSystem.currentSelectedGameObject == null && playButton != null)
            {
                eventSystem.SetSelectedGameObject(playButton.gameObject);
            }
        }

        private void OnDestroy()
        {
            if (playButton != null)
            {
                playButton.onClick.RemoveListener(Play);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(Quit);
            }
        }

        // ------------------------------------------------------------------

        public void Play()
        {
            if (_launching)
            {
                return;
            }

            GameManager game = GameManager.Instance;

            if (game == null)
            {
                Debug.LogError("[MainMenu] GameManager introuvable : ajoute [Core] a la scene (Tools > House of Silence > Create Core Systems).", this);
                return;
            }

            _launching = true;
            SetInteractable(false);

            game.LoadGameplayAndStart(gameMode, NetworkRole.Offline);
        }

        public void Quit()
        {
            GameManager game = GameManager.Instance;

            if (game != null)
            {
                game.QuitGame();
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetInteractable(bool interactable)
        {
            if (playButton != null)
            {
                playButton.interactable = interactable;
            }

            if (quitButton != null)
            {
                quitButton.interactable = interactable;
            }
        }
    }
}
