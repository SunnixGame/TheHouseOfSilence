using System;
using HouseOfSilence.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Traduit l'InputActionAsset en proprietes simples consommees par le joueur.
    /// Aucun autre script ne doit lire l'Input System directement.
    ///
    /// - Utilise l'asset "InputSystem_Actions" fourni avec le projet
    ///   (map "Player" : Move, Look, Sprint, Crouch, Jump, Interact, Attack, Previous, Next).
    /// - Active / desactive automatiquement la map "Player" selon l'etat du jeu :
    ///   plus aucun input de gameplay pendant une pause, un chargement ou un menu.
    ///
    /// Package requis : com.unity.inputsystem (deja installe).
    /// </summary>
    [DisallowMultipleComponent]
    public class InputReader : MonoBehaviour
    {
        private const string PlayerMapName = "Player";
        private const string UIMapName = "UI";

        [Header("Asset")]
        [Tooltip("Glisser Assets/InputSystem_Actions.inputactions")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("Options")]
        [Tooltip("Si vrai, la map Player n'est active que pendant l'etat Playing.")]
        [SerializeField] private bool followGameState = true;

        [Tooltip("Crouch en maintien (false = bascule a chaque appui).")]
        [SerializeField] private bool crouchIsHold = true;

        private InputActionMap _playerMap;
        private InputActionMap _uiMap;

        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _sprintAction;
        private InputAction _crouchAction;
        private InputAction _jumpAction;
        private InputAction _interactAction;
        private InputAction _attackAction;
        private InputAction _previousAction;
        private InputAction _nextAction;
        private InputAction _dropAction;

        private bool _crouchToggleState;
        private bool _initialized;
        private bool _inputEnabled;

        // ------------------------------------------------------------------
        // Lecture
        // ------------------------------------------------------------------

        /// <summary>Deplacement brut (x = lateral, y = avant/arriere), normalise a 1 maximum.</summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>Delta de visee de cette frame (souris ou stick).</summary>
        public Vector2 LookInput { get; private set; }

        /// <summary>Touche sprint maintenue.</summary>
        public bool SprintHeld { get; private set; }

        /// <summary>Etat accroupi demande (gere le mode maintien ou bascule).</summary>
        public bool CrouchRequested { get; private set; }

        /// <summary>Saut demande cette frame.</summary>
        public bool JumpPressed { get; private set; }

        /// <summary>Interaction demandee cette frame (appui court).</summary>
        public bool InteractPressed { get; private set; }

        /// <summary>Touche d'interaction maintenue (interactions a maintien : reparer, demarrer...).</summary>
        public bool InteractHeld { get; private set; }

        /// <summary>Touche d'interaction relachee cette frame.</summary>
        public bool InteractReleased { get; private set; }

        /// <summary>Vrai si les inputs de gameplay sont actuellement actifs.</summary>
        public bool InputEnabled { get { return _inputEnabled; } }

        /// <summary>Declenche a l'appui sur Interact. Utilise par le systeme d'interaction.</summary>
        public event Action OnInteract;

        /// <summary>Declenche a l'appui sur Attack / clic gauche (lampe, objets utilisables).</summary>
        public event Action OnUseItem;

        /// <summary>Changement de slot d'inventaire : -1 (precedent) ou +1 (suivant).</summary>
        public event Action<int> OnSlotChanged;

        /// <summary>Demande de lacher l'objet selectionne.</summary>
        public event Action OnDropItem;

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            Initialize();

            if (!_initialized)
            {
                return;
            }

            EventBus.Subscribe<GameStateChangedEvent>(OnGameStateChanged);

            bool shouldEnable = !followGameState
                || !GameManager.HasInstance
                || GameManager.Instance == null
                || GameManager.Instance.State == GameState.Playing;

            SetInputEnabled(shouldEnable);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
            SetInputEnabled(false);
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            if (inputActions == null)
            {
                Debug.LogError("[InputReader] Aucun InputActionAsset assigne. Glisse Assets/InputSystem_Actions.inputactions dans le champ 'Input Actions'.", this);
                return;
            }

            _playerMap = inputActions.FindActionMap(PlayerMapName, false);
            _uiMap = inputActions.FindActionMap(UIMapName, false);

            if (_playerMap == null)
            {
                Debug.LogError("[InputReader] Action Map '" + PlayerMapName + "' introuvable dans " + inputActions.name + ".", this);
                return;
            }

            _moveAction = _playerMap.FindAction("Move", false);
            _lookAction = _playerMap.FindAction("Look", false);
            _sprintAction = _playerMap.FindAction("Sprint", false);
            _crouchAction = _playerMap.FindAction("Crouch", false);
            _jumpAction = _playerMap.FindAction("Jump", false);
            _interactAction = _playerMap.FindAction("Interact", false);
            _attackAction = _playerMap.FindAction("Attack", false);
            _previousAction = _playerMap.FindAction("Previous", false);
            _nextAction = _playerMap.FindAction("Next", false);
            _dropAction = _playerMap.FindAction("Drop", false);

            WarnIfMissing(_moveAction, "Move");
            WarnIfMissing(_lookAction, "Look");
            WarnIfMissing(_sprintAction, "Sprint");
            WarnIfMissing(_crouchAction, "Crouch");
            WarnIfMissing(_jumpAction, "Jump");
            WarnIfMissing(_interactAction, "Interact");

            _initialized = true;
        }

        private void WarnIfMissing(InputAction action, string actionName)
        {
            if (action == null)
            {
                Debug.LogWarning("[InputReader] Action '" + actionName + "' introuvable dans la map Player.", this);
            }
        }

        private void OnGameStateChanged(GameStateChangedEvent evt)
        {
            if (!followGameState)
            {
                return;
            }

            SetInputEnabled(evt.Current == GameState.Playing);
        }

        /// <summary>Active ou coupe totalement les inputs de gameplay.</summary>
        public void SetInputEnabled(bool enabled)
        {
            if (!_initialized || _playerMap == null)
            {
                return;
            }

            if (_inputEnabled == enabled)
            {
                return;
            }

            _inputEnabled = enabled;

            if (enabled)
            {
                _playerMap.Enable();
            }
            else
            {
                _playerMap.Disable();
                ClearInputs();
            }

            if (_uiMap != null)
            {
                // La map UI reste toujours active (menus, pause).
                if (!_uiMap.enabled)
                {
                    _uiMap.Enable();
                }
            }
        }

        private void ClearInputs()
        {
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            SprintHeld = false;
            JumpPressed = false;
            InteractPressed = false;
            InteractHeld = false;
            InteractReleased = false;
            CrouchRequested = false;
            _crouchToggleState = false;
        }

        // ------------------------------------------------------------------
        // Mise a jour
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!_initialized || !_inputEnabled)
            {
                return;
            }

            Vector2 rawMove = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
            MoveInput = rawMove.sqrMagnitude > 1f ? rawMove.normalized : rawMove;

            LookInput = _lookAction != null ? _lookAction.ReadValue<Vector2>() : Vector2.zero;
            SprintHeld = _sprintAction != null && _sprintAction.IsPressed();

            JumpPressed = _jumpAction != null && _jumpAction.WasPressedThisFrame();
            InteractPressed = _interactAction != null && _interactAction.WasPressedThisFrame();
            InteractHeld = _interactAction != null && _interactAction.IsPressed();
            InteractReleased = _interactAction != null && _interactAction.WasReleasedThisFrame();

            UpdateCrouch();

            if (InteractPressed && OnInteract != null)
            {
                OnInteract.Invoke();
            }

            if (_attackAction != null && _attackAction.WasPressedThisFrame() && OnUseItem != null)
            {
                OnUseItem.Invoke();
            }

            if (OnSlotChanged != null)
            {
                if (_previousAction != null && _previousAction.WasPressedThisFrame())
                {
                    OnSlotChanged.Invoke(-1);
                }

                if (_nextAction != null && _nextAction.WasPressedThisFrame())
                {
                    OnSlotChanged.Invoke(1);
                }
            }

            if (_dropAction != null && _dropAction.WasPressedThisFrame() && OnDropItem != null)
            {
                OnDropItem.Invoke();
            }
        }

        private void UpdateCrouch()
        {
            if (_crouchAction == null)
            {
                CrouchRequested = false;
                return;
            }

            if (crouchIsHold)
            {
                CrouchRequested = _crouchAction.IsPressed();
                return;
            }

            if (_crouchAction.WasPressedThisFrame())
            {
                _crouchToggleState = !_crouchToggleState;
            }

            CrouchRequested = _crouchToggleState;
        }

        /// <summary>
        /// Libelle de la touche d'interaction, tel qu'il apparait sur le clavier
        /// reellement branche (l'Input System tient compte de la disposition :
        /// un clavier AZERTY n'affichera pas la meme lettre qu'un QWERTY).
        /// Utilise par l'UI pour afficher [E] INTERAGIR.
        /// </summary>
        public string GetInteractDisplayKey()
        {
            if (_interactAction == null)
            {
                return "E";
            }

            string display = _interactAction.GetBindingDisplayString();

            if (string.IsNullOrEmpty(display))
            {
                return "E";
            }

            // GetBindingDisplayString peut renvoyer "E | Bouton Ouest" : on garde le premier.
            int separator = display.IndexOf('|');

            if (separator > 0)
            {
                display = display.Substring(0, separator);
            }

            return display.Trim();
        }

        /// <summary>
        /// Force la sortie de l'accroupissement (ex : la creature attrape le joueur).
        /// Utile quand crouchIsHold est desactive.
        /// </summary>
        public void ForceStandUp()
        {
            _crouchToggleState = false;
            CrouchRequested = false;
        }
    }
}
