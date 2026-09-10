using HouseOfSilence.Core;
using HouseOfSilence.Interaction;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.UI
{
    /// <summary>
    /// Affichage temporaire du viseur et du prompt d'interaction, en IMGUI.
    ///
    /// IMPORTANT : c'est une solution provisoire, volontairement sans dependance
    /// (ni Canvas, ni TextMeshPro), pour pouvoir tester la Phase 3 immediatement.
    /// La Phase 15 la remplacera par un vrai HUD uGUI ; il suffira alors de
    /// desactiver ce composant : la nouvelle UI ecoutera exactement les memes
    /// evenements (InteractionTargetChangedEvent, InteractionHoldProgressEvent).
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionPromptOverlay : MonoBehaviour
    {
        [Header("Affichage")]
        [SerializeField] private bool showCrosshair = true;
        [SerializeField] private bool showPrompt = true;

        [Tooltip("N'affiche le HUD que pendant l'etat Playing.")]
        [SerializeField] private bool onlyWhilePlaying = true;

        [Header("Couleurs")]
        [SerializeField] private Color crosshairColor = new Color(1f, 1f, 1f, 0.5f);
        [SerializeField] private Color crosshairFocusedColor = new Color(1f, 0.95f, 0.8f, 0.95f);
        [SerializeField] private Color promptColor = new Color(0.96f, 0.94f, 0.9f, 1f);
        [SerializeField] private Color blockedColor = new Color(0.85f, 0.45f, 0.4f, 1f);

        [Tooltip("Couleur du nom de l'objet, volontairement plus discret que l'action.")]
        [SerializeField] private Color titleColor = new Color(0.82f, 0.79f, 0.72f, 0.85f);

        [Header("Mise en page")]
        [SerializeField, Range(10, 32)] private int fontSize = 17;
        [SerializeField, Range(0f, 200f)] private float promptOffsetY = 60f;

        private InputReader _input;
        private string _prompt = string.Empty;
        private string _title = string.Empty;
        private bool _hasTarget;
        private bool _canInteract;
        private float _holdProgress;
        private bool _isHolding;

        private GUIStyle _promptStyle;
        private GUIStyle _titleStyle;
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
            EventBus.Subscribe<InteractionTargetChangedEvent>(OnTargetChanged);
            EventBus.Subscribe<InteractionHoldProgressEvent>(OnHoldProgress);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<InteractionTargetChangedEvent>(OnTargetChanged);
            EventBus.Unsubscribe<InteractionHoldProgressEvent>(OnHoldProgress);
        }

        private void OnDestroy()
        {
            if (_pixel != null)
            {
                Destroy(_pixel);
                _pixel = null;
            }
        }

        private void OnTargetChanged(InteractionTargetChangedEvent evt)
        {
            _hasTarget = evt.HasTarget;
            _prompt = evt.Prompt;
            _title = evt.Title;
            _canInteract = evt.CanInteract;

            if (!_hasTarget)
            {
                _holdProgress = 0f;
                _isHolding = false;
            }

            if (_input == null && evt.Player != null)
            {
                _input = evt.Player.Input;
            }
        }

        private void OnHoldProgress(InteractionHoldProgressEvent evt)
        {
            _holdProgress = evt.Progress;
            _isHolding = evt.IsHolding;
        }

        // ------------------------------------------------------------------

        private void OnGUI()
        {
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

            if (_promptStyle == null)
            {
                _promptStyle = new GUIStyle(GUI.skin.label);
                _promptStyle.alignment = TextAnchor.MiddleCenter;
                _promptStyle.fontStyle = FontStyle.Bold;
                _promptStyle.richText = false;
            }

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label);
                _titleStyle.alignment = TextAnchor.MiddleCenter;
                _titleStyle.fontStyle = FontStyle.Normal;
                _titleStyle.richText = false;
            }

            _promptStyle.fontSize = fontSize;
            _titleStyle.fontSize = Mathf.Max(10, fontSize - 3);

            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.5f;

            if (showCrosshair)
            {
                DrawCrosshair(centerX, centerY);
            }

            if (!showPrompt || !_hasTarget || string.IsNullOrEmpty(_prompt))
            {
                return;
            }

            string key = _input != null ? _input.GetInteractDisplayKey() : "E";
            string text = _canInteract ? "[ " + key + " ]  " + _prompt.ToUpperInvariant() : _prompt.ToUpperInvariant();

            Color previous = GUI.color;

            // Ligne du haut : quel objet est vise. Elle leve l'ambiguite quand un
            // meme objet propose plusieurs actions selon son etat.
            if (!string.IsNullOrEmpty(_title))
            {
                Rect titleRect = new Rect(centerX - 250f, centerY + promptOffsetY - 21f, 500f, 22f);

                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.Label(new Rect(titleRect.x + 1f, titleRect.y + 1f, titleRect.width, titleRect.height), _title, _titleStyle);

                GUI.color = titleColor;
                GUI.Label(titleRect, _title, _titleStyle);
            }

            // Ligne du bas : l'action, avec la touche.
            Rect rect = new Rect(centerX - 250f, centerY + promptOffsetY, 500f, 30f);

            // Ombre portee : lisible sur fond clair comme sur fond sombre.
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _promptStyle);

            GUI.color = _canInteract ? promptColor : blockedColor;
            GUI.Label(rect, text, _promptStyle);

            GUI.color = previous;

            if (_isHolding && _holdProgress > 0f)
            {
                DrawHoldBar(centerX, rect.y + 30f);
            }
        }

        private void DrawCrosshair(float centerX, float centerY)
        {
            Color previous = GUI.color;

            float size = _hasTarget ? 7f : 4f;
            GUI.color = _hasTarget ? crosshairFocusedColor : crosshairColor;

            GUI.DrawTexture(new Rect(centerX - size * 0.5f, centerY - size * 0.5f, size, size), _pixel);

            GUI.color = previous;
        }

        private void DrawHoldBar(float centerX, float y)
        {
            Color previous = GUI.color;

            const float width = 180f;
            const float height = 5f;

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(centerX - width * 0.5f, y, width, height), _pixel);

            GUI.color = promptColor;
            GUI.DrawTexture(new Rect(centerX - width * 0.5f, y, width * Mathf.Clamp01(_holdProgress), height), _pixel);

            GUI.color = previous;
        }
    }
}
