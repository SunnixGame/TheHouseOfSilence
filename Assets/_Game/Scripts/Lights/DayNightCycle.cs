using HouseOfSilence.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace HouseOfSilence.Lights
{
    /// <summary>
    /// Cycle jour / nuit : fait tourner le soleil et la lune selon l'heure et regle
    /// l'ambiance, le brouillard, le ciel et les reflets. Tout se regle dans l'Inspector :
    /// heure, duree d'une journee, pause, et pour chaque effet une couleur (Gradient) ou
    /// une intensite (AnimationCurve) sur 24 h (0 = minuit, 0.5 = midi, 1 = minuit).
    ///
    /// En mode edition, les changements faits dans l'Inspector sont appliques tout de
    /// suite (apercu), sauf le ciel qui n'est anime qu'en jeu (copie du materiau).
    ///
    /// Jour / nuit fixe : "Fixed Time" fige l'heure a 13 h (Jour) ou 24 h (Nuit) ;
    /// F3 en jeu passe de l'un a l'autre avec une courte transition.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class DayNightCycle : MonoBehaviour
    {
        public enum FixedTime { Libre, Jour, Nuit }

        [Header("Jour / nuit fixe")]
        [Tooltip("Jour = heure figee a Day Hour, Nuit = figee a Night Hour, Libre = Time Of Day ci-dessous. Ignore quand le cycle tourne.")]
        [SerializeField] private FixedTime fixedTime = FixedTime.Jour;
        [SerializeField, Range(0f, 24f)] private float dayHour = 13f;
        [SerializeField, Range(0f, 24f)] private float nightHour = 24f;

        [Tooltip("Touche pour passer du jour a la nuit en jeu (arrete le cycle s'il tourne).")]
        [SerializeField] private string toggleBinding = "<Keyboard>/f3";
        [Tooltip("Duree de la transition en jeu (s) ; 0 = immediat.")]
        [SerializeField, Min(0f)] private float transitionSeconds = 2.5f;
        [SerializeField] private bool showMessage = true;

        [Header("Heure")]
        [Tooltip("Heure courante, de 0 a 24.")]
        [SerializeField, Range(0f, 24f)] private float timeOfDay = 13f;

        [Tooltip("Decoche pour figer l'heure (jour permanent pour l'instant).")]
        [SerializeField] private bool cycleRunning = false;

        [Tooltip("Duree d'une journee complete (24 h de jeu), en minutes reelles.")]
        [SerializeField, Min(0.1f)] private float dayLengthMinutes = 20f;

        [Header("Astres")]
        [SerializeField] private Light sun;
        [SerializeField] private Light moon;

        [Tooltip("Orientation de la course du soleil (degres autour de l'axe vertical).")]
        [SerializeField, Range(-180f, 180f)] private float sunYaw = -35f;

        [Tooltip("Hauteur maximale du soleil a midi (degres).")]
        [SerializeField, Range(10f, 90f)] private float maxSunElevation = 60f;

        [Tooltip("Intensite du soleil selon l'heure.")]
        [SerializeField] private AnimationCurve sunIntensity = Curve(0f, 0f, 0.24f, 0f, 0.3f, 0.7f, 0.5f, 1.3f, 0.7f, 0.7f, 0.76f, 0f, 1f, 0f);

        [SerializeField] private Gradient sunColor = Colors(
            0.25f, new Color(1f, 0.45f, 0.25f),
            0.35f, new Color(1f, 0.85f, 0.7f),
            0.5f, new Color(1f, 0.96f, 0.88f),
            0.65f, new Color(1f, 0.85f, 0.7f),
            0.75f, new Color(1f, 0.42f, 0.22f));

        [Tooltip("Intensite de la lune selon l'heure (0.05 = nuit tres sombre).")]
        [SerializeField] private AnimationCurve moonIntensity = Curve(0f, 0.05f, 0.23f, 0.05f, 0.28f, 0f, 0.72f, 0f, 0.77f, 0.05f, 1f, 0.05f);

        [Header("Ambiance")]
        [Tooltip("Lumiere ambiante (mode Flat) selon l'heure.")]
        [SerializeField] private Gradient ambientColor = Colors(
            0.2f, new Color(0.006f, 0.007f, 0.011f),
            0.3f, new Color(0.22f, 0.2f, 0.2f),
            0.5f, new Color(0.42f, 0.45f, 0.5f),
            0.7f, new Color(0.22f, 0.18f, 0.18f),
            0.8f, new Color(0.006f, 0.007f, 0.011f));

        [Tooltip("Intensite des reflets d'environnement (bas la nuit : sinon le feuillage luit).")]
        [SerializeField] private AnimationCurve reflectionIntensity = Curve(0f, 0.08f, 0.22f, 0.08f, 0.32f, 1f, 0.68f, 1f, 0.78f, 0.08f, 1f, 0.08f);

        [Header("Brouillard")]
        [SerializeField] private bool controlFog = true;

        [SerializeField] private Gradient fogColor = Colors(
            0.2f, new Color(0.004f, 0.005f, 0.008f),
            0.3f, new Color(0.45f, 0.38f, 0.35f),
            0.5f, new Color(0.55f, 0.6f, 0.62f),
            0.7f, new Color(0.45f, 0.34f, 0.3f),
            0.8f, new Color(0.004f, 0.005f, 0.008f));

        [Tooltip("Densite du brouillard (ExponentialSquared). Garde >= 0.008 le jour : il cache la limite des arbres a 220 m.")]
        [SerializeField] private AnimationCurve fogDensity = Curve(0f, 0.028f, 0.24f, 0.028f, 0.32f, 0.01f, 0.5f, 0.008f, 0.68f, 0.01f, 0.76f, 0.028f, 1f, 0.028f);

        [Header("Ciel (Skybox/Procedural)")]
        [Tooltip("Materiau de ciel de base ; une copie est animee en jeu.")]
        [SerializeField] private Material skyMaterial;

        [SerializeField] private AnimationCurve skyExposure = Curve(0f, 0.025f, 0.22f, 0.025f, 0.3f, 0.7f, 0.5f, 1.1f, 0.7f, 0.7f, 0.78f, 0.025f, 1f, 0.025f);

        [SerializeField] private Gradient skyTint = Colors(
            0.2f, new Color(0.06f, 0.08f, 0.14f),
            0.3f, new Color(0.55f, 0.45f, 0.45f),
            0.5f, new Color(0.5f, 0.5f, 0.5f),
            0.7f, new Color(0.55f, 0.42f, 0.4f),
            0.8f, new Color(0.06f, 0.08f, 0.14f));

        [Tooltip("Epaisseur d'atmosphere : bas = ciel noir (nuit), 1 = ciel bleu clair (jour).")]
        [SerializeField] private AnimationCurve skyAtmosphere = Curve(0f, 0.3f, 0.22f, 0.3f, 0.3f, 1.3f, 0.5f, 1f, 0.7f, 1.3f, 0.78f, 0.3f, 1f, 0.3f);

        [SerializeField] private Gradient skyGround = Colors(
            0.2f, new Color(0.02f, 0.022f, 0.03f),
            0.3f, new Color(0.3f, 0.26f, 0.24f),
            0.5f, new Color(0.37f, 0.35f, 0.34f),
            0.7f, new Color(0.3f, 0.24f, 0.22f),
            0.8f, new Color(0.02f, 0.022f, 0.03f));

        [Tooltip("Taille du disque solaire dans le ciel le jour.")]
        [SerializeField, Range(0f, 0.2f)] private float sunDiscSize = 0.04f;

        private Material _skyInstance;

        private InputAction _toggle;
        private bool _togglePending;
        private float _transitionFrom;
        private float _transitionDelta;
        private float _transitionStart = -1f;
        private float _messageUntil;
        private GUIStyle _messageStyle;

        /// <summary>
        /// Heure imposee de l'exterieur (partie en ligne : l'hote la donne, NetworkTimeOfDay
        /// la pose) : plus d'avance propre, de transition ni de touche F3.
        /// </summary>
        public bool ExternallyDriven { get; set; }

        /// <summary>Meteo : multiplie la densite du brouillard (1 = normal, brume > 1).</summary>
        public float FogMultiplier { get; set; } = 1f;

        /// <summary>Meteo : eclair en cours, de 0 a 1 (illumine l'ambiance et le brouillard).</summary>
        public float LightningFlash { get; set; }

        [Header("Meteo (eclairs)")]
        [SerializeField] private Color lightningAmbient = new Color(0.55f, 0.6f, 0.75f);
        [SerializeField] private Color lightningFog = new Color(0.35f, 0.38f, 0.48f);

        /// <summary>Heure courante (0 a 24).</summary>
        public float TimeOfDay
        {
            get { return timeOfDay; }
            set { timeOfDay = Mathf.Repeat(value, 24f); Apply(); }
        }

        public bool CycleRunning
        {
            get { return cycleRunning; }
            set { cycleRunning = value; }
        }

        /// <summary>Jour / nuit figes (en jeu, le changement passe par une transition).</summary>
        public FixedTime Fixed
        {
            get { return fixedTime; }
            set
            {
                fixedTime = value;

                if (Application.isPlaying)
                {
                    StartTransition();
                }
                else
                {
                    SnapToFixed();
                    Apply();
                }
            }
        }

        /// <summary>Vrai entre le lever et le coucher (soleil plus fort que la lune).</summary>
        public bool IsDay { get { return sunIntensity.Evaluate(timeOfDay / 24f) > moonIntensity.Evaluate(timeOfDay / 24f); } }

        // ------------------------------------------------------------------

        private void OnEnable()
        {
            SnapToFixed();
            Apply();

            if (Application.isPlaying)
            {
                _toggle = new InputAction("DayNightToggle", InputActionType.Button, toggleBinding);
                _toggle.performed += _ => _togglePending = true;
                _toggle.Enable();
            }
        }

        private void OnDisable()
        {
            if (_toggle != null)
            {
                _toggle.Disable();
                _toggle.Dispose();
                _toggle = null;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return; // en edition : applique seulement quand l'Inspector change (OnValidate)
            }

            if (ExternallyDriven)
            {
                _togglePending = false;
                _transitionStart = -1f;
                return; // NetworkTimeOfDay pose l'heure (TimeOfDay) et applique
            }

            if (_togglePending)
            {
                _togglePending = false;
                bool playing = !GameManager.HasInstance || GameManager.Instance == null || GameManager.Instance.IsPlaying;

                if (playing)
                {
                    cycleRunning = false;
                    // En mode Libre, on part de ce que montre l'heure actuelle.
                    bool night = fixedTime == FixedTime.Nuit || (fixedTime == FixedTime.Libre && !IsDay);
                    Fixed = night ? FixedTime.Jour : FixedTime.Nuit;
                    _messageUntil = Time.unscaledTime + 2.5f;
                }
            }

            if (cycleRunning)
            {
                _transitionStart = -1f;
                timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime * 24f / (dayLengthMinutes * 60f), 24f);
            }
            else if (_transitionStart >= 0f)
            {
                // Le temps avance (soiree ou matin) jusqu'a l'heure visee.
                float k = transitionSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - _transitionStart) / transitionSeconds);
                timeOfDay = Mathf.Repeat(_transitionFrom + _transitionDelta * Mathf.SmoothStep(0f, 1f, k), 24f);
                if (k >= 1f) _transitionStart = -1f;
            }
            else
            {
                SnapToFixed();
            }

            Apply();
        }

        private float FixedHour()
        {
            return fixedTime == FixedTime.Nuit ? nightHour : dayHour;
        }

        /// <summary>Colle l'heure au jour ou a la nuit figes (sauf mode Libre ou cycle en marche).</summary>
        private void SnapToFixed()
        {
            if (fixedTime != FixedTime.Libre && !cycleRunning)
            {
                timeOfDay = FixedHour();
            }
        }

        private void StartTransition()
        {
            if (fixedTime == FixedTime.Libre || cycleRunning)
            {
                return;
            }

            _transitionFrom = timeOfDay;
            _transitionDelta = Mathf.Repeat(FixedHour() - timeOfDay, 24f);

            if (_transitionDelta > 0.001f && transitionSeconds > 0f)
            {
                _transitionStart = Time.time;
            }
            else
            {
                _transitionStart = -1f;
                timeOfDay = FixedHour();
            }
        }

        private void OnGUI()
        {
            if (!showMessage || !Application.isPlaying || Time.unscaledTime > _messageUntil)
            {
                return;
            }

            if (_messageStyle == null)
            {
                _messageStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }

            float scale = Screen.height / 1080f;
            _messageStyle.fontSize = Mathf.RoundToInt(20f * scale);
            float alpha = Mathf.Clamp01(_messageUntil - Time.unscaledTime);
            string text = fixedTime == FixedTime.Nuit
                ? "NUIT  (" + Mathf.RoundToInt(nightHour) + " h)   -   F3 : jour"
                : "JOUR  (" + Mathf.RoundToInt(dayHour) + " h)   -   F3 : nuit";
            Rect rect = new Rect(0f, 90f * scale, Screen.width, 30f * scale);
            _messageStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f * alpha);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _messageStyle);
            _messageStyle.normal.textColor = new Color(0.9f, 0.88f, 0.8f, alpha);
            GUI.Label(rect, text, _messageStyle);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                // Fixed Time change dans l'Inspector pendant le jeu : transition vers la nouvelle heure.
                if (fixedTime != FixedTime.Libre && !cycleRunning && _transitionStart < 0f
                    && !Mathf.Approximately(Mathf.Repeat(timeOfDay, 24f), Mathf.Repeat(FixedHour(), 24f)))
                {
                    StartTransition();
                }

                return;
            }

            SnapToFixed();
#if UNITY_EDITOR
            // Modifier des Transform pendant OnValidate est interdit : on attend la frame suivante.
            UnityEditor.EditorApplication.delayCall -= Apply;
            UnityEditor.EditorApplication.delayCall += Apply;
#endif
        }

        private void OnDestroy()
        {
            if (_skyInstance != null)
            {
                if (Application.isPlaying) Destroy(_skyInstance);
                else DestroyImmediate(_skyInstance);
            }
        }

        /// <summary>Applique immediatement l'heure courante a la scene.</summary>
        public void Apply()
        {
            if (this == null)
            {
                return; // appel differe apres destruction
            }

            float t = Mathf.Repeat(timeOfDay, 24f) / 24f;

            // Course du soleil : plan incline pour qu'il culmine a maxSunElevation a midi.
            Quaternion sunRotation = Quaternion.AngleAxis(sunYaw, Vector3.up)
                                   * Quaternion.AngleAxis(90f - maxSunElevation, Vector3.forward)
                                   * Quaternion.Euler(t * 360f - 90f, 0f, 0f);

            float sunValue = Mathf.Max(0f, sunIntensity.Evaluate(t));
            float moonValue = Mathf.Max(0f, moonIntensity.Evaluate(t));

            if (sun != null)
            {
                sun.transform.rotation = sunRotation;
                sun.intensity = sunValue;
                sun.color = sunColor.Evaluate(t);
                sun.enabled = sunValue > 0.001f;
            }

            if (moon != null)
            {
                moon.transform.rotation = sunRotation * Quaternion.Euler(180f, 0f, 0f); // a l'oppose du soleil
                moon.intensity = moonValue;
                moon.enabled = moonValue > 0.001f;
            }

            // Lumiere principale (ombres) : l'astre le plus fort.
            Light main = sunValue >= moonValue ? sun : moon;

            if (main != null)
            {
                RenderSettings.sun = main;
            }

            float flash = Mathf.Clamp01(LightningFlash);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(ambientColor.Evaluate(t), lightningAmbient, flash);
            RenderSettings.reflectionIntensity = Mathf.Clamp01(reflectionIntensity.Evaluate(t));

            if (controlFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = Color.Lerp(fogColor.Evaluate(t), lightningFog, flash);
                RenderSettings.fogDensity = Mathf.Max(0f, fogDensity.Evaluate(t) * Mathf.Max(0f, FogMultiplier));
            }

            ApplySky(t, sunValue);
        }

        private void ApplySky(float t, float sunValue)
        {
            if (skyMaterial == null)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                // En edition on ne touche pas au materiau (asset partage) : ciel de base.
                RenderSettings.skybox = skyMaterial;
                return;
            }

            if (_skyInstance == null)
            {
                _skyInstance = new Material(skyMaterial);
                _skyInstance.name = skyMaterial.name + " (cycle)";
                RenderSettings.skybox = _skyInstance;
            }

            if (_skyInstance.HasProperty("_Exposure")) _skyInstance.SetFloat("_Exposure", Mathf.Max(0f, skyExposure.Evaluate(t)));
            if (_skyInstance.HasProperty("_SkyTint")) _skyInstance.SetColor("_SkyTint", skyTint.Evaluate(t));
            if (_skyInstance.HasProperty("_AtmosphereThickness")) _skyInstance.SetFloat("_AtmosphereThickness", Mathf.Max(0f, skyAtmosphere.Evaluate(t)));
            if (_skyInstance.HasProperty("_GroundColor")) _skyInstance.SetColor("_GroundColor", skyGround.Evaluate(t));
            if (_skyInstance.HasProperty("_SunSize")) _skyInstance.SetFloat("_SunSize", sunValue > 0.05f ? sunDiscSize : 0f);
        }

        // ------------------------------------------------------------------
        // Valeurs par defaut
        // ------------------------------------------------------------------

        /// <summary>Courbe lisse a partir de couples (temps, valeur).</summary>
        private static AnimationCurve Curve(params float[] pairs)
        {
            Keyframe[] keys = new Keyframe[pairs.Length / 2];

            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = new Keyframe(pairs[i * 2], pairs[i * 2 + 1]);
            }

            AnimationCurve curve = new AnimationCurve(keys);

            for (int i = 0; i < keys.Length; i++)
            {
                curve.SmoothTangents(i, 0f);
            }

            return curve;
        }

        /// <summary>Degrade a partir de couples (temps, couleur), jusqu'a 8 cles.</summary>
        private static Gradient Colors(params object[] pairs)
        {
            GradientColorKey[] keys = new GradientColorKey[pairs.Length / 2];

            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = new GradientColorKey((Color)pairs[i * 2 + 1], (float)pairs[i * 2]);
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
