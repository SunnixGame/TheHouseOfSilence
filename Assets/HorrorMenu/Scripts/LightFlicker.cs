using UnityEngine;

namespace HouseOfSilence.HorrorMenu
{
    /// <summary>
    /// Fait vaciller une lumiere (bougie, lanterne) avec un bruit de Perlin,
    /// plus quelques micro-coupures aleatoires pour l'ambiance.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [DisallowMultipleComponent]
    public class LightFlicker : MonoBehaviour
    {
        [Tooltip("Variation d'intensite autour de la valeur de depart (0.3 = +/-30%).")]
        [SerializeField, Range(0f, 1f)] private float amplitude = 0.3f;

        [Tooltip("Vitesse du vacillement.")]
        [SerializeField, Min(0f)] private float speed = 6f;

        [Tooltip("Probabilite par seconde d'une breve baisse de lumiere.")]
        [SerializeField, Range(0f, 1f)] private float dropChance = 0.08f;

        private Light _light;
        private float _baseIntensity;
        private float _seed;
        private float _dropTimer;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _baseIntensity = _light.intensity;
            _seed = Random.value * 100f;
        }

        private void Update()
        {
            float noise = Mathf.PerlinNoise(_seed, Time.time * speed) * 2f - 1f;
            float intensity = _baseIntensity * (1f + noise * amplitude);

            if (_dropTimer > 0f)
            {
                _dropTimer -= Time.deltaTime;
                intensity *= 0.35f;
            }
            else if (Random.value < dropChance * Time.deltaTime)
            {
                _dropTimer = Random.Range(0.04f, 0.15f);
            }

            _light.intensity = intensity;
        }
    }
}
