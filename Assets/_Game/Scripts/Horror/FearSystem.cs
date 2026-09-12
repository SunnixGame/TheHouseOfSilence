using HouseOfSilence.Core;
using HouseOfSilence.Core.Debugging;
using HouseOfSilence.Lights;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Horror
{
    /// <summary>
    /// Peur d'un joueur, de 0 (calme) a 100 (panique).
    ///
    /// Sources qui la font monter (points par seconde) : obscurite, isolement
    /// (coop), proximite de la creature, poursuite, bruits soudains, et tout
    /// appel a AddFear() par les evenements horrifiques (Phase 9).
    ///
    /// Ce qui la fait redescendre : lumiere, coequipiers proches, zone sure,
    /// et simplement le temps quand aucune menace n'est active.
    ///
    /// Ce composant ne produit AUCUN effet visuel ou sonore : il publie
    /// FearChangedEvent / FearLevelChangedEvent, et FearEffects,
    /// FearAudioFeedback, le HUD ou les hallucinations s'y abonnent.
    ///
    /// A poser sur le joueur.
    /// </summary>
    [DisallowMultipleComponent]
    public class FearSystem : MonoBehaviour
    {
        private const float MaxFear = 100f;

        [Header("Etat initial")]
        [SerializeField, Range(0f, 100f)] private float startFear = 0f;

        [Header("Montee (points / seconde)")]
        [Tooltip("Dans le noir complet. Pondere par l'obscurite reelle.")]
        [SerializeField, Min(0f)] private float darknessRate = 2.5f;

        [Tooltip("En coop uniquement : aucun coequipier vivant a portee.")]
        [SerializeField, Min(0f)] private float isolationRate = 1.5f;

        [SerializeField, Min(1f)] private float isolationRadius = 8f;

        [Tooltip("Pendant une poursuite (Phase 12).")]
        [SerializeField, Min(0f)] private float chaseRate = 12f;

        [Header("Apaisement (points / seconde)")]
        [Tooltip("En pleine lumiere, sans menace.")]
        [SerializeField, Min(0f)] private float lightRecovery = 3f;

        [Tooltip("Dans le noir, sans menace. Tres faible : l'obscurite n'apaise jamais vraiment.")]
        [SerializeField, Min(0f)] private float darkRecovery = 0.4f;

        [Tooltip("Par coequipier vivant a portee (maximum 2 comptes).")]
        [SerializeField, Min(0f)] private float teammateRecovery = 1f;

        [SerializeField, Min(0f)] private float safeZoneRecovery = 8f;

        [Header("Bruits soudains")]
        [Tooltip("Un bruit dont le rayon depasse ce seuil est considere comme violent (porte qui claque : 22 m).")]
        [SerializeField, Min(0f)] private float loudNoiseRadiusThreshold = 15f;

        [Tooltip("Peur ajoutee par un bruit violent a bout portant. Decroit avec la distance.")]
        [SerializeField, Min(0f)] private float fearPerLoudNoise = 8f;

        [SerializeField, Min(0f)] private float loudNoiseCooldown = 1.5f;

        [Header("Lampes")]
        [Tooltip("Distance maximale a laquelle une lampe qui lache effraie le joueur.")]
        [SerializeField, Min(0f)] private float lightEventRadius = 7f;

        [SerializeField, Min(0f)] private float fearPerLightFlicker = 3f;
        [SerializeField, Min(0f)] private float fearPerLightBreak = 10f;

        [Header("Difficulte (Phase 45)")]
        [SerializeField, Min(0f)] private float gainMultiplier = 1f;
        [SerializeField, Min(0f)] private float recoveryMultiplier = 1f;

        [Header("Debug")]
        [SerializeField] private bool registerDebugCommand = true;
        [SerializeField] private Key debugToggleKey = Key.F4;

        // ------------------------------------------------------------------

        private PlayerCharacter _player;
        private LightLevelSensor _lightSensor;

        private float _fear;
        private FearLevel _level = FearLevel.Calm;
        private float _lastPublished = -1f;
        private float _noiseCooldownTimer;
        private float _monsterDistance = float.MaxValue;
        private bool _hasMonsterDistance;
        private bool _isChased;
        private int _safeZoneCount;
        private bool _debugLocked;

        // Derniers taux calcules, pour l'overlay de debug.
        private float _lastGain;
        private float _lastRecovery;

        /// <summary>Peur courante, 0 a 100.</summary>
        public float Current { get { return _fear; } }

        /// <summary>Peur normalisee, 0 a 1.</summary>
        public float Normalized { get { return _fear / MaxFear; } }

        /// <summary>Palier courant.</summary>
        public FearLevel Level { get { return _level; } }

        /// <summary>Vrai si une menace active (creature proche ou poursuite) empeche l'apaisement.</summary>
        public bool IsThreatened { get { return _isChased || (_hasMonsterDistance && _monsterDistance < 15f); } }

        public bool IsInSafeZone { get { return _safeZoneCount > 0; } }

        /// <summary>Lumiere recue par le joueur, 0 a 1 (1 si aucun capteur).</summary>
        public float LightLevel { get { return _lightSensor != null ? _lightSensor.LightLevel : 1f; } }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        private void Awake()
        {
            _player = GetComponent<PlayerCharacter>();
            _lightSensor = GetComponent<LightLevelSensor>();
            _fear = Mathf.Clamp(startFear, 0f, MaxFear);
            _level = LevelFor(_fear);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<NoiseEmittedEvent>(OnNoiseEmitted);
            EventBus.Subscribe<LightStateChangedEvent>(OnLightStateChanged);

            if (registerDebugCommand)
            {
                DebugManager.Register(debugToggleKey, "Peur : basculer 100 / 0", DebugToggle);
                DebugManager.RegisterInfo("Peur", BuildDebugInfo);
            }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<NoiseEmittedEvent>(OnNoiseEmitted);
            EventBus.Unsubscribe<LightStateChangedEvent>(OnLightStateChanged);

            if (registerDebugCommand)
            {
                DebugManager.Unregister(debugToggleKey);
                DebugManager.UnregisterInfo("Peur");
            }
        }

        private void Start()
        {
            Publish(true);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_noiseCooldownTimer > 0f)
            {
                _noiseCooldownTimer -= dt;
            }

            if (_debugLocked)
            {
                return;
            }

            if (_player != null && !_player.IsAlive)
            {
                return;
            }

            float gain = ComputeGain();
            float recovery = ComputeRecovery();

            _lastGain = gain * gainMultiplier;
            _lastRecovery = recovery * recoveryMultiplier;

            float delta = (_lastGain - _lastRecovery) * dt;

            if (Mathf.Abs(delta) > 0.00001f)
            {
                SetFear(_fear + delta);
            }
        }

        // ------------------------------------------------------------------
        // Calcul
        // ------------------------------------------------------------------

        private float ComputeGain()
        {
            float gain = 0f;

            // Obscurite : proportionnelle au manque de lumiere.
            float darkness = 1f - LightLevel;
            gain += darknessRate * darkness;

            // Isolement : en coop uniquement, sinon le solo serait toujours "isole".
            if (isolationRate > 0f && CountNearbyTeammates() == 0 && IsCooperative())
            {
                gain += isolationRate;
            }

            gain += MonsterProximityRate();

            if (_isChased)
            {
                gain += chaseRate;
            }

            return gain;
        }

        private float ComputeRecovery()
        {
            float recovery = 0f;

            if (!IsThreatened)
            {
                recovery += Mathf.Lerp(darkRecovery, lightRecovery, LightLevel);
                recovery += teammateRecovery * Mathf.Min(2, CountNearbyTeammates());
            }

            if (IsInSafeZone)
            {
                recovery += safeZoneRecovery;
            }

            return recovery;
        }

        /// <summary>Paliers de proximite du cahier des charges (section 18).</summary>
        private float MonsterProximityRate()
        {
            if (!_hasMonsterDistance)
            {
                return 0f;
            }

            float d = _monsterDistance;

            if (d >= 15f) return 0f;
            if (d >= 10f) return 0.8f;
            if (d >= 7f) return 2f;
            if (d >= 4f) return 5f;
            if (d >= 2f) return 10f;
            return 20f;
        }

        private int CountNearbyTeammates()
        {
            if (_player == null || !PlayerManager.HasInstance)
            {
                return 0;
            }

            PlayerManager manager = PlayerManager.Instance;

            if (manager == null)
            {
                return 0;
            }

            return manager.CountNearbyPlayers(_player, isolationRadius);
        }

        private static bool IsCooperative()
        {
            if (!GameManager.HasInstance)
            {
                return false;
            }

            GameManager game = GameManager.Instance;
            return game != null && game.Mode == GameMode.Cooperative;
        }

        private static FearLevel LevelFor(float fear)
        {
            if (fear >= 80f) return FearLevel.Panic;
            if (fear >= 60f) return FearLevel.Afraid;
            if (fear >= 40f) return FearLevel.Stressed;
            if (fear >= 20f) return FearLevel.Worried;
            return FearLevel.Calm;
        }

        private void SetFear(float value)
        {
            _fear = Mathf.Clamp(value, 0f, MaxFear);

            // Hysteresis de 2 points : on ne redescend de palier qu'en passant
            // nettement sous le seuil, pour eviter le clignotement des effets.
            FearLevel candidate = LevelFor(_fear);

            if (candidate < _level)
            {
                float threshold = ThresholdFor(_level);

                if (_fear > threshold - 2f)
                {
                    candidate = _level;
                }
            }

            if (candidate != _level)
            {
                FearLevel previous = _level;
                _level = candidate;
                EventBus.Publish(new FearLevelChangedEvent(_player, previous, _level));
            }

            Publish(false);
        }

        private static float ThresholdFor(FearLevel level)
        {
            switch (level)
            {
                case FearLevel.Panic: return 80f;
                case FearLevel.Afraid: return 60f;
                case FearLevel.Stressed: return 40f;
                case FearLevel.Worried: return 20f;
                default: return 0f;
            }
        }

        private void Publish(bool force)
        {
            if (!force && Mathf.Abs(_fear - _lastPublished) < 0.5f)
            {
                return;
            }

            _lastPublished = _fear;
            EventBus.Publish(new FearChangedEvent(_player, _fear, Normalized, _level));
        }

        // ------------------------------------------------------------------
        // API publique : sources externes
        // ------------------------------------------------------------------

        /// <summary>Pic de peur immediat (evenement paranormal, apparition, cadavre...).</summary>
        public void AddFear(float amount, string reason = null)
        {
            if (amount <= 0f || _debugLocked)
            {
                return;
            }

            SetFear(_fear + amount * gainMultiplier);

            if (!string.IsNullOrEmpty(reason))
            {
                Debug.Log("[Peur] +" + Mathf.RoundToInt(amount) + " (" + reason + ") -> " + Mathf.RoundToInt(_fear));
            }
        }

        /// <summary>Apaisement immediat (objet reconfortant, coequipier retrouve).</summary>
        public void Relieve(float amount)
        {
            if (amount <= 0f || _debugLocked)
            {
                return;
            }

            SetFear(_fear - amount * recoveryMultiplier);
        }

        /// <summary>Distance a la creature la plus proche. Appele chaque frame par la Phase 18.</summary>
        public void SetMonsterDistance(float distance)
        {
            _monsterDistance = distance;
            _hasMonsterDistance = true;
        }

        /// <summary>Plus aucune creature a portee.</summary>
        public void ClearMonsterDistance()
        {
            _hasMonsterDistance = false;
            _monsterDistance = float.MaxValue;
        }

        /// <summary>Le joueur est poursuivi (Phase 12).</summary>
        public void SetChased(bool chased)
        {
            _isChased = chased;
        }

        public void EnterSafeZone()
        {
            _safeZoneCount++;
        }

        public void ExitSafeZone()
        {
            _safeZoneCount = Mathf.Max(0, _safeZoneCount - 1);
        }

        /// <summary>Remet la peur a zero (nouvelle partie, reapparition).</summary>
        public void ResetFear()
        {
            _debugLocked = false;
            _isChased = false;
            _safeZoneCount = 0;
            ClearMonsterDistance();
            SetFear(startFear);
        }

        // ------------------------------------------------------------------
        // Bruits
        // ------------------------------------------------------------------

        private void OnNoiseEmitted(NoiseEmittedEvent evt)
        {
            if (evt.Radius < loudNoiseRadiusThreshold || _noiseCooldownTimer > 0f)
            {
                return;
            }

            // Un bruit provoque par le joueur lui-meme ne l'effraie pas.
            if (evt.Source != null && (evt.Source == gameObject || evt.Source.transform.IsChildOf(transform)))
            {
                return;
            }

            float distance = Vector3.Distance(evt.Position, transform.position);

            if (distance > evt.Radius)
            {
                return;
            }

            float falloff = 1f - Mathf.Clamp01(distance / evt.Radius);
            float amount = fearPerLoudNoise * falloff;

            if (amount < 0.5f)
            {
                return;
            }

            _noiseCooldownTimer = loudNoiseCooldown;
            AddFear(amount, "bruit violent a " + Mathf.RoundToInt(distance) + " m");
        }

        // ------------------------------------------------------------------
        // Lampes
        // ------------------------------------------------------------------

        /// <summary>
        /// Une lampe qui lache A COTE du joueur est un choc : un pic immediat,
        /// en plus de l'obscurite qui va suivre. Une lampe qu'on eteint
        /// soi-meme (interrupteur) ou qui se rallume n'effraie pas.
        /// </summary>
        private void OnLightStateChanged(LightStateChangedEvent evt)
        {
            if (evt.Previous != LightState.On)
            {
                return;
            }

            float distance = Vector3.Distance(evt.Position, transform.position);

            if (distance > lightEventRadius)
            {
                return;
            }

            float amount;
            string reason;

            switch (evt.Current)
            {
                case LightState.Broken:
                    amount = fearPerLightBreak;
                    reason = "ampoule eclatee";
                    break;

                case LightState.Flickering:
                    amount = fearPerLightFlicker;
                    reason = "lampe qui scintille";
                    break;

                default:
                    // Extinction volontaire par un interrupteur : pas de choc.
                    return;
            }

            float falloff = 1f - Mathf.Clamp01(distance / lightEventRadius) * 0.6f;
            AddFear(amount * falloff, reason);
        }

        // ------------------------------------------------------------------
        // Debug
        // ------------------------------------------------------------------

        private void DebugToggle()
        {
            if (_debugLocked)
            {
                _debugLocked = false;
                SetFear(0f);
                Debug.Log("[Peur] Debug : peur liberee et remise a 0.");
                return;
            }

            _debugLocked = true;
            SetFear(MaxFear);
            Debug.Log("[Peur] Debug : peur verrouillee a 100.");
        }

        private string BuildDebugInfo()
        {
            return "  Peur     : " + Mathf.RoundToInt(_fear) + " / 100   [" + _level + "]" + (_debugLocked ? "  (VERROUILLE)" : "")
                + "\n  Lumiere  : " + LightLevel.ToString("F2")
                + "   Coequipiers : " + CountNearbyTeammates()
                + "   Zone sure : " + (IsInSafeZone ? "oui" : "non")
                + "\n  Montee   : +" + _lastGain.ToString("F1") + "/s   Apaisement : -" + _lastRecovery.ToString("F1") + "/s"
                + (_isChased ? "   POURSUITE" : "")
                + (_hasMonsterDistance ? "   Creature : " + Mathf.RoundToInt(_monsterDistance) + " m" : "");
        }
    }
}
