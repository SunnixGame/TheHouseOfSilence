using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Mode vol PROVISOIRE (outil de test) : V active / desactive.
    /// ZQSD = avancer dans la direction du regard, Espace = monter, Ctrl = descendre,
    /// Maj = plus vite. Traverse les murs si Pass Through Walls est coche.
    ///
    /// Pendant le vol, PlayerMotor est coupe (pas de gravite, pas de bruits de pas) ;
    /// en sortant du vol le joueur retombe normalement. Tout se regle dans l'Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class FlyMode : MonoBehaviour
    {
        [Header("Activation")]
        [Tooltip("Decoche pour desactiver completement l'outil.")]
        [SerializeField] private bool enableFlyMode = true;

        [Tooltip("Binding Input System. #(V) = la touche qui affiche V (AZERTY comme QWERTY).")]
        [SerializeField] private string toggleBinding = "<Keyboard>/#(V)";

        [SerializeField] private bool startFlying = false;

        [Header("Vitesse")]
        [SerializeField, Min(0.1f)] private float speed = 12f;

        [Tooltip("Multiplicateur avec Maj.")]
        [SerializeField, Min(1f)] private float boostMultiplier = 4f;

        [Tooltip("Vitesse verticale (Espace / Ctrl).")]
        [SerializeField, Min(0.1f)] private float verticalSpeed = 8f;

        [Tooltip("Temps pour atteindre la vitesse voulue (0 = instantane).")]
        [SerializeField, Range(0f, 1f)] private float smoothing = 0.12f;

        [Header("Collisions")]
        [Tooltip("Coche : on traverse murs, arbres et terrain. Decoche : on bute sur le decor.")]
        [SerializeField] private bool passThroughWalls = true;

        [Header("References")]
        [SerializeField] private InputReader input;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private CharacterController controller;
        [SerializeField] private Transform view;

        [Header("Affichage")]
        [SerializeField] private bool showIndicator = true;

        private bool _flying;
        private Vector3 _velocity;
        private InputAction _toggleAction;
        private bool _togglePending;
        private GUIStyle _style;

        public bool IsFlying { get { return _flying; } }

        private void Awake()
        {
            if (input == null) input = GetComponent<InputReader>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (controller == null) controller = GetComponent<CharacterController>();

            if (view == null)
            {
                Camera cam = GetComponentInChildren<Camera>(true);
                view = cam != null ? cam.transform : transform;
            }
        }

        private void OnEnable()
        {
            _toggleAction = new InputAction("FlyMode", InputActionType.Button, toggleBinding);
            _toggleAction.performed += OnTogglePerformed;
            _toggleAction.Enable();
        }

        private void Start()
        {
            if (startFlying && enableFlyMode)
            {
                SetFlying(true);
            }
        }

        private void OnDisable()
        {
            if (_toggleAction != null)
            {
                _toggleAction.performed -= OnTogglePerformed;
                _toggleAction.Disable();
                _toggleAction.Dispose();
                _toggleAction = null;
            }

            if (_flying)
            {
                SetFlying(false);
            }
        }

        private void OnTogglePerformed(InputAction.CallbackContext context)
        {
            _togglePending = true;
        }

        private void Update()
        {
            bool toggle = _togglePending;
            _togglePending = false;

            bool inputActive = input == null || input.InputEnabled; // coupe en pause / carte ouverte

            if (!enableFlyMode)
            {
                if (_flying) SetFlying(false);
                return;
            }

            if (toggle && inputActive)
            {
                SetFlying(!_flying);
            }

            if (!_flying)
            {
                return;
            }

            // La teleportation de la carte reactive le CharacterController : on le recoupe.
            if (passThroughWalls && controller != null && controller.enabled)
            {
                controller.enabled = false;
            }

            Vector3 target = Vector3.zero;

            if (inputActive)
            {
                Vector2 move = input != null ? input.MoveInput : Vector2.zero;
                Vector3 forward = view.forward;
                Vector3 right = view.right;
                target = (forward * move.y + right * move.x) * speed;

                Keyboard keyboard = Keyboard.current;

                if (keyboard != null)
                {
                    if (keyboard[Key.Space].isPressed) target += Vector3.up * verticalSpeed;
                    if (keyboard[Key.LeftCtrl].isPressed || keyboard[Key.RightCtrl].isPressed) target += Vector3.down * verticalSpeed;
                    if (keyboard[Key.LeftShift].isPressed) target *= boostMultiplier;
                }
            }

            float blend = smoothing <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / smoothing);
            _velocity = Vector3.Lerp(_velocity, target, blend);

            Vector3 step = _velocity * Time.deltaTime;

            if (passThroughWalls || controller == null)
            {
                transform.position += step;
            }
            else
            {
                controller.Move(step);
            }
        }

        public void SetFlying(bool flying)
        {
            if (_flying == flying)
            {
                return;
            }

            _flying = flying;
            _velocity = Vector3.zero;

            // Le moteur gere la gravite et les pas : coupe pendant le vol.
            if (motor != null) motor.enabled = !flying;
            if (controller != null) controller.enabled = !flying || !passThroughWalls;
        }

        private void OnGUI()
        {
            if (!_flying || !showIndicator)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label);
                _style.fontSize = 15;
                _style.fontStyle = FontStyle.Bold;
                _style.normal.textColor = new Color(0.45f, 0.9f, 1f, 0.9f);
            }

            GUI.Label(new Rect(16f, 12f, 520f, 24f), "MODE VOL   V quitter · Espace monter · Ctrl descendre · Maj vite", _style);
        }
    }
}
