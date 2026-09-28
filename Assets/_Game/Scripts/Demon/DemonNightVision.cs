using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Vision nocturne du demon (seulement quand on le joue) : un Volume de
    /// post-traitement (exposition, teinte, vignette, grain) sur la camera du demon,
    /// plus une lumiere ambiante relevee et un brouillard allege tant qu'elle est active.
    ///
    /// Elle s'adapte a l'obscurite : pleine puissance la nuit, nulle en plein jour
    /// (Auto By Darkness). N pour la couper / la rallumer. Tout se regle dans l'Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class DemonNightVision : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private bool nightVisionEnabled = true;
        [SerializeField] private bool startOn = true;
        [Tooltip("#(N) = la touche qui affiche N (AZERTY comme QWERTY).")]
        [SerializeField] private string toggleBinding = "<Keyboard>/#(N)";
        [Tooltip("Temps d'apparition / disparition (s).")]
        [SerializeField, Min(0.01f)] private float fadeTime = 0.6f;

        [Header("References")]
        [SerializeField] private DemonController controller;
        [Tooltip("Volume global du demon (exposition, teinte, vignette, grain). Seule sa camera a le post-traitement.")]
        [SerializeField] private Volume volume;

        [Header("Adaptation a l'obscurite")]
        [SerializeField] private bool autoByDarkness = true;
        [Tooltip("Luminosite ambiante (0-1) en dessous de laquelle la vision est a pleine puissance / au-dessus de laquelle elle s'eteint.")]
        [SerializeField] private Vector2 darknessRange = new Vector2(0.05f, 0.25f);

        [Header("Eclairage du monde vu par le demon")]
        [Tooltip("Lumiere ambiante ajoutee a pleine puissance.")]
        [SerializeField] private Color ambientBoost = new Color(0.42f, 0.34f, 0.34f, 0f);
        [Tooltip("Densite du brouillard multipliee par cette valeur (0.3 = on voit ~3x plus loin).")]
        [SerializeField, Range(0f, 1f)] private float fogDensityMultiplier = 0.25f;
        [SerializeField] private Color fogColor = new Color(0.07f, 0.045f, 0.045f, 1f);
        [Tooltip("Reflets d'environnement a pleine puissance (feuillage lisible).")]
        [SerializeField, Range(0f, 1f)] private float reflectionIntensity = 0.35f;

        [Header("HUD")]
        [SerializeField] private bool showIndicator = true;

        private InputAction _toggle;
        private bool _pending;
        private bool _on;
        private float _weight;

        // Valeurs du monde (cycle jour/nuit) sous notre modification.
        private bool _applied;
        private Color _baseAmbient;
        private float _baseFogDensity;
        private Color _baseFogColor;
        private float _baseReflection;
        private Color _setAmbient;
        private float _setFogDensity;
        private Color _setFogColor;
        private float _setReflection;

        private GUIStyle _style;

        public bool IsOn { get { return _on; } }
        public float Strength { get { return _weight * Darkness(); } }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<DemonController>();
            _on = startOn;
            if (volume != null) volume.weight = 0f;
        }

        private void OnEnable()
        {
            _toggle = new InputAction("NightVision", InputActionType.Button, toggleBinding);
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

            Restore();
            if (volume != null) volume.weight = 0f;
        }

        private void LateUpdate()
        {
            bool controlled = controller != null && controller.IsControlled;

            if (_pending)
            {
                _pending = false;
                if (controlled && controller.InputAllowed) _on = !_on;
            }

            // Coupee pendant un jumpscare : c'est la vue de la victime qui s'affiche.
            bool active = nightVisionEnabled && controlled && _on && !SurvivorJumpscare.AnyPlaying;
            _weight = Mathf.MoveTowards(_weight, active ? 1f : 0f, Time.deltaTime / fadeTime);

            CaptureBase();
            float strength = _weight * Darkness();

            if (volume != null) volume.weight = strength;

            if (strength <= 0.0001f)
            {
                Restore();
                return;
            }

            _setAmbient = _baseAmbient + ambientBoost * strength;
            _setAmbient.a = _baseAmbient.a;
            _setFogDensity = _baseFogDensity * Mathf.Lerp(1f, fogDensityMultiplier, strength);
            _setFogColor = Color.Lerp(_baseFogColor, fogColor, strength);
            _setReflection = Mathf.Lerp(_baseReflection, Mathf.Max(_baseReflection, reflectionIntensity), strength);

            RenderSettings.ambientLight = _setAmbient;
            RenderSettings.fogDensity = _setFogDensity;
            RenderSettings.fogColor = _setFogColor;
            RenderSettings.reflectionIntensity = _setReflection;
            _applied = true;
        }

        /// <summary>
        /// Memorise les valeurs du monde. Si quelqu'un d'autre (cycle jour/nuit) les a
        /// changees depuis notre derniere ecriture, ce sont les nouvelles valeurs de base.
        /// </summary>
        private void CaptureBase()
        {
            if (!_applied || RenderSettings.ambientLight != _setAmbient) _baseAmbient = RenderSettings.ambientLight;
            if (!_applied || !Mathf.Approximately(RenderSettings.fogDensity, _setFogDensity)) _baseFogDensity = RenderSettings.fogDensity;
            if (!_applied || RenderSettings.fogColor != _setFogColor) _baseFogColor = RenderSettings.fogColor;
            if (!_applied || !Mathf.Approximately(RenderSettings.reflectionIntensity, _setReflection)) _baseReflection = RenderSettings.reflectionIntensity;
        }

        private void Restore()
        {
            if (!_applied) return;

            RenderSettings.ambientLight = _baseAmbient;
            RenderSettings.fogDensity = _baseFogDensity;
            RenderSettings.fogColor = _baseFogColor;
            RenderSettings.reflectionIntensity = _baseReflection;
            _applied = false;
        }

        /// <summary>1 dans le noir, 0 en plein jour (d'apres la lumiere ambiante du monde).</summary>
        private float Darkness()
        {
            if (!autoByDarkness) return 1f;
            Color a = _applied ? _baseAmbient : RenderSettings.ambientLight;
            float luminance = a.r * 0.2126f + a.g * 0.7152f + a.b * 0.0722f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(darknessRange.x, darknessRange.y, luminance));
        }

        private void OnGUI()
        {
            if (!showIndicator || controller == null || !controller.IsControlled || !controller.InputAllowed || !nightVisionEnabled || SurvivorJumpscare.AnyPlaying)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
            }

            float scale = Screen.height / 1080f;
            _style.fontSize = Mathf.RoundToInt(15f * scale);
            string text = !_on ? "VISION NOCTURNE  coupee  [N]"
                : Darkness() < 0.05f ? "VISION NOCTURNE  en veille (jour)  [N]"
                : "VISION NOCTURNE  " + Mathf.RoundToInt(Strength * 100f) + " %  [N]";
            _style.normal.textColor = _on ? new Color(1f, 0.45f, 0.35f, 0.85f) : new Color(0.7f, 0.7f, 0.7f, 0.6f);
            GUI.Label(new Rect(Screen.width - 520f * scale, Screen.height - 44f * scale, 500f * scale, 30f * scale), text, _style);
        }
    }
}
