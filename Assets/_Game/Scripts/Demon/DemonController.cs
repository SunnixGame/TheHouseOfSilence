using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Demon jouable en vue a la troisieme personne (outil de test).
    /// ZQSD pour se deplacer (relatif a la camera), souris pour tourner la camera,
    /// Maj pour courir, molette pour le zoom. Le modele se tourne dans le sens de la
    /// marche ; l'Animator recoit "Speed" (m/s) pour melanger Idle / Walk / Run.
    ///
    /// Les entrees sont coupees en pause et carte ouverte (meme regle que le
    /// survivant : on suit son InputReader). Tout se regle dans l'Inspector.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class DemonController : MonoBehaviour
    {
        [Header("Deplacement (m/s)")]
        [SerializeField, Min(0f)] private float walkSpeed = 2.4f;
        [SerializeField, Min(0f)] private float runSpeed = 5.5f;
        [Tooltip("Acceleration / freinage (m/s par seconde).")]
        [SerializeField, Min(0.1f)] private float acceleration = 14f;
        [Tooltip("Vitesse de rotation du corps vers la direction de marche (degres/s).")]
        [SerializeField, Min(0f)] private float turnSpeed = 720f;
        [SerializeField] private float gravity = -20f;

        [Header("Camera TPS")]
        [SerializeField] private Camera tpsCamera;
        [Tooltip("Point vise par la camera, au-dessus des pieds (m).")]
        [SerializeField] private float pivotHeight = 0.95f;
        [Tooltip("Decalage par-dessus l'epaule droite (m).")]
        [SerializeField] private float shoulderOffset = 0.35f;
        [SerializeField] private float distance = 2.4f;
        [SerializeField] private Vector2 distanceRange = new Vector2(1.1f, 6f);
        [SerializeField] private Vector2 pitchRange = new Vector2(-35f, 70f);
        [Tooltip("Degres par pixel de souris.")]
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.12f;
        [SerializeField] private bool invertY = false;
        [SerializeField, Min(0f)] private float zoomSensitivity = 0.0025f;
        [Tooltip("Rayon de la sphere qui empeche la camera de traverser le decor.")]
        [SerializeField, Min(0f)] private float cameraCollisionRadius = 0.2f;
        [SerializeField] private LayerMask cameraCollisionMask = ~0;
        [SerializeField] private float baseFieldOfView = 65f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string speedParameter = "Speed";
        [Tooltip("Multiplie la vitesse de lecture quand on depasse la course (cri).")]
        [SerializeField] private string animSpeedParameter = "AnimSpeed";
        [SerializeField, Min(0f)] private float animationDamping = 0.1f;

        [Header("Entrees")]
        [Tooltip("InputReader du survivant : sert seulement a savoir si le jeu accepte les entrees (pause, carte).")]
        [SerializeField] private InputReader inputGate;
        [SerializeField] private string runBinding = "<Keyboard>/leftShift";

        private CharacterController _controller;
        private InputAction _move;
        private InputAction _look;
        private InputAction _run;
        private InputAction _zoom;

        private bool _controlled;
        private Vector3 _velocity;
        private float _verticalVelocity;
        private float _yaw;
        private float _pitch = 12f;
        private float _shake;
        private float _shakeAmplitude;
        private float _fovKick;

        /// <summary>Multiplicateur de vitesse applique par les pouvoirs (cri).</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>Bloque le deplacement (animation assise) ; la camera reste libre.</summary>
        public bool MovementLocked { get; set; }

        /// <summary>Direction demandee au clavier cette frame (ZQSD), meme si le deplacement est bloque.</summary>
        public Vector2 MoveInput { get; private set; }

        public bool IsControlled { get { return _controlled; } }
        public Camera Camera { get { return tpsCamera; } }
        public float CurrentSpeed { get { return new Vector3(_velocity.x, 0f, _velocity.z).magnitude; } }
        public float RunSpeed { get { return runSpeed; } }

        /// <summary>Le joueur peut agir : demon controle et jeu en cours (pas de pause ni de carte).</summary>
        public bool InputAllowed { get { return _controlled && (inputGate == null || inputGate.InputEnabled); } }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _yaw = transform.eulerAngles.y;

            if (tpsCamera != null)
            {
                SetCameraActive(false);
            }
        }

        private void OnEnable()
        {
            _move = new InputAction("DemonMove", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")      // positions physiques : Z Q S D en AZERTY
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _look = new InputAction("DemonLook", InputActionType.Value, "<Mouse>/delta");
            _run = new InputAction("DemonRun", InputActionType.Button, runBinding);
            _zoom = new InputAction("DemonZoom", InputActionType.Value, "<Mouse>/scroll/y");

            _move.Enable();
            _look.Enable();
            _run.Enable();
            _zoom.Enable();
        }

        private void OnDisable()
        {
            foreach (InputAction a in new[] { _move, _look, _run, _zoom })
            {
                if (a != null)
                {
                    a.Disable();
                    a.Dispose();
                }
            }

            _move = _look = _run = _zoom = null;
        }

        /// <summary>Prend ou rend le controle (camera et ecoute audio du demon).</summary>
        public void SetControlled(bool controlled)
        {
            _controlled = controlled;

            if (controlled)
            {
                _yaw = transform.eulerAngles.y;
            }

            SetCameraActive(controlled);
        }

        private void SetCameraActive(bool active)
        {
            if (tpsCamera == null) return;

            tpsCamera.enabled = active;
            AudioListener listener = tpsCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;
        }

        /// <summary>Deplace instantanement le demon (teleportation) et l'oriente.</summary>
        public void TeleportTo(Vector3 position, Quaternion facing)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, facing.eulerAngles.y, 0f));
            _controller.enabled = true;
            _velocity = Vector3.zero;
            _verticalVelocity = 0f;
            _yaw = transform.eulerAngles.y;
            PlaceCamera(true);
        }

        /// <summary>Secousse de camera (cri, teleportation).</summary>
        public void Shake(float amplitude, float duration)
        {
            _shakeAmplitude = Mathf.Max(_shakeAmplitude, amplitude);
            _shake = Mathf.Max(_shake, duration);
        }

        /// <summary>Elargit brievement le champ de vision (degres).</summary>
        public void KickFov(float degrees)
        {
            _fovKick = Mathf.Max(_fovKick, degrees);
        }

        private void Update()
        {
            bool allowed = InputAllowed;

            Vector2 move = allowed && _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            MoveInput = move;
            if (MovementLocked) move = Vector2.zero;
            bool running = allowed && _run != null && _run.IsPressed();

            if (allowed && _look != null)
            {
                Vector2 look = _look.ReadValue<Vector2>() * mouseSensitivity;
                _yaw += look.x;
                _pitch += invertY ? look.y : -look.y;
                _pitch = Mathf.Clamp(_pitch, pitchRange.x, pitchRange.y);
            }

            if (allowed && _zoom != null)
            {
                distance = Mathf.Clamp(distance - _zoom.ReadValue<float>() * zoomSensitivity, distanceRange.x, distanceRange.y);
            }

            // Direction relative a la camera, a plat.
            Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 wish = yawRotation * new Vector3(move.x, 0f, move.y);
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float targetSpeed = (running ? runSpeed : walkSpeed) * SpeedMultiplier;
            Vector3 targetVelocity = wish * targetSpeed;
            _velocity = Vector3.MoveTowards(_velocity, targetVelocity, acceleration * SpeedMultiplier * Time.deltaTime);

            if (wish.sqrMagnitude > 0.001f)
            {
                Quaternion face = Quaternion.LookRotation(wish, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, turnSpeed * Time.deltaTime);
            }

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            _verticalVelocity += gravity * Time.deltaTime;

            if (_controller.enabled)
            {
                _controller.Move((_velocity + Vector3.up * _verticalVelocity) * Time.deltaTime);
            }

            UpdateAnimator();
        }

        private void UpdateAnimator()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            float speed = CurrentSpeed;
            animator.SetFloat(speedParameter, speed, animationDamping, Time.deltaTime);

            if (!string.IsNullOrEmpty(animSpeedParameter))
            {
                // Au-dela de la course (cri), les jambes accelerent au lieu de glisser.
                float animSpeed = runSpeed > 0f && speed > runSpeed ? speed / runSpeed : 1f;
                animator.SetFloat(animSpeedParameter, animSpeed);
            }
        }

        private void LateUpdate()
        {
            if (_controlled)
            {
                PlaceCamera(false);
            }
        }

        private void PlaceCamera(bool instant)
        {
            if (tpsCamera == null) return;

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = transform.position + Vector3.up * pivotHeight;
            Vector3 shoulder = pivot + rotation * Vector3.right * shoulderOffset;
            Vector3 desired = shoulder - rotation * Vector3.forward * distance;

            // Le decor (arbres, murs, terrain) rapproche la camera au lieu d'etre traverse.
            Vector3 dir = desired - pivot;
            float length = dir.magnitude;
            float allowed = length;

            if (length > 0.001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(pivot, cameraCollisionRadius, dir / length, length, cameraCollisionMask, QueryTriggerInteraction.Ignore);

                foreach (RaycastHit h in hits)
                {
                    if (h.collider.transform.IsChildOf(transform) || h.distance <= 0f) continue;
                    allowed = Mathf.Min(allowed, h.distance);
                }
            }

            Vector3 position = pivot + dir.normalized * Mathf.Max(0.2f, allowed);

            if (_shake > 0f)
            {
                _shake -= Time.deltaTime;
                position += Random.insideUnitSphere * _shakeAmplitude * Mathf.Clamp01(_shake * 3f);
                if (_shake <= 0f) _shakeAmplitude = 0f;
            }

            tpsCamera.transform.SetPositionAndRotation(position, rotation);

            _fovKick = instant ? 0f : Mathf.MoveTowards(_fovKick, 0f, 20f * Time.deltaTime);
            tpsCamera.fieldOfView = baseFieldOfView + _fovKick;
        }
    }
}
