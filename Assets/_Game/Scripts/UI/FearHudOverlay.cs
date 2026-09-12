using HouseOfSilence.Core;
using HouseOfSilence.Horror;
using UnityEngine;

namespace HouseOfSilence.UI
{
    /// <summary>
    /// Indicateur de peur discret, en bas a gauche : une fine jauge verticale
    /// qui se remplit et se colore, sans chiffre. Le joueur doit SENTIR sa
    /// peur, pas la lire.
    ///
    /// Overlay IMGUI temporaire, remplace en Phase 15 (memes evenements).
    /// </summary>
    [DisallowMultipleComponent]
    public class FearHudOverlay : MonoBehaviour
    {
        [Header("Affichage")]
        [SerializeField] private bool visible = true;
        [SerializeField] private bool onlyWhilePlaying = true;

        [Tooltip("Cache la jauge tant que la peur est sous ce seuil (0-1).")]
        [SerializeField, Range(0f, 0.5f)] private float hideBelow = 0.05f;

        [Header("Mise en page")]
        [SerializeField, Range(3f, 14f)] private float barWidth = 6f;
        [SerializeField, Range(40f, 200f)] private float barHeight = 90f;
        [SerializeField, Range(8f, 80f)] private float margin = 26f;

        [Header("Couleurs")]
        [SerializeField] private Color calmColor = new Color(0.85f, 0.85f, 0.8f, 0.55f);
        [SerializeField] private Color panicColor = new Color(0.85f, 0.15f, 0.12f, 0.95f);

        private float _target;
        private float _current;
        private FearLevel _level = FearLevel.Calm;
        private Texture2D _pixel;

        private void Awake()
        {
            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
            _pixel.hideFlags = HideFlags.HideAndDontSave;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<FearChangedEvent>(OnFearChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<FearChangedEvent>(OnFearChanged);
        }

        private void OnDestroy()
        {
            if (_pixel != null)
            {
                Destroy(_pixel);
                _pixel = null;
            }
        }

        private void OnFearChanged(FearChangedEvent evt)
        {
            // En coop, le HUD ne suit que le joueur local.
            if (evt.Player != null && !evt.Player.IsLocalPlayer)
            {
                return;
            }

            _target = evt.Normalized;
            _level = evt.Level;
        }

        private void Update()
        {
            _current = Mathf.Lerp(_current, _target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
        }

        private void OnGUI()
        {
            if (!visible || _current < hideBelow)
            {
                return;
            }

            if (onlyWhilePlaying)
            {
                if (!GameManager.HasInstance)
                {
                    return;
                }

                GameManager game = GameManager.Instance;

                if (game == null || game.State != GameState.Playing)
                {
                    return;
                }
            }

            float x = margin;
            float bottom = Screen.height - margin;

            Color previous = GUI.color;

            // Fond
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(x - 1f, bottom - barHeight - 1f, barWidth + 2f, barHeight + 2f), _pixel);

            // Remplissage, du bas vers le haut
            float fill = barHeight * Mathf.Clamp01(_current);
            Color color = Color.Lerp(calmColor, panicColor, _current);

            if (_level == FearLevel.Panic)
            {
                float pulse = (Mathf.Sin(Time.unscaledTime * 10f) + 1f) * 0.5f;
                color.a = Mathf.Lerp(0.6f, 1f, pulse);
            }

            GUI.color = color;
            GUI.DrawTexture(new Rect(x, bottom - fill, barWidth, fill), _pixel);

            GUI.color = previous;
        }
    }
}
