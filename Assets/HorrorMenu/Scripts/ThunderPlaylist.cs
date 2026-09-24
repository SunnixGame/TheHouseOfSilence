using UnityEngine;

namespace HouseOfSilence.HorrorMenu
{
    /// <summary>
    /// Joue une liste de sons de tonnerre a la suite, sans blanc entre eux,
    /// puis recommence au debut, a l'infini. Vit dans la scene du menu :
    /// le son s'arrete de lui-meme quand on quitte le menu (scene dechargee).
    ///
    /// Deux AudioSource en alternance + PlayScheduled : l'enchainement est
    /// cale sur l'horloge audio (dspTime), donc sans coupure ni decalage.
    /// A chaque debut de son, un eclair est declenche si LightningStorm est renseigne.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThunderPlaylist : MonoBehaviour
    {
        [Tooltip("Sons joues dans l'ordre, puis en boucle.")]
        [SerializeField] private AudioClip[] clips;

        [Tooltip("Silence optionnel entre deux sons, en secondes.")]
        [SerializeField, Min(0f)] private float gapBetweenClips = 0f;

        [SerializeField, Range(0f, 1f)] private float volume = 0.9f;

        [Tooltip("Eclairs synchronises sur le debut de chaque son (optionnel).")]
        [SerializeField] private LightningStorm lightning;

        [Tooltip("Decalage de l'eclair par rapport au debut du son (negatif = avant).")]
        [SerializeField] private float flashOffset = 0f;

        private const double ScheduleAhead = 1.0;

        private AudioSource[] _sources;
        private int _nextClip;
        private int _nextSource;
        private double _nextStartDsp;
        private double _pendingFlashDsp = -1.0;

        private void Awake()
        {
            _sources = new AudioSource[2];
            for (int i = 0; i < _sources.Length; i++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.volume = volume;
                _sources[i] = source;
            }
        }

        private void Start()
        {
            if (clips == null || clips.Length == 0)
            {
                Debug.LogWarning("[ThunderPlaylist] Aucun son assigne.", this);
                enabled = false;
                return;
            }

            foreach (AudioClip clip in clips)
            {
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                {
                    clip.LoadAudioData();
                }
            }

            _nextStartDsp = AudioSettings.dspTime + 0.2;
            ScheduleNext();
        }

        private void Update()
        {
            double now = AudioSettings.dspTime;

            if (now >= _nextStartDsp - ScheduleAhead)
            {
                ScheduleNext();
            }

            if (_pendingFlashDsp >= 0.0 && now >= _pendingFlashDsp)
            {
                _pendingFlashDsp = -1.0;
                if (lightning != null)
                {
                    lightning.Strike();
                }
            }
        }

        private void ScheduleNext()
        {
            AudioClip clip = null;

            // Saute les entrees vides sans boucler indefiniment.
            for (int tries = 0; tries < clips.Length && clip == null; tries++)
            {
                clip = clips[_nextClip];
                _nextClip = (_nextClip + 1) % clips.Length;
            }

            if (clip == null)
            {
                enabled = false;
                return;
            }

            AudioSource source = _sources[_nextSource];
            _nextSource = (_nextSource + 1) % _sources.Length;

            source.clip = clip;
            source.volume = volume;
            source.PlayScheduled(_nextStartDsp);

            // Le flash precedent a forcement eu lieu (un son dure > ScheduleAhead).
            _pendingFlashDsp = _nextStartDsp + flashOffset;

            double length = (double)clip.samples / clip.frequency;
            _nextStartDsp += length + gapBetweenClips;
        }

        private void OnDisable()
        {
            if (_sources == null)
            {
                return;
            }

            foreach (AudioSource source in _sources)
            {
                if (source != null)
                {
                    source.Stop();
                }
            }
        }
    }
}
