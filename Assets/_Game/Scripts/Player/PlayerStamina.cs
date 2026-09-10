using HouseOfSilence.Core;
using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Endurance du joueur : le sprint n'est jamais infini.
    ///
    /// Regle : quand la barre tombe a zero, le joueur est "epuise" et ne peut
    /// plus courir tant qu'il n'a pas recupere un minimum (recoveryThreshold).
    /// C'est ce qui rend les poursuites tendues sans etre injustes.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerStamina : MonoBehaviour
    {
        [Header("Valeurs")]
        [SerializeField, Min(1f)] private float staminaMax = 100f;

        [Tooltip("Points d'endurance consommes par seconde de sprint.")]
        [SerializeField, Min(0f)] private float staminaDrain = 18f;

        [Tooltip("Points d'endurance regeneres par seconde.")]
        [SerializeField, Min(0f)] private float staminaRecovery = 14f;

        [Tooltip("Delai avant que la regeneration ne commence, apres avoir arrete de courir.")]
        [SerializeField, Min(0f)] private float recoveryDelay = 1.1f;

        [Tooltip("Endurance minimale a atteindre pour pouvoir recourir apres un epuisement.")]
        [SerializeField, Min(0f)] private float recoveryThreshold = 25f;

        [Header("Debug")]
        [SerializeField] private bool infiniteStamina = false;

        private PlayerCharacter _owner;
        private float _current;
        private float _recoveryTimer;
        private bool _isExhausted;
        private bool _isSprinting;
        private float _lastPublished = -1f;
        private bool _lastExhaustedPublished;

        /// <summary>Endurance courante.</summary>
        public float Current { get { return _current; } }

        /// <summary>Endurance maximale.</summary>
        public float Max { get { return staminaMax; } }

        /// <summary>Ratio 0 -> 1 pour le HUD.</summary>
        public float Normalized { get { return staminaMax > 0f ? Mathf.Clamp01(_current / staminaMax) : 0f; } }

        /// <summary>Vrai tant que le joueur doit recuperer avant de pouvoir recourir.</summary>
        public bool IsExhausted { get { return _isExhausted; } }

        /// <summary>Vrai si le joueur peut sprinter maintenant.</summary>
        public bool CanSprint
        {
            get { return infiniteStamina || (!_isExhausted && _current > 0.1f); }
        }

        private void Awake()
        {
            _owner = GetComponentInParent<PlayerCharacter>();
            _current = staminaMax;
        }

        private void Start()
        {
            PublishIfChanged(true);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (infiniteStamina)
            {
                _current = staminaMax;
                _isExhausted = false;
                PublishIfChanged(false);
                return;
            }

            if (_isSprinting)
            {
                _current -= staminaDrain * dt;
                _recoveryTimer = recoveryDelay;

                if (_current <= 0f)
                {
                    _current = 0f;
                    _isExhausted = true;
                }
            }
            else
            {
                if (_recoveryTimer > 0f)
                {
                    _recoveryTimer -= dt;
                }
                else if (_current < staminaMax)
                {
                    _current = Mathf.Min(staminaMax, _current + staminaRecovery * dt);
                }
            }

            if (_isExhausted && _current >= recoveryThreshold)
            {
                _isExhausted = false;
            }

            PublishIfChanged(false);
        }

        /// <summary>Appele par le PlayerMotor a chaque frame : le joueur court-il reellement ?</summary>
        public void SetSprinting(bool sprinting)
        {
            _isSprinting = sprinting && CanSprint;
        }

        /// <summary>Consommation ponctuelle (saut, effort, evenement de peur).</summary>
        public void Consume(float amount)
        {
            if (infiniteStamina || amount <= 0f)
            {
                return;
            }

            _current = Mathf.Max(0f, _current - amount);
            _recoveryTimer = recoveryDelay;

            if (_current <= 0f)
            {
                _isExhausted = true;
            }

            PublishIfChanged(false);
        }

        /// <summary>Restaure de l'endurance (zone sure, objet consommable).</summary>
        public void Restore(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            _current = Mathf.Min(staminaMax, _current + amount);
            PublishIfChanged(false);
        }

        private void PublishIfChanged(bool force)
        {
            // On evite de spammer l'EventBus a chaque frame : seuil de 0.5 point,
            // mais un changement d'etat d'epuisement est toujours diffuse.
            bool exhaustionChanged = _isExhausted != _lastExhaustedPublished;

            if (!force && !exhaustionChanged && Mathf.Abs(_current - _lastPublished) < 0.5f)
            {
                return;
            }

            _lastPublished = _current;
            _lastExhaustedPublished = _isExhausted;
            EventBus.Publish(new PlayerStaminaChangedEvent(_owner, _current, staminaMax, _isExhausted));
        }
    }
}
