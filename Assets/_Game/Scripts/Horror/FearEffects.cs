using HouseOfSilence.Core;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HouseOfSilence.Horror
{
    /// <summary>
    /// Traduit la peur en effets visuels :
    /// - vignette qui se resserre, aberration chromatique, desaturation
    ///   (Volume URP cree a la volee, aucun asset a configurer) ;
    /// - tremblement de camera via PlayerLook.SetFearShake() ;
    /// - pulsation au rythme du coeur en etat de panique.
    ///
    /// Tout est pilote par FearChangedEvent : ce composant n'a aucune
    /// dependance sur FearSystem.
    ///
    /// Package requis : com.unity.render-pipelines.universal (deja installe).
    /// Le post-processing est active automatiquement sur la camera du joueur.
    /// </summary>
    [DisallowMultipleComponent]
    public class FearEffects : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private PlayerLook look;

        [Header("Seuils")]
        [Tooltip("Peur (0-1) a partir de laquelle les effets visuels commencent.")]
        [SerializeField, Range(0f, 1f)] private float visualStart = 0.2f;

        [Tooltip("Peur (0-1) a partir de laquelle la camera tremble.")]
        [SerializeField, Range(0f, 1f)] private float shakeStart = 0.4f;

        [Header("Intensites maximales (a 100 de peur)")]
        [SerializeField, Range(0f, 1f)] private float maxVignette = 0.55f;
        [SerializeField, Range(0f, 1f)] private float vignetteSmoothness = 0.45f;
        [SerializeField, Range(0f, 1f)] private float maxChromaticAberration = 0.4f;
        [SerializeField, Range(-100f, 0f)] private float maxDesaturation = -40f;

        [Header("Pulsation (panique)")]
        [SerializeField, Range(0f, 0.3f)] private float pulseAmplitude = 0.12f;
        [SerializeField, Range(0.5f, 3f)] private float pulseFrequency = 1.6f;

        [Header("Lissage")]
        [SerializeField, Min(0.1f)] private float smoothing = 3f;

        private Volume _volume;
        private VolumeProfile _profile;
        private Vignette _vignette;
        private ChromaticAberration _aberration;
        private ColorAdjustments _colorAdjustments;

        private float _targetFear;
        private float _currentFear;
        private FearLevel _level = FearLevel.Calm;
        private bool _built;

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponent<PlayerCharacter>();
            }

            if (look == null)
            {
                look = GetComponent<PlayerLook>();
            }

            BuildVolume();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<FearChangedEvent>(OnFearChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<FearChangedEvent>(OnFearChanged);

            if (look != null)
            {
                look.SetFearShake(0f);
            }

            if (_volume != null)
            {
                _volume.weight = 0f;
            }
        }

        private void OnDestroy()
        {
            if (_profile != null)
            {
                Destroy(_profile);
                _profile = null;
            }
        }

        // ------------------------------------------------------------------

        private void BuildVolume()
        {
            if (_built)
            {
                return;
            }

            Camera camera = player != null ? player.Camera : GetComponentInChildren<Camera>(true);

            if (camera == null)
            {
                Debug.LogWarning("[Peur] FearEffects : aucune camera trouvee, effets visuels desactives.", this);
                return;
            }

            // Le post-processing doit etre actif sur la camera pour que le Volume agisse.
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();

            if (cameraData != null)
            {
                cameraData.renderPostProcessing = true;
            }

            GameObject host = new GameObject("FearVolume");
            host.transform.SetParent(camera.transform, false);

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "FearProfile (runtime)";
            _profile.hideFlags = HideFlags.HideAndDontSave;

            _vignette = _profile.Add<Vignette>(true);
            _vignette.intensity.Override(maxVignette);
            _vignette.smoothness.Override(vignetteSmoothness);
            _vignette.color.Override(Color.black);
            _vignette.rounded.Override(false);

            _aberration = _profile.Add<ChromaticAberration>(true);
            _aberration.intensity.Override(maxChromaticAberration);

            _colorAdjustments = _profile.Add<ColorAdjustments>(true);
            _colorAdjustments.saturation.Override(maxDesaturation);

            _volume = host.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.weight = 0f;
            _volume.sharedProfile = _profile;

            _built = true;
        }

        private void OnFearChanged(FearChangedEvent evt)
        {
            if (player != null && evt.Player != player)
            {
                return;
            }

            _targetFear = evt.Normalized;
            _level = evt.Level;
        }

        private void Update()
        {
            // Lissage : la peur monte par a-coups, les effets doivent glisser.
            _currentFear = Mathf.Lerp(_currentFear, _targetFear, 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));

            ApplyVisuals();
            ApplyShake();
        }

        private void ApplyVisuals()
        {
            if (_volume == null)
            {
                return;
            }

            float weight = Mathf.InverseLerp(visualStart, 1f, _currentFear);

            if (_level == FearLevel.Panic && pulseAmplitude > 0f)
            {
                float pulse = (Mathf.Sin(Time.unscaledTime * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
                weight = Mathf.Clamp01(weight - pulseAmplitude * pulse);
            }

            _volume.weight = weight;
        }

        private void ApplyShake()
        {
            if (look == null)
            {
                return;
            }

            float shake = Mathf.InverseLerp(shakeStart, 1f, _currentFear);
            look.SetFearShake(shake);
        }
    }
}
