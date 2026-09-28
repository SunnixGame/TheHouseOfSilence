using HouseOfSilence.Lights;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HouseOfSilence.Network
{
    /// <summary>Meteo partagee par tous les joueurs.</summary>
    public enum Weather : byte
    {
        Clear = 0,
        Mist = 1,
        Storm = 2
    }

    /// <summary>
    /// Heure, jour / nuit, brouillard et meteo identiques chez tous les joueurs (partie en
    /// ligne) : l'hote fait avancer l'horloge et decide de la meteo et des eclairs ; il les
    /// envoie plusieurs fois par seconde, les clients prolongent entre deux messages et se
    /// recalent. DayNightCycle (existant) fait le rendu : il ne calcule plus rien lui-meme
    /// pendant la partie (ExternallyDriven).
    ///
    /// La partie commence a startHour ; a dawnHour (l'aube), les survivants ont gagne.
    /// Effets locaux non concernes : VHS, HUD, cameras.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkTimeOfDay : MonoBehaviour
    {
        private const string TimeMessage = "HOS_Time";
        private const string LightningMessage = "HOS_Lightning";

        [Header("Horloge")]
        [SerializeField, Range(0f, 24f)] private float startHour = 22f;
        [SerializeField, Range(0f, 24f)] private float dawnHour = 6f;
        [Tooltip("Duree reelle de la nuit (de startHour a dawnHour), en minutes.")]
        [SerializeField, Min(1f)] private float nightRealMinutes = 15f;
        [SerializeField, Range(1f, 20f)] private float sendsPerSecond = 4f;

        [Header("Meteo")]
        [Tooltip("Intervalle entre deux changements de meteo (minutes reelles).")]
        [SerializeField] private Vector2 weatherChangeMinutes = new Vector2(2f, 4f);
        [SerializeField, Min(1f)] private float mistFog = 2.2f;
        [SerializeField, Min(1f)] private float stormFog = 1.5f;
        [Tooltip("Intervalle entre deux eclairs pendant l'orage (s).")]
        [SerializeField] private Vector2 lightningInterval = new Vector2(6f, 18f);
        [SerializeField, Range(0f, 1f)] private float thunderVolume = 0.8f;

        private NetworkGameManager _game;
        private DayNightCycle _cycle;
        private AudioSource _audio;

        private bool _active;
        private float _time;
        private float _hoursPerSecond;
        private float _hoursPassed;
        private float _nightHours;
        private Weather _weather;
        private float _fogMultiplier = 1f;
        private float _flash;
        private float _nextSend;
        private float _nextWeather;
        private float _nextLightning;
        private float _thunderAt = -1f;
        private int _thunderClip;

        /// <summary>Hote : l'aube est arrivee (victoire des survivants).</summary>
        public bool DawnReached { get; private set; }

        public bool Active { get { return _active; } }
        public float Hour { get { return _time; } }
        public Weather CurrentWeather { get { return _weather; } }

        private void Awake()
        {
            _game = GetComponent<NetworkGameManager>();
            _game.Register(TimeMessage, OnTimeMessage);
            _game.Register(LightningMessage, OnLightningMessage);

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        // ------------------------------------------------------------------

        /// <summary>Hote : nuit qui commence, ciel clair.</summary>
        public void ServerStartRound()
        {
            _nightHours = Mathf.Repeat(dawnHour - startHour, 24f);
            if (_nightHours <= 0.01f) _nightHours = 24f;

            _time = startHour;
            _hoursPassed = 0f;
            _hoursPerSecond = _nightHours / (nightRealMinutes * 60f);
            _weather = Weather.Clear;
            DawnReached = false;
            _nextWeather = Time.unscaledTime + Random.Range(weatherChangeMinutes.x, weatherChangeMinutes.y) * 60f;
            _nextLightning = Time.unscaledTime + Random.Range(lightningInterval.x, lightningInterval.y);
            _nextSend = 0f;
        }

        /// <summary>Tout le monde, au depart de la partie : l'heure vient du reseau.</summary>
        public void Begin()
        {
            _active = true;
            if (!_game.IsHost) _time = startHour; // en attendant le premier message
            FindCycle();
        }

        public void Stop()
        {
            _active = false;
            _flash = 0f;
            _thunderAt = -1f;

            if (_cycle != null)
            {
                _cycle.ExternallyDriven = false;
                _cycle.FogMultiplier = 1f;
                _cycle.LightningFlash = 0f;
            }
        }

        private void FindCycle()
        {
            if (_cycle == null) _cycle = FindAnyObjectByType<DayNightCycle>();
        }

        private void Update()
        {
            if (!_active) return;
            FindCycle();

            float dt = Time.unscaledDeltaTime;

            // L'horloge avance chez tous ; les clients se recalent sur l'hote a chaque message.
            if (!DawnReached || !_game.IsHost)
            {
                _time = Mathf.Repeat(_time + _hoursPerSecond * dt, 24f);
                _hoursPassed += _hoursPerSecond * dt;
            }

            if (_game.IsHost) UpdateHost();

            float targetFog = _weather == Weather.Mist ? mistFog : _weather == Weather.Storm ? stormFog : 1f;
            _fogMultiplier = Mathf.MoveTowards(_fogMultiplier, targetFog, dt * 0.1f);
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 2.5f);

            if (_thunderAt >= 0f && Time.unscaledTime >= _thunderAt)
            {
                _thunderAt = -1f;
                AudioClip[] clips = _game.ThunderClips;
                if (clips != null && clips.Length > 0 && clips[_thunderClip % clips.Length] != null)
                {
                    _audio.PlayOneShot(clips[_thunderClip % clips.Length], thunderVolume);
                }
            }

            if (_cycle != null)
            {
                _cycle.ExternallyDriven = true;
                _cycle.FogMultiplier = _fogMultiplier;
                _cycle.LightningFlash = _flash;
                _cycle.TimeOfDay = _time; // applique soleil, lune, ambiance, brouillard, ciel
            }
        }

        private void UpdateHost()
        {
            if (!DawnReached && _hoursPassed >= _nightHours)
            {
                DawnReached = true;
                _time = dawnHour;
            }

            if (Time.unscaledTime >= _nextWeather)
            {
                _nextWeather = Time.unscaledTime + Random.Range(weatherChangeMinutes.x, weatherChangeMinutes.y) * 60f;
                Weather next = (Weather)Random.Range(0, 3);
                if (next == _weather) next = (Weather)(((int)next + 1) % 3);
                _weather = next;
                _nextSend = 0f;
            }

            if (_weather == Weather.Storm && Time.unscaledTime >= _nextLightning)
            {
                _nextLightning = Time.unscaledTime + Random.Range(lightningInterval.x, lightningInterval.y);
                SendLightning(Random.Range(0.5f, 1f), Random.Range(0.3f, 2.5f), Random.Range(0, 16));
            }

            if (Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + 1f / sendsPerSecond;
                SendTime();
            }
        }

        // ------------------------------------------------------------------
        // Messages
        // ------------------------------------------------------------------

        private void SendTime()
        {
            using (FastBufferWriter w = new FastBufferWriter(32, Allocator.Temp))
            {
                w.WriteValueSafe(_time);
                w.WriteValueSafe(DawnReached ? 0f : _hoursPerSecond);
                w.WriteValueSafe((byte)_weather);
                _game.SendToOthers(_game.LocalId, TimeMessage, w, NetworkDelivery.UnreliableSequenced);
            }
        }

        private void OnTimeMessage(ulong sender, FastBufferReader r)
        {
            float time, rate;
            byte weather;
            r.ReadValueSafe(out time);
            r.ReadValueSafe(out rate);
            r.ReadValueSafe(out weather);

            if (_game.IsHost) return;

            // Recalage : saut si l'ecart est grand, sinon on rattrape en douceur.
            float diff = Mathf.DeltaAngle(_time * 15f, time * 15f) / 15f; // ecart en heures, sur 24 h
            _time = Mathf.Abs(diff) > 0.25f ? time : Mathf.Repeat(_time + diff * 0.5f, 24f);
            _hoursPerSecond = rate;
            _weather = weather <= (byte)Weather.Storm ? (Weather)weather : Weather.Clear;
        }

        /// <summary>Hote : eclair chez tous (tonnerre apres 'delay' secondes, selon la distance).</summary>
        private void SendLightning(float intensity, float delay, int clip)
        {
            using (FastBufferWriter w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe(intensity);
                w.WriteValueSafe(delay);
                w.WriteValueSafe((byte)clip);
                _game.SendToAll(LightningMessage, w);
            }
        }

        private void OnLightningMessage(ulong sender, FastBufferReader r)
        {
            float intensity, delay;
            byte clip;
            r.ReadValueSafe(out intensity);
            r.ReadValueSafe(out delay);
            r.ReadValueSafe(out clip);

            if (!_active) return;

            _flash = Mathf.Max(_flash, intensity);
            _thunderClip = clip;
            _thunderAt = Time.unscaledTime + delay;
        }

        /// <summary>"23:45".</summary>
        public string Clock()
        {
            int minutes = Mathf.FloorToInt(Mathf.Repeat(_time, 24f) * 60f);
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        public string WeatherName()
        {
            return _weather == Weather.Mist ? "Brume" : _weather == Weather.Storm ? "Orage" : "Ciel degage";
        }
    }
}
