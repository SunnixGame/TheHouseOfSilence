using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Level
{
    /// <summary>
    /// Affiche le nom du lieu (ForestLocations) au centre de l'ecran quand le joueur
    /// entre dans une clairiere : fondu d'apparition, maintien, fondu de disparition.
    /// Les noms se modifient dans Assets/_Game/Settings/ForestLocations.asset.
    /// </summary>
    [DisallowMultipleComponent]
    public class ZoneTitleDisplay : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private bool showZoneTitles = true;

        [Tooltip("Affiche aussi le lieu de depart au lancement de la partie.")]
        [SerializeField] private bool showOnSpawn = true;

        [Tooltip("Delai avant le titre de depart (le temps que l'ecran apparaisse).")]
        [SerializeField, Min(0f)] private float spawnDelay = 1.5f;

        [Tooltip("Ignore les lieux decoches 'Show On Map'.")]
        [SerializeField] private bool respectShowOnMap = true;

        [Header("References")]
        [SerializeField] private ForestLocations locations;

        [Tooltip("Vide = objet tague Player.")]
        [SerializeField] private Transform player;

        [Tooltip("Vide = cherche l'InputReader du joueur. Le titre est cache en pause / carte ouverte.")]
        [SerializeField] private InputReader input;

        [Header("Detection")]
        [Tooltip("Frequence de verification (secondes).")]
        [SerializeField, Min(0.02f)] private float checkInterval = 0.2f;

        [Tooltip("On quitte la zone au-dela de rayon x cette valeur (evite le clignotement en bordure).")]
        [SerializeField, Range(1f, 2f)] private float exitMargin = 1.15f;

        [Tooltip("Coche : chaque lieu n'est annonce qu'une seule fois par partie.")]
        [SerializeField] private bool onlyOncePerZone = false;

        [Tooltip("Pour reannoncer un lieu, il faut s'en eloigner d'au moins cette distance (m) au-dela de son rayon...")]
        [SerializeField, Min(0f)] private float reshowDistance = 80f;

        [Tooltip("... et que ce delai (s) soit passe depuis la derniere annonce.")]
        [SerializeField, Min(0f)] private float reshowCooldown = 120f;

        [Header("Animation (secondes)")]
        [SerializeField, Min(0f)] private float fadeIn = 1.2f;
        [SerializeField, Min(0f)] private float hold = 3f;
        [SerializeField, Min(0f)] private float fadeOut = 1.8f;

        [Header("Style")]
        [SerializeField, Min(8)] private int titleSize = 46;
        [SerializeField] private Color titleColor = new Color(0.86f, 0.84f, 0.78f, 1f);
        [SerializeField] private Font font;

        [Tooltip("Petite ligne au-dessus du nom. Vide = pas de ligne.")]
        [SerializeField] private string subtitle = "";
        [SerializeField, Min(8)] private int subtitleSize = 16;
        [SerializeField] private Color subtitleColor = new Color(0.6f, 0.58f, 0.54f, 1f);

        [Tooltip("Position verticale du titre (0 = haut, 1 = bas).")]
        [SerializeField, Range(0f, 1f)] private float verticalPosition = 0.3f;

        [Tooltip("Ombre portee legere sous le texte.")]
        [SerializeField] private bool drawShadow = false;
        [SerializeField] private bool drawDivider = true;

        private ForestLocations.Location _current;
        // Lieux deja annonces -> heure de l'annonce. Retires quand on s'en est assez eloigne.
        private readonly System.Collections.Generic.Dictionary<string, float> _announced = new System.Collections.Generic.Dictionary<string, float>();
        private readonly System.Collections.Generic.List<string> _rearm = new System.Collections.Generic.List<string>();
        private float _nextCheck;
        private string _title;
        private float _titleStart = -1f;
        private float _pendingSpawnTime = -1f;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;

        /// <summary>Lieu ou se trouve le joueur (null hors clairiere).</summary>
        public ForestLocations.Location Current { get { return _current; } }

        /// <summary>Titre en cours d'affichage (null si aucun).</summary>
        public string ShowingTitle { get { return _titleStart >= 0f ? _title : null; } }

        private void Start()
        {
            ResolvePlayer();

            if (showOnSpawn)
            {
                _pendingSpawnTime = Time.time + spawnDelay;
            }
            else if (player != null && locations != null)
            {
                // Sans titre de depart : on part deja "dans" la zone, sans l'annoncer.
                _current = Find(player.position, null);
                if (_current != null) _announced[_current.clearingId] = Time.time;
            }
        }

        /// <summary>Change le personnage suivi (survivant ou demon) sans reannoncer le lieu actuel.</summary>
        public void SetPlayer(Transform newPlayer)
        {
            player = newPlayer;

            if (player != null && locations != null)
            {
                _current = Find(player.position, null);
                if (_current != null) _announced[_current.clearingId] = Time.time;
            }
        }

        private void ResolvePlayer()
        {
            if (player == null)
            {
                GameObject tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }

            if (input == null && player != null)
            {
                input = player.GetComponent<InputReader>();
            }
        }

        private void Update()
        {
            if (!showZoneTitles || locations == null)
            {
                return;
            }

            if (player == null)
            {
                ResolvePlayer();
                if (player == null) return;
            }

            if (Time.time < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.time + checkInterval;

            if (_pendingSpawnTime >= 0f && Time.time < _pendingSpawnTime)
            {
                return;
            }

            RearmFarZones(player.position);

            ForestLocations.Location zone = Find(player.position, _current);

            bool spawnTitle = _pendingSpawnTime >= 0f;
            _pendingSpawnTime = -1f;

            if (zone == _current && !spawnTitle)
            {
                return;
            }

            _current = zone;

            if (zone == null || _announced.ContainsKey(zone.clearingId))
            {
                return;
            }

            Show(zone.displayName);
            _announced[zone.clearingId] = Time.time;
        }

        /// <summary>
        /// Un lieu deja annonce redevient annoncable quand on en est loin (rayon +
        /// reshowDistance) depuis assez longtemps : les allers-retours en bordure de
        /// clairiere ne reaffichent jamais le nom.
        /// </summary>
        private void RearmFarZones(Vector3 position)
        {
            if (onlyOncePerZone || _announced.Count == 0)
            {
                return;
            }

            _rearm.Clear();

            foreach (System.Collections.Generic.KeyValuePair<string, float> pair in _announced)
            {
                ForestLocations.Location l = locations.Find(pair.Key);

                if (l == null || (Time.time - pair.Value >= reshowCooldown && Distance(position, l) > l.radius + reshowDistance))
                {
                    _rearm.Add(pair.Key);
                }
            }

            for (int i = 0; i < _rearm.Count; i++)
            {
                _announced.Remove(_rearm[i]);
            }
        }

        /// <summary>
        /// Zone contenant le point. La zone actuelle garde la priorite tant qu'on reste
        /// dans rayon x exitMargin.
        /// </summary>
        private ForestLocations.Location Find(Vector3 position, ForestLocations.Location current)
        {
            if (current != null && Usable(current) && Distance(position, current) <= current.radius * exitMargin)
            {
                return current;
            }

            ForestLocations.Location best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < locations.All.Count; i++)
            {
                ForestLocations.Location l = locations.All[i];

                if (!Usable(l))
                {
                    continue;
                }

                float d = Distance(position, l);

                if (d <= l.radius && d < bestDistance)
                {
                    best = l;
                    bestDistance = d;
                }
            }

            return best;
        }

        private bool Usable(ForestLocations.Location l)
        {
            return l != null && !string.IsNullOrEmpty(l.displayName) && (!respectShowOnMap || l.showOnMap);
        }

        private static float Distance(Vector3 position, ForestLocations.Location l)
        {
            float dx = position.x - l.position.x;
            float dz = position.z - l.position.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Affiche un titre quelconque (utilisable par d'autres scripts).</summary>
        public void Show(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return;
            }

            _title = title;
            _titleStart = Time.time;
        }

        private void OnGUI()
        {
            if (!showZoneTitles || _titleStart < 0f || string.IsNullOrEmpty(_title))
            {
                return;
            }

            if (input != null && !input.InputEnabled)
            {
                return; // pause ou carte ouverte
            }

            float t = Time.time - _titleStart;
            float total = fadeIn + hold + fadeOut;

            if (t > total)
            {
                _titleStart = -1f;
                return;
            }

            float alpha;

            if (t < fadeIn) alpha = fadeIn > 0f ? t / fadeIn : 1f;
            else if (t < fadeIn + hold) alpha = 1f;
            else alpha = fadeOut > 0f ? 1f - (t - fadeIn - hold) / fadeOut : 0f;

            alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(alpha));

            EnsureStyles();

            float scale = Screen.height / 1080f;
            _titleStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(titleSize * scale));
            _subtitleStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(subtitleSize * scale));

            // Le trait sous le nom s'etire pendant l'apparition.
            float reveal = fadeIn > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fadeIn)) : 1f;
            string text = _title;

            float centerY = Screen.height * verticalPosition;
            float titleHeight = _titleStyle.fontSize * 1.6f;
            Rect titleRect = new Rect(0f, centerY - titleHeight * 0.5f, Screen.width, titleHeight);

            Color previous = GUI.color;

            if (!string.IsNullOrEmpty(subtitle))
            {
                float subHeight = _subtitleStyle.fontSize * 1.6f;
                Rect subRect = new Rect(0f, titleRect.y - subHeight, Screen.width, subHeight);
                DrawText(subRect, subtitle.ToUpperInvariant(), _subtitleStyle, subtitleColor, alpha);
            }

            DrawText(titleRect, text, _titleStyle, titleColor, alpha);

            if (drawDivider)
            {
                float width = Mathf.Min(Screen.width * 0.5f, _titleStyle.CalcSize(new GUIContent(_title)).x * 1.2f) * reveal;
                Rect line = new Rect((Screen.width - width) * 0.5f, titleRect.yMax + 2f * scale, width, Mathf.Max(1f, scale));
                GUI.color = new Color(titleColor.r, titleColor.g, titleColor.b, titleColor.a * alpha * 0.6f);
                GUI.DrawTexture(line, Texture2D.whiteTexture);
            }

            GUI.color = previous;
        }

        private void DrawText(Rect rect, string text, GUIStyle style, Color color, float alpha)
        {
            if (drawShadow)
            {
                style.normal.textColor = new Color(0f, 0f, 0f, 0.75f * alpha * color.a);
                Rect shadow = rect;
                shadow.x += 2f;
                shadow.y += 2f;
                GUI.Label(shadow, text, style);
            }

            style.normal.textColor = new Color(color.r, color.g, color.b, color.a * alpha);
            GUI.Label(rect, text, style);
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.alignment = TextAnchor.MiddleCenter;
            _titleStyle.fontStyle = FontStyle.Normal;
            _titleStyle.wordWrap = false;
            _titleStyle.richText = false;
            if (font != null) _titleStyle.font = font;

            _subtitleStyle = new GUIStyle(_titleStyle);
            _subtitleStyle.fontStyle = FontStyle.Normal;
        }
    }
}
