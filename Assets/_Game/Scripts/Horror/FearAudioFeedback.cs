using HouseOfSilence.Core;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Horror
{
    /// <summary>
    /// Battements de coeur et respiration pilotes par la peur.
    ///
    /// Les clips sont facultatifs : sans eux, rien n'est joue et rien ne
    /// plante. La Phase 14 (HorrorAudioManager) branchera les vrais fichiers ;
    /// les emplacements sont deja la.
    ///
    /// Les deux sources sont en 2D (spatialBlend = 0) : c'est le corps du
    /// joueur qu'on entend, pas un objet dans la piece.
    /// </summary>
    [DisallowMultipleComponent]
    public class FearAudioFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCharacter player;

        [Header("Battements de coeur")]
        [SerializeField] private AudioClip heartbeatClip;
        [SerializeField, Range(0f, 1f)] private float heartbeatStart = 0.35f;
        [SerializeField, Range(0f, 1f)] private float heartbeatMaxVolume = 0.9f;
        [SerializeField, Range(0.5f, 1f)] private float heartbeatMinPitch = 0.85f;
        [SerializeField, Range(1f, 2f)] private float heartbeatMaxPitch = 1.4f;

        [Header("Respiration")]
        [SerializeField] private AudioClip breathingClip;
        [SerializeField, Range(0f, 1f)] private float breathingStart = 0.55f;
        [SerializeField, Range(0f, 1f)] private float breathingMaxVolume = 0.7f;

        [Header("Lissage")]
        [SerializeField, Min(0.1f)] private float smoothing = 2.5f;

        private AudioSource _heartbeat;
        private AudioSource _breathing;
        private float _targetFear;
        private float _currentFear;

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponent<PlayerCharacter>();
            }

            _heartbeat = CreateSource("FearHeartbeat", heartbeatClip);
            _breathing = CreateSource("FearBreathing", breathingClip);
        }

        private AudioSource CreateSource(string sourceName, AudioClip clip)
        {
            GameObject host = new GameObject(sourceName);
            host.transform.SetParent(transform, false);

            AudioSource source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.ignoreListenerPause = false;

            return source;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<FearChangedEvent>(OnFearChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<FearChangedEvent>(OnFearChanged);
            Stop(_heartbeat);
            Stop(_breathing);
        }

        private void OnFearChanged(FearChangedEvent evt)
        {
            if (player != null && evt.Player != player)
            {
                return;
            }

            _targetFear = evt.Normalized;
        }

        private void Update()
        {
            _currentFear = Mathf.Lerp(_currentFear, _targetFear, 1f - Mathf.Exp(-smoothing * Time.deltaTime));

            UpdateLoop(_heartbeat, heartbeatStart, heartbeatMaxVolume, heartbeatMinPitch, heartbeatMaxPitch);
            UpdateLoop(_breathing, breathingStart, breathingMaxVolume, 1f, 1f);
        }

        private void UpdateLoop(AudioSource source, float start, float maxVolume, float minPitch, float maxPitch)
        {
            if (source == null || source.clip == null)
            {
                return;
            }

            float t = Mathf.InverseLerp(start, 1f, _currentFear);

            if (t <= 0.001f)
            {
                Stop(source);
                return;
            }

            source.volume = t * maxVolume;
            source.pitch = Mathf.Lerp(minPitch, maxPitch, t);

            if (!source.isPlaying)
            {
                source.Play();
            }
        }

        private static void Stop(AudioSource source)
        {
            if (source != null && source.isPlaying)
            {
                source.Stop();
            }
        }

        /// <summary>Permet a l'audio manager (Phase 14) d'injecter les clips a chaud.</summary>
        public void SetClips(AudioClip heartbeat, AudioClip breathing)
        {
            heartbeatClip = heartbeat;
            breathingClip = breathing;

            if (_heartbeat != null)
            {
                _heartbeat.clip = heartbeat;
            }

            if (_breathing != null)
            {
                _breathing.clip = breathing;
            }
        }
    }
}
