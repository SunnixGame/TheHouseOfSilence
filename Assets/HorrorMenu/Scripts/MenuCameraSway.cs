using UnityEngine;

namespace HouseOfSilence.HorrorMenu
{
    /// <summary>
    /// Balancement lent de la camera du menu (respiration / camera a l'epaule),
    /// plus un leger decalage qui suit la souris.
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuCameraSway : MonoBehaviour
    {
        [Tooltip("Amplitude de rotation en degres.")]
        [SerializeField] private Vector2 rotationAmplitude = new Vector2(0.6f, 0.9f);

        [Tooltip("Amplitude de deplacement en metres.")]
        [SerializeField] private float positionAmplitude = 0.04f;

        [SerializeField, Min(0f)] private float speed = 0.15f;

        [Tooltip("Rotation maximale (degres) liee a la position de la souris.")]
        [SerializeField] private float mouseInfluence = 1.2f;

        private Vector3 _basePosition;
        private Quaternion _baseRotation;
        private Vector2 _mouseSmoothed;

        private void Awake()
        {
            _basePosition = transform.localPosition;
            _baseRotation = transform.localRotation;
        }

        private void LateUpdate()
        {
            float t = Time.time * speed;
            float pitch = (Mathf.PerlinNoise(t, 0.3f) * 2f - 1f) * rotationAmplitude.x;
            float yaw = (Mathf.PerlinNoise(0.7f, t) * 2f - 1f) * rotationAmplitude.y;

            Vector2 mouse = ReadMouseViewport();
            _mouseSmoothed = Vector2.Lerp(_mouseSmoothed, mouse, Time.deltaTime * 2f);
            pitch -= _mouseSmoothed.y * mouseInfluence;
            yaw += _mouseSmoothed.x * mouseInfluence;

            transform.localRotation = _baseRotation * Quaternion.Euler(pitch, yaw, 0f);
            transform.localPosition = _basePosition + new Vector3(
                Mathf.PerlinNoise(t * 0.8f, 5f) * 2f - 1f,
                Mathf.PerlinNoise(9f, t * 0.8f) * 2f - 1f,
                0f) * positionAmplitude;
        }

        /// <summary>Position de la souris ramenee a [-1, 1] (0 au centre de l'ecran).</summary>
        private static Vector2 ReadMouseViewport()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) { return Vector2.zero; }
            Vector2 pos = mouse.position.ReadValue();
#else
            Vector2 pos = Input.mousePosition;
#endif
            if (Screen.width <= 0 || Screen.height <= 0) { return Vector2.zero; }
            return new Vector2(
                Mathf.Clamp(pos.x / Screen.width * 2f - 1f, -1f, 1f),
                Mathf.Clamp(pos.y / Screen.height * 2f - 1f, -1f, 1f));
        }
    }
}
