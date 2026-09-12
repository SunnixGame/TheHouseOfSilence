using System.Collections.Generic;
using UnityEngine;

namespace HouseOfSilence.Lights
{
    /// <summary>
    /// Estime la quantite de lumiere qui atteint un point (les yeux du joueur).
    /// Resultat : LightLevel, de 0 (noir total) a 1 (bien eclaire).
    ///
    /// Methode : somme des contributions des Light actives a portee, avec
    /// attenuation par la distance, cone des spots, et test d'occlusion par
    /// raycast (une lampe derriere un mur n'eclaire pas).
    /// Echantillonnage a 5 Hz : cout negligeable.
    ///
    /// Les lampes de la scene sont recensees toutes les 2 s. La Phase 8
    /// (LightController) appellera MarkLightsDirty() quand une lampe apparait
    /// ou disparait pour forcer un recensement immediat.
    ///
    /// Les sources portees (lampe torche, briquet) s'ajoutent via
    /// SetExternalLight() : elles ne dependent pas de la geometrie.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightLevelSensor : MonoBehaviour
    {
        [Header("Echantillonnage")]
        [Tooltip("Point de mesure. Vide = la camera enfant, sinon ce transform.")]
        [SerializeField] private Transform samplePoint;

        [SerializeField, Range(0.05f, 1f)] private float sampleInterval = 0.2f;
        [SerializeField, Range(0.5f, 10f)] private float lightsRefreshInterval = 2f;

        [Header("Calibration")]
        [Tooltip("Somme d'intensite consideree comme 'plein jour'. Au dela, LightLevel = 1.")]
        [SerializeField, Min(0.1f)] private float fullBrightness = 1.2f;

        [Tooltip("Poids de la lumiere ambiante (RenderSettings). 0 pour l'ignorer.")]
        [SerializeField, Range(0f, 1f)] private float ambientWeight = 0.5f;

        [Tooltip("Poids d'une Directional Light (soleil / lune). 1 = on se fie a son intensite. Un plafond la bloque de toute facon.")]
        [SerializeField, Range(0f, 1f)] private float directionalWeight = 1f;

        [Header("Occlusion")]
        [Tooltip("Verifie qu'aucun mur ne se trouve entre la lampe et le point de mesure.")]
        [SerializeField] private bool checkOcclusion = true;

        [SerializeField] private LayerMask occlusionMask = ~0;

        [Header("Debug")]
        [SerializeField] private bool drawDebugRays = false;

        // ------------------------------------------------------------------

        private static int s_DirtyCounter;

        private readonly List<Light> _lights = new List<Light>(32);
        private readonly Dictionary<string, float> _externalLights = new Dictionary<string, float>(4);

        private float _sampleTimer;
        private float _refreshTimer;
        private int _seenDirtyCounter;
        private float _lightLevel = 1f;
        private float _sceneContribution;
        private float _externalContribution;

        /// <summary>Niveau de lumiere, de 0 (noir) a 1 (eclaire).</summary>
        public float LightLevel { get { return _lightLevel; } }

        /// <summary>Vrai si le joueur est dans le noir ou presque.</summary>
        public bool IsInDarkness { get { return _lightLevel < 0.2f; } }

        /// <summary>Force un recensement des lampes au prochain echantillon (Phase 8).</summary>
        public static void MarkLightsDirty()
        {
            s_DirtyCounter++;
        }

        /// <summary>
        /// Ajoute une source de lumiere portee (lampe torche allumee, briquet).
        /// amount est exprime dans la meme unite que fullBrightness. 0 = retire.
        /// </summary>
        public void SetExternalLight(string id, float amount)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (amount <= 0f)
            {
                _externalLights.Remove(id);
            }
            else
            {
                _externalLights[id] = amount;
            }

            _externalContribution = 0f;

            foreach (KeyValuePair<string, float> pair in _externalLights)
            {
                _externalContribution += pair.Value;
            }
        }

        // ------------------------------------------------------------------

        private void Awake()
        {
            if (samplePoint == null)
            {
                Camera camera = GetComponentInChildren<Camera>(true);
                samplePoint = camera != null ? camera.transform : transform;
            }
        }

        private void OnEnable()
        {
            RefreshLights();
            Sample();
        }

        private void Update()
        {
            _refreshTimer -= Time.deltaTime;

            if (_refreshTimer <= 0f || _seenDirtyCounter != s_DirtyCounter)
            {
                RefreshLights();
            }

            _sampleTimer -= Time.deltaTime;

            if (_sampleTimer <= 0f)
            {
                Sample();
            }
        }

        private void RefreshLights()
        {
            _refreshTimer = lightsRefreshInterval;
            _seenDirtyCounter = s_DirtyCounter;

            _lights.Clear();

            Light[] found = FindObjectsByType<Light>(FindObjectsInactive.Exclude);

            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                {
                    _lights.Add(found[i]);
                }
            }
        }

        private void Sample()
        {
            _sampleTimer = sampleInterval;

            Vector3 point = samplePoint != null ? samplePoint.position : transform.position;
            float total = 0f;

            if (ambientWeight > 0f)
            {
                total += RenderSettings.ambientLight.grayscale * RenderSettings.ambientIntensity * ambientWeight;
            }

            for (int i = 0; i < _lights.Count; i++)
            {
                Light light = _lights[i];

                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy || light.intensity <= 0f)
                {
                    continue;
                }

                total += Contribution(light, point);
            }

            _sceneContribution = total;
            _lightLevel = Mathf.Clamp01((_sceneContribution + _externalContribution) / fullBrightness);
        }

        private float Contribution(Light light, Vector3 point)
        {
            if (light.type == LightType.Directional)
            {
                // Un plafond ou un sol au dessus du joueur coupe la lumiere du ciel :
                // c'est ce qui rend un sous-sol sombre.
                if (checkOcclusion && IsSkyOccluded(point, -light.transform.forward))
                {
                    return 0f;
                }

                return light.intensity * directionalWeight;
            }

            if (light.type != LightType.Point && light.type != LightType.Spot)
            {
                return 0f;
            }

            Vector3 lightPosition = light.transform.position;
            Vector3 toPoint = point - lightPosition;
            float distance = toPoint.magnitude;

            if (distance >= light.range || distance < 0.001f)
            {
                return 0f;
            }

            float normalized = distance / light.range;
            float attenuation = (1f - normalized) * (1f - normalized);

            if (light.type == LightType.Spot)
            {
                float halfAngle = light.spotAngle * 0.5f;
                float angle = Vector3.Angle(light.transform.forward, toPoint);

                if (angle > halfAngle)
                {
                    return 0f;
                }

                // Adoucit le bord du cone.
                float edge = Mathf.InverseLerp(halfAngle, halfAngle * 0.7f, angle);
                attenuation *= Mathf.Clamp01(edge);
            }

            if (checkOcclusion && IsOccluded(lightPosition, point, distance))
            {
                return 0f;
            }

            return light.intensity * attenuation;
        }

        private bool IsSkyOccluded(Vector3 point, Vector3 towardsLight)
        {
            const float skyDistance = 80f;

            RaycastHit hit;

            if (!Physics.Raycast(point, towardsLight, out hit, skyDistance, occlusionMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
            {
                return false;
            }

            if (drawDebugRays)
            {
                Debug.DrawLine(point, hit.point, Color.magenta, sampleInterval);
            }

            return true;
        }

        private bool IsOccluded(Vector3 from, Vector3 to, float distance)
        {
            Vector3 direction = (to - from) / distance;

            RaycastHit hit;

            // On s'arrete un peu avant le point de mesure pour ne pas toucher le joueur.
            if (!Physics.Raycast(from, direction, out hit, distance - 0.35f, occlusionMask, QueryTriggerInteraction.Ignore))
            {
                if (drawDebugRays)
                {
                    Debug.DrawLine(from, to, Color.yellow, sampleInterval);
                }

                return false;
            }

            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
            {
                return false;
            }

            if (drawDebugRays)
            {
                Debug.DrawLine(from, hit.point, Color.red, sampleInterval);
            }

            return true;
        }
    }
}
