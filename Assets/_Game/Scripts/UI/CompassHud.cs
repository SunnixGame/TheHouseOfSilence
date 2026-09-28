using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.UI
{
    /// <summary>
    /// Boussole en bandeau en haut de l'ecran (style jeu d'exploration) : points
    /// cardinaux, graduations et cap en degres. Le Nord est l'axe +Z du monde (reglable avec North Offset).
    /// </summary>
    [DisallowMultipleComponent]
    public class CompassHud : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private bool showCompass = true;

        [Header("References")]
        [Tooltip("Vide = camera principale.")]
        [SerializeField] private Transform view;

        [Tooltip("Vide = cherche l'InputReader du joueur. Cachee en pause / carte ouverte.")]
        [SerializeField] private InputReader input;

        [Header("Orientation")]
        [Tooltip("Angle (degres) ajoute au cap : 0 = le Nord est +Z.")]
        [SerializeField, Range(-180f, 180f)] private float northOffset = 0f;

        [Tooltip("Angle de vue couvert par la bande (degres).")]
        [SerializeField, Range(60f, 360f)] private float visibleAngle = 160f;

        [Header("Position et taille")]
        [Tooltip("Largeur en fraction de l'ecran.")]
        [SerializeField, Range(0.1f, 1f)] private float widthFraction = 0.42f;
        [SerializeField, Min(10f)] private float height = 38f;
        [SerializeField, Min(0f)] private float topMargin = 18f;

        [Header("Graduations")]
        [SerializeField, Range(1f, 45f)] private float minorTickStep = 15f;
        [SerializeField] private bool showDegrees = true;
        [SerializeField] private bool showIntercardinals = true;

        [Header("Couleurs")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.35f);
        [SerializeField] private Color tickColor = new Color(0.82f, 0.8f, 0.74f, 0.55f);
        [SerializeField] private Color cardinalColor = new Color(0.9f, 0.88f, 0.82f, 0.95f);
        [SerializeField] private Color northColor = new Color(0.85f, 0.25f, 0.2f, 1f);
        [SerializeField] private Color centerColor = new Color(1f, 1f, 1f, 0.85f);

        [Tooltip("Les bords de la bande s'estompent.")]
        [SerializeField, Range(0f, 0.5f)] private float edgeFade = 0.22f;

        private static readonly string[] Cardinals = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };

        private Texture2D _background;
        private float _backgroundFade = -1f;
        private GUIStyle _cardinalStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _degreeStyle;

        /// <summary>Cap actuel en degres (0 = Nord, 90 = Est).</summary>
        public float Heading { get { return view != null ? Normalize(view.eulerAngles.y + northOffset) : 0f; } }

        /// <summary>Change la vue suivie (camera du survivant ou du demon).</summary>
        public void SetView(Transform newView)
        {
            view = newView;
        }

        private void ResolveReferences()
        {
            if (view == null && Camera.main != null)
            {
                view = Camera.main.transform;
            }

            if (input == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) input = player.GetComponent<InputReader>();
            }
        }

        private void OnGUI()
        {
            if (!showCompass || Event.current.type != EventType.Repaint || Demon.SurvivorJumpscare.AnyPlaying)
            {
                return;
            }

            if (view == null || input == null)
            {
                ResolveReferences();
                if (view == null) return;
            }

            if (input != null && !input.InputEnabled)
            {
                return; // pause ou carte ouverte
            }

            EnsureStyles();

            float scale = Screen.height / 1080f;
            float width = Screen.width * widthFraction;
            float h = height * scale;
            Rect bar = new Rect((Screen.width - width) * 0.5f, topMargin * scale, width, h);
            float heading = Heading;
            float half = visibleAngle * 0.5f;

            _cardinalStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(20f * scale));
            _smallStyle.fontSize = Mathf.Max(7, Mathf.RoundToInt(14f * scale));
            _degreeStyle.fontSize = Mathf.Max(7, Mathf.RoundToInt(15f * scale));

            Color previous = GUI.color;

            // Fond : degrade horizontal qui s'estompe sur les bords.
            GUI.color = backgroundColor;
            GUI.DrawTexture(bar, BackgroundTexture());

            // Graduations et points cardinaux.
            float start = Mathf.Ceil((heading - half) / minorTickStep) * minorTickStep;

            for (float a = start; a <= heading + half; a += minorTickStep)
            {
                int cardinal = CardinalIndex(Normalize(a));

                if (cardinal >= 0 && (cardinal % 2 == 0 || showIntercardinals))
                {
                    continue; // une lettre prend la place du trait
                }

                float u = 0.5f + Mathf.DeltaAngle(heading, a) / visibleAngle;
                float x = bar.x + u * bar.width;
                GUI.color = Fade(tickColor, u);
                float tick = bar.height * 0.28f;
                GUI.DrawTexture(new Rect(x - 0.5f * Mathf.Max(1f, scale), bar.y + (bar.height - tick) * 0.5f, Mathf.Max(1f, scale), tick), Texture2D.whiteTexture);
            }

            for (int cardinal = 0; cardinal < 8; cardinal++)
            {
                bool main = cardinal % 2 == 0;

                if (!main && !showIntercardinals)
                {
                    continue;
                }

                float delta = Mathf.DeltaAngle(heading, cardinal * 45f);

                if (Mathf.Abs(delta) > half)
                {
                    continue;
                }

                float u = 0.5f + delta / visibleAngle;
                float x = bar.x + u * bar.width;
                Color c = cardinal == 0 ? northColor : cardinalColor;
                if (!main) c.a *= 0.75f;
                Label(new Rect(x - 30f * scale, bar.y, 60f * scale, bar.height), Cardinals[cardinal], main ? _cardinalStyle : _smallStyle, Fade(c, u));
            }

            // Trait central.
            GUI.color = centerColor;
            float cw = Mathf.Max(2f, 2f * scale);
            GUI.DrawTexture(new Rect(bar.center.x - cw * 0.5f, bar.y - 4f * scale, cw, 7f * scale), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bar.center.x - cw * 0.5f, bar.yMax - 3f * scale, cw, 7f * scale), Texture2D.whiteTexture);

            if (showDegrees)
            {
                string text = Mathf.RoundToInt(heading) % 360 + "°  " + Cardinals[Mathf.RoundToInt(heading / 45f) % 8];
                Label(new Rect(bar.x, bar.yMax + 4f * scale, bar.width, 20f * scale), text, _degreeStyle, cardinalColor * new Color(1f, 1f, 1f, 0.8f));
            }

            GUI.color = previous;
        }

        private Color Fade(Color c, float u)
        {
            if (edgeFade <= 0f) return c;
            float edge = Mathf.Min(u, 1f - u);
            c.a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / edgeFade));
            return c;
        }

        private Texture2D BackgroundTexture()
        {
            if (_background != null && Mathf.Approximately(_backgroundFade, edgeFade))
            {
                return _background;
            }

            if (_background == null)
            {
                _background = new Texture2D(128, 1, TextureFormat.RGBA32, false);
                _background.wrapMode = TextureWrapMode.Clamp;
                _background.hideFlags = HideFlags.HideAndDontSave;
            }

            for (int i = 0; i < 128; i++)
            {
                _background.SetPixel(i, 0, Fade(Color.white, (i + 0.5f) / 128f));
            }

            _background.Apply();
            _backgroundFade = edgeFade;
            return _background;
        }

        private void OnDestroy()
        {
            if (_background != null) Destroy(_background);
        }

        private static void Label(Rect rect, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.7f);
            GUI.color = Color.white;
            Rect shadow = rect;
            shadow.x += 1f;
            shadow.y += 1f;
            GUI.Label(shadow, text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }

        private static int CardinalIndex(float angle)
        {
            float step = angle / 45f;
            int index = Mathf.RoundToInt(step);
            return Mathf.Abs(step - index) < 0.01f ? index % 8 : -1;
        }

        private static float Normalize(float angle)
        {
            angle %= 360f;
            return angle < 0f ? angle + 360f : angle;
        }

        private void EnsureStyles()
        {
            if (_cardinalStyle != null)
            {
                return;
            }

            _cardinalStyle = new GUIStyle(GUI.skin.label);
            _cardinalStyle.alignment = TextAnchor.MiddleCenter;
            _cardinalStyle.fontStyle = FontStyle.Bold;
            _cardinalStyle.wordWrap = false;
            _cardinalStyle.clipping = TextClipping.Overflow;

            _smallStyle = new GUIStyle(_cardinalStyle);
            _smallStyle.fontStyle = FontStyle.Normal;

            _degreeStyle = new GUIStyle(_smallStyle);
        }
    }
}
