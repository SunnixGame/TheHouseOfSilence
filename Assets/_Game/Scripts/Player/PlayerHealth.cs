using HouseOfSilence.Core;
using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Points de vie du joueur.
    ///
    /// Version Phase 2 : degats, soin, mort logique, evenements.
    /// La Phase 13 (PlayerDeathSystem) viendra y brancher l'animation de mort,
    /// le fondu au noir et le passage en spectateur, sans modifier ce fichier :
    /// il suffira d'ecouter PlayerDiedEvent.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerHealth : MonoBehaviour
    {
        [Header("Valeurs")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;

        [Tooltip("Duree d'invulnerabilite apres avoir subi des degats, en secondes.")]
        [SerializeField, Min(0f)] private float invulnerabilityDuration = 0.6f;

        [Header("Debug")]
        [SerializeField] private bool godMode = false;

        private PlayerCharacter _owner;
        private PlayerMotor _motor;
        private PlayerLook _look;
        private float _current;
        private float _invulnerabilityTimer;

        public float Current { get { return _current; } }
        public float Max { get { return maxHealth; } }
        public float Normalized { get { return maxHealth > 0f ? Mathf.Clamp01(_current / maxHealth) : 0f; } }
        public bool IsAlive { get { return _current > 0f; } }
        public bool IsInvulnerable { get { return godMode || _invulnerabilityTimer > 0f; } }

        /// <summary>Active / desactive le mode invincible (debug, mode facile).</summary>
        public bool GodMode
        {
            get { return godMode; }
            set { godMode = value; }
        }

        private void Awake()
        {
            _owner = GetComponent<PlayerCharacter>();
            _motor = GetComponent<PlayerMotor>();
            _look = GetComponent<PlayerLook>();
            _current = maxHealth;
        }

        private void Start()
        {
            EventBus.Publish(new PlayerHealthChangedEvent(_owner, _current, maxHealth));
        }

        private void Update()
        {
            if (_invulnerabilityTimer > 0f)
            {
                _invulnerabilityTimer -= Time.deltaTime;
            }
        }

        /// <summary>Inflige des degats. Sans effet si le joueur est deja mort ou invulnerable.</summary>
        public void TakeDamage(float amount, string cause = "Inconnu")
        {
            if (!IsAlive || amount <= 0f || IsInvulnerable)
            {
                return;
            }

            _current = Mathf.Max(0f, _current - amount);
            _invulnerabilityTimer = invulnerabilityDuration;

            EventBus.Publish(new PlayerHealthChangedEvent(_owner, _current, maxHealth));

            if (_current <= 0f)
            {
                Die(cause);
            }
        }

        /// <summary>Soigne le joueur (trousse de soin, zone sure).</summary>
        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return;
            }

            _current = Mathf.Min(maxHealth, _current + amount);
            EventBus.Publish(new PlayerHealthChangedEvent(_owner, _current, maxHealth));
        }

        /// <summary>Tue le joueur immediatement (saisie de la creature, debug).</summary>
        public void Kill(string cause = "Creature")
        {
            if (!IsAlive)
            {
                return;
            }

            _current = 0f;
            EventBus.Publish(new PlayerHealthChangedEvent(_owner, _current, maxHealth));
            Die(cause);
        }

        /// <summary>Remet le joueur en vie (reapparition, nouvelle partie).</summary>
        public void Revive(float healthAmount = -1f)
        {
            _current = healthAmount > 0f ? Mathf.Min(healthAmount, maxHealth) : maxHealth;
            _invulnerabilityTimer = invulnerabilityDuration;

            if (_motor != null)
            {
                _motor.MovementLocked = false;
            }

            if (_look != null)
            {
                _look.LookLocked = false;
            }

            EventBus.Publish(new PlayerHealthChangedEvent(_owner, _current, maxHealth));
        }

        private void Die(string cause)
        {
            if (_motor != null)
            {
                _motor.StopImmediately();
                _motor.MovementLocked = true;
            }

            // La vue reste libre : le joueur doit pouvoir regarder ce qui l'a tue.
            Debug.Log("[PlayerHealth] Joueur mort. Cause : " + cause);

            EventBus.Publish(new PlayerDiedEvent(_owner, cause));
        }
    }
}
