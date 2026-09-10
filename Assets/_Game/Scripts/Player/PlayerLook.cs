using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Rotation de la vue FPS.
    /// - Le lacet (gauche/droite) tourne la racine du joueur.
    /// - Le tangage (haut/bas) tourne uniquement le pivot de camera.
    ///
    /// Le delta souris de l'Input System est deja exprime par frame :
    /// il ne faut donc PAS le multiplier par Time.deltaTime (sinon la sensibilite
    /// varie avec le framerate). Le stick de manette, lui, est une valeur continue
    /// et doit l'etre : les deux cas sont geres separement.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerLook : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Transform parent de la camera (enfant du joueur).")]
        [SerializeField] private Transform cameraPivot;

        [SerializeField] private InputReader input;

        [Header("Sensibilite")]
        [SerializeField, Range(0.01f, 2f)] private float mouseSensitivity = 0.18f;

        [Tooltip("Multiplicateur vertical (1 = identique a l'horizontal).")]
        [SerializeField, Range(0.1f, 2f)] private float verticalSensitivityMultiplier = 1f;

        [SerializeField] private bool invertY = false;

        [Header("Limites")]
        [SerializeField, Range(-89f, 0f)] private float minPitch = -85f;
        [SerializeField, Range(0f, 89f)] private float maxPitch = 85f;

        [Header("Lissage")]
        [Tooltip("0 = aucun lissage (reponse directe). Au dela de 0.2 la visee devient molle.")]
        [SerializeField, Range(0f, 0.3f)] private float smoothing = 0.02f;

        [Header("Effets")]
        [Tooltip("Amplitude maximale du tremblement de peur, en degres (Phase 7).")]
        [SerializeField, Range(0f, 5f)] private float maxFearShake = 1.5f;

        private float _yaw;
        private float _pitch;
        private Vector2 _smoothedDelta;
        private Vector2 _smoothVelocity;
        private float _fearShakeAmount;
        private float _shakeSeed;

        /// <summary>Angle vertical courant, en degres.</summary>
        public float Pitch { get { return _pitch; } }

        /// <summary>Transform du pivot de camera (lecture seule).</summary>
        public Transform CameraPivot { get { return cameraPivot; } }

        /// <summary>Bloque la rotation (mort, cachette, cinematique).</summary>
        public bool LookLocked { get; set; }

        private void Awake()
        {
            if (input == null)
            {
                input = GetComponentInParent<InputReader>();
            }

            if (cameraPivot == null)
            {
                Debug.LogError("[PlayerLook] 'Camera Pivot' n'est pas assigne.", this);
            }

            _yaw = transform.eulerAngles.y;
            _shakeSeed = Random.value * 100f;
        }

        private void Start()
        {
            ApplyRotation();
        }

        private void LateUpdate()
        {
            if (LookLocked || input == null || !input.InputEnabled)
            {
                return;
            }

            Vector2 raw = input.LookInput;

            if (smoothing > 0.0001f)
            {
                _smoothedDelta = Vector2.SmoothDamp(_smoothedDelta, raw, ref _smoothVelocity, smoothing, Mathf.Infinity, Time.unscaledDeltaTime);
            }
            else
            {
                _smoothedDelta = raw;
            }

            float deltaX = _smoothedDelta.x * mouseSensitivity;
            float deltaY = _smoothedDelta.y * mouseSensitivity * verticalSensitivityMultiplier;

            if (invertY)
            {
                deltaY = -deltaY;
            }

            _yaw += deltaX;
            _pitch = Mathf.Clamp(_pitch - deltaY, minPitch, maxPitch);

            ApplyRotation();
        }

        private void ApplyRotation()
        {
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (cameraPivot == null)
            {
                return;
            }

            float shakePitch = 0f;
            float shakeYaw = 0f;

            if (_fearShakeAmount > 0.001f && maxFearShake > 0f)
            {
                float t = Time.time * 6f;
                float amplitude = _fearShakeAmount * maxFearShake;

                shakePitch = (Mathf.PerlinNoise(_shakeSeed, t) - 0.5f) * 2f * amplitude;
                shakeYaw = (Mathf.PerlinNoise(t, _shakeSeed) - 0.5f) * 2f * amplitude;
            }

            cameraPivot.localRotation = Quaternion.Euler(_pitch + shakePitch, shakeYaw, 0f);
        }

        /// <summary>
        /// Regle l'intensite du tremblement de camera, de 0 a 1.
        /// Sera pilote par le FearSystem en Phase 7.
        /// </summary>
        public void SetFearShake(float normalizedAmount)
        {
            _fearShakeAmount = Mathf.Clamp01(normalizedAmount);
        }

        /// <summary>Oriente instantanement la vue (spawn, teleportation, scenario).</summary>
        public void SetRotation(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            _smoothedDelta = Vector2.zero;
            _smoothVelocity = Vector2.zero;
            ApplyRotation();
        }

        /// <summary>Force la vue a regarder un point du monde (evenements horrifiques, jumpscare).</summary>
        public void LookAtPoint(Vector3 worldPosition)
        {
            Vector3 direction = worldPosition - (cameraPivot != null ? cameraPivot.position : transform.position);

            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            Vector3 euler = rotation.eulerAngles;

            float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            SetRotation(euler.y, pitch);
        }
    }
}
