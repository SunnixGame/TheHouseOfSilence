using HouseOfSilence.Core;
using UnityEngine;

namespace HouseOfSilence.Lights
{
    /// <summary>
    /// Pilote une lampe de la maison : ON, OFF, scintillement, casse.
    ///
    /// - Depend du courant general (LightManager) si Requires Power est coche.
    /// - Peut tomber en panne toute seule (Random Failures Per Minute).
    /// - Reagit a la creature : plus elle est proche, plus la lampe scintille,
    ///   jusqu'a s'eteindre (Monster Influence). La Phase 18 alimente cette
    ///   proximite via LightManager.SetMonsterPosition().
    /// - Peut casser definitivement a la fin d'un scintillement (Chance To Break).
    ///
    /// Le composant pilote un ou plusieurs Light enfants, et optionnellement
    /// le materiau d'une ampoule (teinte claire / sombre, sans instancier de materiau).
    ///
    /// A poser sur le GameObject parent des Light.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightController : MonoBehaviour
    {
        [Header("Lumieres")]
        [Tooltip("Vide = tous les Light enfants.")]
        [SerializeField] private Light[] lights;

        [Tooltip("Renderer de l'ampoule, teinte selon l'etat. Facultatif.")]
        [SerializeField] private Renderer bulbRenderer;

        [SerializeField] private Color bulbOnColor = new Color(1f, 0.95f, 0.8f);
        [SerializeField] private Color bulbOffColor = new Color(0.18f, 0.17f, 0.16f);

        [Header("Etat")]
        [SerializeField] private bool startOn = true;

        [Tooltip("S'eteint quand le courant general est coupe.")]
        [SerializeField] private bool requiresPower = true;

        [Header("Scintillement")]
        [Tooltip("Duree par defaut d'un scintillement, en secondes.")]
        [SerializeField, Min(0.1f)] private float flickerDuration = 2.5f;

        [Tooltip("Changements d'etat par seconde pendant un scintillement.")]
        [SerializeField, Range(2f, 40f)] private float flickerFrequency = 12f;

        [Tooltip("Intensite minimale pendant le scintillement (0 = extinction franche).")]
        [SerializeField, Range(0f, 1f)] private float flickerMinIntensity = 0.05f;

        [Tooltip("Probabilite que la lampe casse a la fin d'un scintillement (0 a 1).")]
        [SerializeField, Range(0f, 1f)] private float chanceToBreak = 0.1f;

        [Header("Pannes aleatoires")]
        [SerializeField] private bool randomFailures = true;

        [Tooltip("Nombre moyen de scintillements spontanes par minute.")]
        [SerializeField, Range(0f, 10f)] private float randomFailuresPerMinute = 0.4f;

        [Header("Creature")]
        [Tooltip("Sensibilite a la creature : 0 = insensible, 1 = s'eteint des qu'elle approche.")]
        [SerializeField, Range(0f, 1f)] private float monsterInfluence = 1f;

        [Header("Bruit")]
        [Tooltip("Rayon du bruit quand l'ampoule eclate.")]
        [SerializeField, Min(0f)] private float breakNoiseRadius = 8f;

        // ------------------------------------------------------------------

        private float[] _baseIntensities;
        private MaterialPropertyBlock _block;
        private string _bulbColorProperty;

        private LightState _state = LightState.Off;
        private bool _wantsOn;
        private bool _powered = true;
        private float _monsterProximity;
        private bool _monsterSuppressed;

        private float _flickerTimer;
        private float _flickerStepTimer;
        private bool _flickerBreakAtEnd;
        private bool _flickerAllowRandomBreak = true;
        private bool _flickerPhaseOn = true;
        private float _flickerLevel = 1f;

        private bool _currentlyLit;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        /// <summary>Etat logique courant.</summary>
        public LightState State { get { return _state; } }

        /// <summary>Vrai si la lampe emet reellement de la lumiere en ce moment.</summary>
        public bool IsLit { get { return _currentlyLit; } }

        public bool IsBroken { get { return _state == LightState.Broken; } }
        public bool RequiresPower { get { return requiresPower; } }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (lights == null || lights.Length == 0)
            {
                lights = GetComponentsInChildren<Light>(true);
            }

            _baseIntensities = new float[lights.Length];

            for (int i = 0; i < lights.Length; i++)
            {
                _baseIntensities[i] = lights[i] != null ? lights[i].intensity : 0f;
            }

            if (bulbRenderer != null)
            {
                _block = new MaterialPropertyBlock();
                Material material = bulbRenderer.sharedMaterial;

                if (material != null)
                {
                    if (material.HasProperty(BaseColorId))
                    {
                        _bulbColorProperty = "_BaseColor";
                    }
                    else if (material.HasProperty(LegacyColorId))
                    {
                        _bulbColorProperty = "_Color";
                    }
                }
            }

            _wantsOn = startOn;
            _state = startOn ? LightState.On : LightState.Off;
        }

        private void OnEnable()
        {
            LightManager manager = LightManager.Instance;

            if (manager != null)
            {
                manager.Register(this);
                _powered = manager.IsPowered;
            }

            ApplyLit(ComputeLit());
        }

        private void OnDisable()
        {
            if (LightManager.HasInstance)
            {
                LightManager manager = LightManager.Instance;

                if (manager != null)
                {
                    manager.Unregister(this);
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            UpdateFlicker(dt);
            UpdateRandomFailure(dt);
            UpdateMonsterInfluence(dt);

            ApplyLit(ComputeLit());
        }

        // ------------------------------------------------------------------
        // Logique
        // ------------------------------------------------------------------

        private bool ComputeLit()
        {
            if (_state == LightState.Broken || !_wantsOn)
            {
                return false;
            }

            if (requiresPower && !_powered)
            {
                return false;
            }

            if (_monsterSuppressed)
            {
                return false;
            }

            if (_state == LightState.Flickering)
            {
                return _flickerPhaseOn;
            }

            return true;
        }

        private void ApplyLit(bool lit)
        {
            float level = lit ? (_state == LightState.Flickering ? _flickerLevel : 1f) : 0f;

            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];

                if (light == null)
                {
                    continue;
                }

                light.enabled = lit;
                light.intensity = _baseIntensities[i] * level;
            }

            if (bulbRenderer != null && _bulbColorProperty != null)
            {
                Color color = Color.Lerp(bulbOffColor, bulbOnColor, level);
                bulbRenderer.GetPropertyBlock(_block);
                _block.SetColor(_bulbColorProperty, color);
                bulbRenderer.SetPropertyBlock(_block);
            }

            _currentlyLit = lit;
        }

        private void UpdateFlicker(float dt)
        {
            if (_state != LightState.Flickering)
            {
                return;
            }

            _flickerTimer -= dt;
            _flickerStepTimer -= dt;

            if (_flickerStepTimer <= 0f)
            {
                // Pas de metronome : les intervalles sont irreguliers, c'est ce qui rend le scintillement credible.
                _flickerStepTimer = Random.Range(0.4f, 1.6f) / flickerFrequency;
                _flickerPhaseOn = Random.value > 0.35f;
                _flickerLevel = _flickerPhaseOn ? Random.Range(flickerMinIntensity, 1f) : 0f;
            }

            if (_flickerTimer > 0f)
            {
                return;
            }

            // Fin du scintillement.
            if (_flickerBreakAtEnd || (_flickerAllowRandomBreak && Random.value < chanceToBreak))
            {
                Break();
                return;
            }

            _flickerLevel = 1f;
            _flickerPhaseOn = true;
            SetState(_wantsOn ? LightState.On : LightState.Off);
        }

        private void UpdateRandomFailure(float dt)
        {
            if (!randomFailures || randomFailuresPerMinute <= 0f)
            {
                return;
            }

            if (_state != LightState.On || !_currentlyLit)
            {
                return;
            }

            float chancePerSecond = randomFailuresPerMinute / 60f;

            if (Random.value < chancePerSecond * dt)
            {
                StartFlicker(Random.Range(flickerDuration * 0.5f, flickerDuration * 1.5f));
            }
        }

        private void UpdateMonsterInfluence(float dt)
        {
            float influence = _monsterProximity * monsterInfluence;

            _monsterSuppressed = influence > 0.85f && _state != LightState.Broken;

            if (influence <= 0.4f || _state != LightState.On || !_currentlyLit)
            {
                return;
            }

            // Plus la creature est proche, plus les scintillements sont frequents.
            float chancePerSecond = Mathf.Lerp(0.3f, 3f, Mathf.InverseLerp(0.4f, 0.85f, influence));

            if (Random.value < chancePerSecond * dt)
            {
                StartFlicker(Random.Range(0.3f, 1.2f), false, false);
            }
        }

        private void SetState(LightState next)
        {
            if (_state == next)
            {
                return;
            }

            LightState previous = _state;
            _state = next;

            EventBus.Publish(new LightStateChangedEvent(this, previous, next, transform.position));
        }

        // ------------------------------------------------------------------
        // API publique
        // ------------------------------------------------------------------

        public void TurnOn()
        {
            if (_state == LightState.Broken)
            {
                return;
            }

            _wantsOn = true;

            if (_state != LightState.Flickering)
            {
                SetState(LightState.On);
            }
        }

        public void TurnOff()
        {
            if (_state == LightState.Broken)
            {
                return;
            }

            _wantsOn = false;

            if (_state != LightState.Flickering)
            {
                SetState(LightState.Off);
            }
        }

        public void Toggle()
        {
            if (_wantsOn)
            {
                TurnOff();
            }
            else
            {
                TurnOn();
            }
        }

        /// <summary>
        /// Lance un scintillement. Sans effet sur une lampe eteinte ou cassee.
        /// </summary>
        /// <param name="duration">Duree en secondes, ou -1 pour la duree par defaut.</param>
        /// <param name="breakAtEnd">Force la casse a la fin.</param>
        /// <param name="allowRandomBreak">Applique Chance To Break a la fin.</param>
        public void StartFlicker(float duration = -1f, bool breakAtEnd = false, bool allowRandomBreak = true)
        {
            if (_state == LightState.Broken || !_wantsOn)
            {
                return;
            }

            if (requiresPower && !_powered)
            {
                return;
            }

            _flickerTimer = duration > 0f ? duration : flickerDuration;
            _flickerStepTimer = 0f;
            _flickerBreakAtEnd = breakAtEnd;
            _flickerAllowRandomBreak = allowRandomBreak;

            if (_state != LightState.Flickering)
            {
                SetState(LightState.Flickering);
            }
        }

        /// <summary>L'ampoule eclate : eteinte jusqu'a Repair().</summary>
        public void Break()
        {
            if (_state == LightState.Broken)
            {
                return;
            }

            _flickerLevel = 0f;
            _flickerPhaseOn = false;
            SetState(LightState.Broken);

            Noise.Emit(transform.position, breakNoiseRadius, NoiseType.Object, gameObject);
        }

        /// <summary>Repare une lampe cassee (elle revient dans son etat demande).</summary>
        public void Repair()
        {
            if (_state != LightState.Broken)
            {
                return;
            }

            _flickerLevel = 1f;
            _flickerPhaseOn = true;
            SetState(_wantsOn ? LightState.On : LightState.Off);
        }

        /// <summary>Appele par le LightManager quand le courant general change.</summary>
        public void SetPowered(bool powered)
        {
            _powered = powered;

            if (!powered && _state == LightState.Flickering)
            {
                _flickerTimer = 0f;
                _flickerLevel = 1f;
                _flickerPhaseOn = true;
                SetState(_wantsOn ? LightState.On : LightState.Off);
            }
        }

        /// <summary>Proximite de la creature, de 0 (loin) a 1 (au contact). Appele par le LightManager.</summary>
        public void SetMonsterProximity(float proximity)
        {
            _monsterProximity = Mathf.Clamp01(proximity);
        }
    }
}
