using HouseOfSilence.Core;
using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Deplacement FPS base sur CharacterController.
    /// Marche, sprint (limite par l'endurance), accroupissement avec detection
    /// de plafond, gravite, saut optionnel, oscillation de camera et emission
    /// de bruits de pas.
    ///
    /// Ce script ne lit jamais l'Input System directement : il passe par InputReader.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InputReader input;
        [SerializeField] private PlayerStamina stamina;

        [Tooltip("Transform parent de la camera : sa hauteur suit l'accroupissement.")]
        [SerializeField] private Transform cameraPivot;

        [Header("Vitesses (m/s)")]
        [SerializeField, Min(0f)] private float walkSpeed = 2.6f;
        [SerializeField, Min(0f)] private float runSpeed = 4.8f;
        [SerializeField, Min(0f)] private float crouchSpeed = 1.3f;

        [Tooltip("Vitesse de marche arriere / laterale, en pourcentage de la vitesse normale.")]
        [SerializeField, Range(0.3f, 1f)] private float backwardSpeedMultiplier = 0.75f;

        [Header("Acceleration")]
        [SerializeField, Min(0.1f)] private float acceleration = 12f;
        [SerializeField, Min(0.1f)] private float deceleration = 16f;

        [Tooltip("Controle du joueur en l'air (0 = aucun, 1 = total).")]
        [SerializeField, Range(0f, 1f)] private float airControl = 0.35f;

        [Header("Gravite et saut")]
        [SerializeField] private float gravity = -19f;
        [SerializeField] private bool jumpEnabled = true;
        [SerializeField, Min(0f)] private float jumpHeight = 0.9f;
        [SerializeField, Min(0f)] private float jumpStaminaCost = 8f;

        [Tooltip("Delai de tolerance pour sauter juste apres avoir quitte le sol.")]
        [SerializeField, Min(0f)] private float coyoteTime = 0.12f;

        [Header("Accroupissement")]
        [SerializeField, Min(0.5f)] private float standHeight = 1.8f;
        [SerializeField, Min(0.4f)] private float crouchHeight = 1.15f;
        [SerializeField, Min(0.1f)] private float cameraStandHeight = 1.65f;
        [SerializeField, Min(0.1f)] private float cameraCrouchHeight = 1.0f;
        [SerializeField, Min(1f)] private float crouchTransitionSpeed = 9f;

        [Tooltip("Couches testees pour savoir si le joueur peut se relever.")]
        [SerializeField] private LayerMask ceilingMask = ~0;

        [Header("Oscillation de camera")]
        [SerializeField] private bool headBobEnabled = true;
        [SerializeField, Range(0f, 0.15f)] private float bobAmplitude = 0.035f;
        [SerializeField, Range(0f, 20f)] private float bobFrequency = 9f;

        [Header("Bruits de pas")]
        [Tooltip("Distance parcourue entre deux pas, en marche.")]
        [SerializeField, Min(0.2f)] private float walkStepDistance = 2.1f;
        [SerializeField, Min(0.2f)] private float runStepDistance = 1.5f;
        [SerializeField, Min(0.2f)] private float crouchStepDistance = 2.8f;

        // ------------------------------------------------------------------

        private CharacterController _controller;
        private PlayerCharacter _owner;

        private readonly RaycastHit[] _ceilingHits = new RaycastHit[8];

        private Vector3 _horizontalVelocity;
        private Vector3 _impulse;
        private float _verticalVelocity;
        private float _coyoteTimer;
        private float _currentHeight;
        private float _cameraHeight;
        private float _bobTimer;
        private float _stepDistanceAccumulator;
        private bool _isCrouching;
        private bool _isSprinting;
        private bool _isGrounded;
        private PlayerMovementState _movementState = PlayerMovementState.Idle;

        // ------------------------------------------------------------------
        // Lecture publique
        // ------------------------------------------------------------------

        public PlayerMovementState MovementState { get { return _movementState; } }
        public bool IsGrounded { get { return _isGrounded; } }
        public bool IsCrouching { get { return _isCrouching; } }
        public bool IsSprinting { get { return _isSprinting; } }

        /// <summary>Vitesse horizontale reelle, en m/s.</summary>
        public float CurrentSpeed { get { return _horizontalVelocity.magnitude; } }

        /// <summary>Vitesses reglees dans l'Inspector (animation du corps).</summary>
        public float WalkSpeed { get { return walkSpeed; } }
        public float RunSpeed { get { return runSpeed; } }

        /// <summary>Bloque tout deplacement (mort, cachette, cinematique).</summary>
        public bool MovementLocked { get; set; }

        /// <summary>
        /// Bruit genere par le deplacement, de 0 (immobile ou accroupi) a 1 (course).
        /// L'ouie de la creature (Phase 17) s'en servira directement.
        /// </summary>
        public float NoiseLevel
        {
            get
            {
                if (!_isGrounded || CurrentSpeed < 0.1f)
                {
                    return 0f;
                }

                if (_isCrouching)
                {
                    return 0.15f;
                }

                return _isSprinting ? 1f : 0.5f;
            }
        }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _owner = GetComponent<PlayerCharacter>();

            if (input == null)
            {
                input = GetComponent<InputReader>();
            }

            if (stamina == null)
            {
                stamina = GetComponent<PlayerStamina>();
            }

            if (crouchHeight > standHeight)
            {
                crouchHeight = standHeight;
            }

            _currentHeight = standHeight;
            _cameraHeight = cameraStandHeight;

            ApplyHeight(_currentHeight);
            ApplyCameraHeight(_cameraHeight, 0f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (dt <= 0f)
            {
                return;
            }

            bool canMove = !MovementLocked && input != null && input.InputEnabled;

            Vector2 moveInput = canMove ? input.MoveInput : Vector2.zero;

            UpdateCrouchState(canMove);
            UpdateSprintState(canMove, moveInput);

            Vector3 targetVelocity = ComputeTargetVelocity(moveInput);

            float rate = targetVelocity.sqrMagnitude > 0.01f ? acceleration : deceleration;

            if (!_isGrounded)
            {
                rate *= Mathf.Max(0.05f, airControl);
            }

            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, rate * dt);

            UpdateVertical(canMove, dt);

            // Impulsions externes (poussee de la creature, explosion...) : amorties.
            if (_impulse.sqrMagnitude > 0.0001f)
            {
                _impulse = Vector3.MoveTowards(_impulse, Vector3.zero, 12f * dt);
            }

            Vector3 motion = (_horizontalVelocity + _impulse) * dt;
            motion.y += _verticalVelocity * dt;

            _controller.Move(motion);

            _isGrounded = _controller.isGrounded;

            if (_isGrounded)
            {
                _coyoteTimer = coyoteTime;
            }
            else if (_coyoteTimer > 0f)
            {
                _coyoteTimer -= dt;
            }

            UpdateHeights(dt);
            UpdateHeadBob(dt);
            UpdateFootsteps(dt);
            UpdateMovementState();
        }

        // ------------------------------------------------------------------
        // Deplacement
        // ------------------------------------------------------------------

        private Vector3 ComputeTargetVelocity(Vector2 moveInput)
        {
            if (moveInput.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            Vector3 direction = transform.right * moveInput.x + transform.forward * moveInput.y;

            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            float speed = crouchSpeed;

            if (!_isCrouching)
            {
                speed = _isSprinting ? runSpeed : walkSpeed;
            }

            // Reculer ou se deplacer lateralement est plus lent.
            if (moveInput.y < -0.1f)
            {
                speed *= backwardSpeedMultiplier;
            }

            return direction * speed;
        }

        private void UpdateSprintState(bool canMove, Vector2 moveInput)
        {
            bool wantsSprint = canMove
                && input != null
                && input.SprintHeld
                && !_isCrouching
                && moveInput.y > 0.1f;

            if (wantsSprint && stamina != null && !stamina.CanSprint)
            {
                wantsSprint = false;
            }

            _isSprinting = wantsSprint && _isGrounded;

            if (stamina != null)
            {
                stamina.SetSprinting(_isSprinting && CurrentSpeed > 0.5f);
            }
        }

        private void UpdateCrouchState(bool canMove)
        {
            bool wantsCrouch = canMove && input != null && input.CrouchRequested;

            if (wantsCrouch)
            {
                _isCrouching = true;
                return;
            }

            if (!_isCrouching)
            {
                return;
            }

            // On ne se releve que si la place existe reellement.
            if (!HasObstacleAbove(standHeight))
            {
                _isCrouching = false;
            }
        }

        private void UpdateVertical(bool canMove, float dt)
        {
            if (_isGrounded && _verticalVelocity < 0f)
            {
                // Colle le joueur au sol (evite de "flotter" dans les escaliers).
                _verticalVelocity = -2f;
            }

            bool canJump = canMove
                && jumpEnabled
                && !_isCrouching
                && _coyoteTimer > 0f
                && input != null
                && input.JumpPressed;

            if (canJump)
            {
                if (stamina != null && jumpStaminaCost > 0f)
                {
                    stamina.Consume(jumpStaminaCost);
                }

                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                _coyoteTimer = 0f;
                _isGrounded = false;

                EmitFootstep(0.8f);
            }

            _verticalVelocity += gravity * dt;

            // Vitesse terminale : evite les valeurs absurdes lors d'une longue chute.
            if (_verticalVelocity < -55f)
            {
                _verticalVelocity = -55f;
            }
        }

        // ------------------------------------------------------------------
        // Hauteurs
        // ------------------------------------------------------------------

        private void UpdateHeights(float dt)
        {
            float targetHeight = _isCrouching ? crouchHeight : standHeight;
            float targetCameraHeight = _isCrouching ? cameraCrouchHeight : cameraStandHeight;

            if (!Mathf.Approximately(_currentHeight, targetHeight))
            {
                _currentHeight = Mathf.MoveTowards(_currentHeight, targetHeight, crouchTransitionSpeed * dt);
                ApplyHeight(_currentHeight);
            }

            if (!Mathf.Approximately(_cameraHeight, targetCameraHeight))
            {
                _cameraHeight = Mathf.MoveTowards(_cameraHeight, targetCameraHeight, crouchTransitionSpeed * dt);
            }
        }

        private void ApplyHeight(float height)
        {
            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        private void ApplyCameraHeight(float height, float bobOffset)
        {
            if (cameraPivot == null)
            {
                return;
            }

            Vector3 local = cameraPivot.localPosition;
            local.x = 0f;
            local.y = height + bobOffset;
            local.z = 0f;
            cameraPivot.localPosition = local;
        }

        /// <summary>
        /// Teste s'il y a un obstacle au dessus, en ignorant les colliders du joueur.
        /// </summary>
        private bool HasObstacleAbove(float targetHeight)
        {
            float radius = Mathf.Max(0.05f, _controller.radius * 0.95f);
            float distance = Mathf.Max(0.02f, targetHeight - _currentHeight);

            Vector3 origin = transform.position + Vector3.up * Mathf.Max(radius, _currentHeight - radius);

            int count = Physics.SphereCastNonAlloc(
                origin,
                radius,
                Vector3.up,
                _ceilingHits,
                distance,
                ceilingMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = _ceilingHits[i].collider;

                if (hitCollider == null)
                {
                    continue;
                }

                if (hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Camera et bruits de pas
        // ------------------------------------------------------------------

        private void UpdateHeadBob(float dt)
        {
            float bobOffset = 0f;

            if (headBobEnabled && _isGrounded && CurrentSpeed > 0.3f)
            {
                float speedFactor = Mathf.Clamp01(CurrentSpeed / Mathf.Max(0.1f, runSpeed));
                _bobTimer += dt * bobFrequency * (0.6f + speedFactor);
                bobOffset = Mathf.Sin(_bobTimer) * bobAmplitude * speedFactor;
            }
            else
            {
                _bobTimer = 0f;
            }

            ApplyCameraHeight(_cameraHeight, bobOffset);
        }

        private void UpdateFootsteps(float dt)
        {
            if (!_isGrounded)
            {
                return;
            }

            float speed = CurrentSpeed;

            if (speed < 0.25f)
            {
                _stepDistanceAccumulator = 0f;
                return;
            }

            _stepDistanceAccumulator += speed * dt;

            float threshold = walkStepDistance;

            if (_isCrouching)
            {
                threshold = crouchStepDistance;
            }
            else if (_isSprinting)
            {
                threshold = runStepDistance;
            }

            if (_stepDistanceAccumulator < threshold)
            {
                return;
            }

            _stepDistanceAccumulator = 0f;
            EmitFootstep(NoiseLevel);
        }

        private void EmitFootstep(float intensity)
        {
            if (intensity <= 0.001f)
            {
                return;
            }

            intensity = Mathf.Clamp01(intensity);

            // Deux publications volontairement distinctes :
            // - PlayerFootstepEvent sert a l'audio et aux animations (il porte le joueur) ;
            // - NoiseEmittedEvent sert a la perception de la creature (il porte un rayon).
            EventBus.Publish(new PlayerFootstepEvent(_owner, transform.position, intensity));

            Noise.Emit(
                transform.position,
                2f + intensity * 14f,
                _isSprinting ? NoiseType.Run : NoiseType.Footstep,
                gameObject);
        }

        private void UpdateMovementState()
        {
            PlayerMovementState next;

            if (!_isGrounded)
            {
                next = PlayerMovementState.Airborne;
            }
            else if (_isCrouching)
            {
                next = CurrentSpeed > 0.25f ? PlayerMovementState.CrouchWalking : PlayerMovementState.Crouching;
            }
            else if (CurrentSpeed <= 0.25f)
            {
                next = PlayerMovementState.Idle;
            }
            else
            {
                next = _isSprinting ? PlayerMovementState.Running : PlayerMovementState.Walking;
            }

            if (next == _movementState)
            {
                return;
            }

            PlayerMovementState previous = _movementState;
            _movementState = next;

            EventBus.Publish(new PlayerMovementStateChangedEvent(_owner, previous, next));
        }

        // ------------------------------------------------------------------
        // API externe
        // ------------------------------------------------------------------

        /// <summary>Deplace le joueur sans le faire traverser les murs (spawn, teleportation debug).</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            _controller.enabled = false;

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _impulse = Vector3.zero;

            _controller.enabled = true;

            PlayerLook look = GetComponent<PlayerLook>();

            if (look != null)
            {
                look.SetRotation(yaw, 0f);
            }
        }

        /// <summary>Applique une poussee (creature qui bouscule, souffle, chute).</summary>
        public void AddImpulse(Vector3 impulse)
        {
            _impulse += impulse;
        }

        /// <summary>Arrete net le joueur (mort, saisie par la creature).</summary>
        public void StopImmediately()
        {
            _horizontalVelocity = Vector3.zero;
            _impulse = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
