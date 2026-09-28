using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Le demon s'assoit par terre et pleure (touche C) : animation assise (parametre
    /// "Crying" de l'Animator) et pleurs "woman-crying" en boucle, lances automatiquement
    /// avec l'animation et coupes en fondu quand il se releve (C a nouveau, ou ZQSD).
    /// Le demon ne se deplace pas tant qu'il pleure ; les pleurs continuent si on
    /// repasse au survivant (F2), pour les entendre de son point de vue.
    /// </summary>
    [DisallowMultipleComponent]
    public class DemonCry : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private bool enableCry = true;
        [Tooltip("#(C) = la touche qui affiche C (AZERTY comme QWERTY).")]
        [SerializeField] private string toggleBinding = "<Keyboard>/#(C)";
        [Tooltip("Se relever des qu'on appuie sur ZQSD.")]
        [SerializeField] private bool standUpOnMove = true;

        [Header("References")]
        [SerializeField] private DemonController controller;
        [SerializeField] private Animator animator;
        [SerializeField] private string cryingParameter = "Crying";
        [Tooltip("Source 3D qui joue les pleurs en boucle.")]
        [SerializeField] private AudioSource cryingSource;

        [Header("Son")]
        [SerializeField] private AudioClip cryingClip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [Tooltip("Delai entre le debut de l'animation et les premiers sanglots (le temps de s'asseoir).")]
        [SerializeField, Min(0f)] private float soundDelay = 0.6f;
        [SerializeField, Min(0.01f)] private float fadeIn = 0.8f;
        [SerializeField, Min(0.01f)] private float fadeOut = 0.6f;
        [Tooltip("Distance jusqu'a laquelle les pleurs restent a plein volume (m).")]
        [SerializeField, Min(0f)] private float minDistance = 6f;
        [Tooltip("Distance au-dela de laquelle on n'entend plus rien (m).")]
        [SerializeField, Min(1f)] private float maxDistance = 60f;

        [Header("Relever")]
        [Tooltip("Temps minimum assis avant de pouvoir se relever en marchant (s).")]
        [SerializeField, Min(0f)] private float minSitTime = 0.8f;

        private InputAction _toggle;
        private bool _pending;
        private bool _crying;
        private float _cryStart;
        private float _currentVolume;

        public bool IsCrying { get { return _crying; } }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<DemonController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (cryingSource != null)
            {
                cryingSource.loop = true;
                cryingSource.playOnAwake = false;
                cryingSource.volume = 0f;
            }
        }

        private void OnEnable()
        {
            _toggle = new InputAction("DemonCry", InputActionType.Button, toggleBinding);
            _toggle.performed += _ => _pending = true;
            _toggle.Enable();
        }

        private void OnDisable()
        {
            if (_toggle != null)
            {
                _toggle.Disable();
                _toggle.Dispose();
                _toggle = null;
            }

            SetCrying(false);
            if (cryingSource != null) cryingSource.Stop();
        }

        private void Update()
        {
            bool allowed = controller != null && controller.InputAllowed;

            if (_pending)
            {
                _pending = false;
                if (allowed && enableCry) SetCrying(!_crying);
            }

            if (!enableCry && _crying)
            {
                SetCrying(false);
            }

            // Marcher fait se relever.
            if (_crying && standUpOnMove && allowed && Time.time - _cryStart > minSitTime && controller.MoveInput.sqrMagnitude > 0.01f)
            {
                SetCrying(false);
            }

            UpdateSound();
        }

        /// <summary>Lance ou arrete les pleurs (animation + son).</summary>
        public void SetCrying(bool crying)
        {
            _crying = crying;
            if (crying) _cryStart = Time.time;

            if (controller != null) controller.MovementLocked = crying;
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetBool(cryingParameter, crying);
        }

        private void UpdateSound()
        {
            if (cryingSource == null)
            {
                return;
            }

            bool audible = _crying && Time.time - _cryStart >= soundDelay;
            float target = audible ? volume : 0f;
            float speed = audible ? volume / fadeIn : volume / fadeOut;
            _currentVolume = Mathf.MoveTowards(_currentVolume, target, Mathf.Max(0.01f, speed) * Time.deltaTime);

            if (audible && !cryingSource.isPlaying)
            {
                if (cryingClip != null) cryingSource.clip = cryingClip;
                cryingSource.minDistance = minDistance;
                cryingSource.maxDistance = maxDistance;
                cryingSource.time = 0f;
                cryingSource.Play();
            }

            cryingSource.volume = _currentVolume;

            if (!_crying && _currentVolume <= 0.0001f && cryingSource.isPlaying)
            {
                cryingSource.Stop();
            }
        }
    }
}
