using HouseOfSilence.Core;
using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Facade d'un joueur : identite + acces rapide a ses sous-systemes.
    ///
    /// C'est le seul type que les autres systemes (IA, objectifs, UI, peur)
    /// manipulent. Ils n'ont jamais besoin de connaitre PlayerMotor ou InputReader.
    ///
    /// En multijoueur (Phase 18), NetworkPlayer viendra se poser a cote et
    /// renseignera PlayerId / IsLocalPlayer : rien d'autre ne changera.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCharacter : MonoBehaviour
    {
        [Header("Identite")]
        [SerializeField] private int playerId = 0;
        [SerializeField] private string displayName = "Survivant";

        [Tooltip("Vrai pour le joueur controle sur cette machine. En solo, toujours vrai.")]
        [SerializeField] private bool isLocalPlayer = true;

        [Header("References")]
        [Tooltip("Camera du joueur. Utilisee par l'interaction, la peur et les evenements.")]
        [SerializeField] private Camera playerCamera;

        [SerializeField] private Transform cameraPivot;

        private PlayerMotor _motor;
        private PlayerLook _look;
        private PlayerHealth _health;
        private PlayerStamina _stamina;
        private InputReader _input;
        private CharacterController _controller;

        // ------------------------------------------------------------------
        // Acces publics
        // ------------------------------------------------------------------

        public int PlayerId { get { return playerId; } }
        public string DisplayName { get { return displayName; } }
        public bool IsLocalPlayer { get { return isLocalPlayer; } }

        public PlayerMotor Motor { get { return _motor; } }
        public PlayerLook Look { get { return _look; } }
        public PlayerHealth Health { get { return _health; } }
        public PlayerStamina Stamina { get { return _stamina; } }
        public InputReader Input { get { return _input; } }
        public Camera Camera { get { return playerCamera; } }
        public Transform CameraPivot { get { return cameraPivot; } }

        /// <summary>Position des yeux : origine des raycasts d'interaction et de la vision de la creature.</summary>
        public Vector3 EyePosition
        {
            get
            {
                if (playerCamera != null)
                {
                    return playerCamera.transform.position;
                }

                if (cameraPivot != null)
                {
                    return cameraPivot.position;
                }

                return transform.position + Vector3.up * 1.6f;
            }
        }

        /// <summary>Position au sol du joueur.</summary>
        public Vector3 FeetPosition { get { return transform.position; } }

        public bool IsAlive { get { return _health == null || _health.IsAlive; } }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _look = GetComponent<PlayerLook>();
            _health = GetComponent<PlayerHealth>();
            _stamina = GetComponent<PlayerStamina>();
            _input = GetComponent<InputReader>();
            _controller = GetComponent<CharacterController>();

            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>(true);
            }

            if (cameraPivot == null && playerCamera != null)
            {
                cameraPivot = playerCamera.transform.parent != null ? playerCamera.transform.parent : playerCamera.transform;
            }

            if (playerCamera == null)
            {
                Debug.LogError("[PlayerCharacter] Aucune camera trouvee sous le joueur.", this);
            }
        }

        private void OnEnable()
        {
            PlayerManager manager = PlayerManager.Instance;

            if (manager != null)
            {
                manager.Register(this);
            }

            EventBus.Publish(new PlayerSpawnedEvent(this));
        }

        private void OnDisable()
        {
            if (PlayerManager.HasInstance)
            {
                PlayerManager manager = PlayerManager.Instance;

                if (manager != null)
                {
                    manager.Unregister(this);
                }
            }

            EventBus.Publish(new PlayerDespawnedEvent(this));
        }

        /// <summary>Renseigne l'identite du joueur (appele par le spawner ou le code reseau).</summary>
        public void SetIdentity(int id, string playerName, bool local)
        {
            playerId = id;
            displayName = string.IsNullOrEmpty(playerName) ? ("Survivant " + id) : playerName;
            isLocalPlayer = local;
        }

        /// <summary>Deplace le joueur vers un point du monde en toute securite.</summary>
        public void TeleportTo(Vector3 position, float yaw)
        {
            if (_motor != null)
            {
                _motor.Teleport(position, yaw);
                return;
            }

            if (_controller != null)
            {
                _controller.enabled = false;
                transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                _controller.enabled = true;
                return;
            }

            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }
    }
}

