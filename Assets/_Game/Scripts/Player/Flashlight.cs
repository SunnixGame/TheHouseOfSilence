using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Lampe torche tenue en main : un Spot qui suit le regard avec un leger
    /// retard (inertie du poignet) et un balancement discret a la marche.
    /// F allume / eteint.
    ///
    /// La touche est cherchee d'apres la lettre imprimee (AZERTY comme QWERTY).
    /// Rien ne se passe quand les inputs de gameplay sont coupes (pause, carte).
    /// </summary>
    [DisallowMultipleComponent]
    public class Flashlight : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Spot de la lampe (enfant du CameraPivot, decale vers la main droite).")]
        [SerializeField] private Light spot;

        [Tooltip("Ce que la lampe suit : la camera du joueur.")]
        [SerializeField] private Transform aim;

        [SerializeField] private InputReader input;
        [SerializeField] private PlayerMotor motor;

        [Header("Etat")]
        [SerializeField] private bool startOn = true;

        [Tooltip("Binding Input System. #(F) = la touche qui affiche F, quelle que soit la disposition (AZERTY, QWERTY).")]
        [SerializeField] private string toggleBinding = "<Keyboard>/#(F)";

        [Header("Tenue en main")]
        [Tooltip("Vitesse a laquelle le faisceau rattrape le regard (plus bas = plus de retard).")]
        [SerializeField, Range(2f, 30f)] private float followSharpness = 11f;

        [Tooltip("Amplitude du balancement a la marche, en degres.")]
        [SerializeField, Range(0f, 3f)] private float walkSway = 0.8f;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip onClip;
        [SerializeField] private AudioClip offClip;
        [SerializeField, Range(0f, 1f)] private float clickVolume = 0.7f;

        private Quaternion _rotation;
        private float _swayTime;
        private InputAction _toggleAction;

        public bool IsOn { get { return spot != null && spot.enabled; } }

        private void Awake()
        {
            if (input == null) input = GetComponent<InputReader>();
            if (motor == null) motor = GetComponent<PlayerMotor>();

            if (spot != null)
            {
                spot.enabled = startOn;
                _rotation = aim != null ? aim.rotation : spot.transform.rotation;
            }
        }

        // Une InputAction ne se declenche qu'une fois par appui (lire wasPressedThisFrame
        // dans Update peut compter deux fois le meme appui dans l'editeur).
        private void OnEnable()
        {
            _toggleAction = new InputAction("Flashlight", InputActionType.Button, toggleBinding);
            _toggleAction.performed += OnTogglePerformed;
            _toggleAction.Enable();
        }

        private void OnDisable()
        {
            if (_toggleAction == null)
            {
                return;
            }

            _toggleAction.performed -= OnTogglePerformed;
            _toggleAction.Disable();
            _toggleAction.Dispose();
            _toggleAction = null;
        }

        private void OnTogglePerformed(InputAction.CallbackContext context)
        {
            // Pause, carte ouverte... : pas de lampe.
            if (input != null && !input.InputEnabled)
            {
                return;
            }

            Toggle();
        }

        private void LateUpdate()
        {
            if (spot == null || aim == null)
            {
                return;
            }

            // Le faisceau rattrape le regard : sans a-coups, avec un peu de retard.
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            _rotation = Quaternion.Slerp(_rotation, aim.rotation, t);

            // Balancement de la main a la marche.
            float speed = motor != null ? motor.CurrentSpeed : 0f;
            _swayTime += Time.deltaTime * (3f + speed * 1.6f);
            float amount = walkSway * Mathf.Clamp01(speed / 3f);
            Quaternion sway = Quaternion.Euler(Mathf.Sin(_swayTime * 2f) * amount * 0.6f, Mathf.Sin(_swayTime) * amount, 0f);

            spot.transform.rotation = _rotation * sway;
        }

        public void Toggle()
        {
            SetOn(!IsOn);
        }

        public void SetOn(bool on)
        {
            if (spot == null || spot.enabled == on)
            {
                return;
            }

            spot.enabled = on;

            AudioClip clip = on ? onClip : offClip;

            if (audioSource != null && clip != null)
            {
                audioSource.pitch = Random.Range(0.96f, 1.04f); // deux clics jamais tout a fait identiques
                audioSource.PlayOneShot(clip, clickVolume);
            }
        }
    }
}
