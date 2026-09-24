using System.Collections;
using UnityEngine;

namespace HouseOfSilence.HorrorMenu
{
    /// <summary>
    /// Eclairs d'orage : flash d'une lumiere directionnelle, brouillard et
    /// lumiere ambiante qui s'illuminent, et arc electrique (LineRenderer)
    /// dessine au loin dans le brouillard. Strike() est appele par
    /// ThunderPlaylist au debut de chaque tonnerre ; des eclairs lointains
    /// plus faibles peuvent aussi tomber au hasard entre deux.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightningStorm : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Light flashLight;
        [SerializeField] private LineRenderer bolt;
        [Tooltip("Centre de la zone ou l'arc peut apparaitre (derriere la maison).")]
        [SerializeField] private Transform boltArea;

        [Header("Flash")]
        [SerializeField, Min(0f)] private float peakIntensity = 5f;
        [SerializeField] private Color fogFlashColor = new Color(0.45f, 0.5f, 0.62f);
        [SerializeField] private Color ambientFlashColor = new Color(0.6f, 0.66f, 0.8f);

        [Header("Arc electrique")]
        [SerializeField] private Vector2 boltAreaSize = new Vector2(40f, 12f);
        [SerializeField] private float boltHeight = 45f;
        [SerializeField, Range(4, 40)] private int boltSegments = 18;

        [Header("Eclairs lointains aleatoires")]
        [SerializeField] private bool randomDistantFlashes = true;
        [SerializeField] private Vector2 distantInterval = new Vector2(7f, 16f);

        private Color _baseFog;
        private Color _baseAmbientSky;
        private Color _baseAmbientEquator;
        private Coroutine _flash;
        private float _nextDistant;

        private void Awake()
        {
            _baseFog = RenderSettings.fogColor;
            _baseAmbientSky = RenderSettings.ambientSkyColor;
            _baseAmbientEquator = RenderSettings.ambientEquatorColor;

            if (flashLight != null)
            {
                flashLight.intensity = 0f;
            }

            if (bolt != null)
            {
                bolt.enabled = false;
            }

            ScheduleDistant();
        }

        private void Update()
        {
            if (randomDistantFlashes && _flash == null && Time.time >= _nextDistant)
            {
                _flash = StartCoroutine(FlashRoutine(Random.Range(0.2f, 0.4f), false));
                ScheduleDistant();
            }
        }

        private void OnDisable()
        {
            RestoreBase();
        }

        /// <summary>Declenche a chaque eclair principal (pas pour les eclairs lointains).</summary>
        public event System.Action Struck;

        /// <summary>Eclair principal (avec arc visible).</summary>
        public void Strike()
        {
            if (Struck != null)
            {
                Struck.Invoke();
            }

            if (_flash != null)
            {
                StopCoroutine(_flash);
            }

            _flash = StartCoroutine(FlashRoutine(1f, true));
            ScheduleDistant();
        }

        private void ScheduleDistant()
        {
            _nextDistant = Time.time + Random.Range(distantInterval.x, distantInterval.y);
        }

        private IEnumerator FlashRoutine(float strength, bool showBolt)
        {
            if (showBolt)
            {
                BuildBolt();
            }

            // 2 a 4 impulsions rapprochees, comme un vrai eclair.
            int pulses = Random.Range(2, 5);
            for (int i = 0; i < pulses; i++)
            {
                float k = strength * (i == 0 ? 1f : Random.Range(0.4f, 0.9f));
                Apply(k, showBolt);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.09f));
                Apply(k * 0.15f, false);
                yield return new WaitForSeconds(Random.Range(0.05f, 0.14f));
            }

            // Decroissance douce.
            float t = 0f;
            const float fade = 0.6f;
            while (t < fade)
            {
                t += Time.deltaTime;
                Apply(strength * 0.25f * (1f - t / fade), false);
                yield return null;
            }

            RestoreBase();
            _flash = null;
        }

        private void Apply(float k, bool boltVisible)
        {
            if (flashLight != null)
            {
                flashLight.intensity = peakIntensity * k;
            }

            float c = Mathf.Clamp01(k);
            RenderSettings.fogColor = Color.Lerp(_baseFog, fogFlashColor, c);
            RenderSettings.ambientSkyColor = Color.Lerp(_baseAmbientSky, ambientFlashColor, c);
            RenderSettings.ambientEquatorColor = Color.Lerp(_baseAmbientEquator, ambientFlashColor * 0.6f, c);

            if (Camera.main != null && Camera.main.clearFlags == CameraClearFlags.SolidColor)
            {
                Camera.main.backgroundColor = RenderSettings.fogColor;
            }

            if (bolt != null)
            {
                bolt.enabled = boltVisible;
            }
        }

        private void RestoreBase()
        {
            if (flashLight != null)
            {
                flashLight.intensity = 0f;
            }

            RenderSettings.fogColor = _baseFog;
            RenderSettings.ambientSkyColor = _baseAmbientSky;
            RenderSettings.ambientEquatorColor = _baseAmbientEquator;

            if (Camera.main != null && Camera.main.clearFlags == CameraClearFlags.SolidColor)
            {
                Camera.main.backgroundColor = _baseFog;
            }

            if (bolt != null)
            {
                bolt.enabled = false;
            }
        }

        private void BuildBolt()
        {
            if (bolt == null)
            {
                return;
            }

            Vector3 center = boltArea != null ? boltArea.position : transform.position;
            Vector3 ground = center + new Vector3(
                Random.Range(-boltAreaSize.x, boltAreaSize.x) * 0.5f,
                0f,
                Random.Range(-boltAreaSize.y, boltAreaSize.y) * 0.5f);
            Vector3 top = ground + new Vector3(Random.Range(-8f, 8f), boltHeight, Random.Range(-4f, 4f));

            bolt.positionCount = boltSegments + 1;
            for (int i = 0; i <= boltSegments; i++)
            {
                float t = (float)i / boltSegments;
                Vector3 p = Vector3.Lerp(top, ground, t);
                if (i > 0 && i < boltSegments)
                {
                    float jitter = Mathf.Sin(t * Mathf.PI) * 3.5f;
                    p += new Vector3(Random.Range(-jitter, jitter), Random.Range(-0.5f, 0.5f), Random.Range(-jitter, jitter) * 0.4f);
                }

                bolt.SetPosition(i, p);
            }
        }
    }
}
