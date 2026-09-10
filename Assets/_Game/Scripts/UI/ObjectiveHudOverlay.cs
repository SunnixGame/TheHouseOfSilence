using HouseOfSilence.Core;
using HouseOfSilence.Objectives;
using UnityEngine;

namespace HouseOfSilence.UI
{
    /// <summary>
    /// Affichage temporaire de l'objectif courant, en IMGUI, en haut a droite.
    ///
    /// Comme les autres overlays des phases precedentes, c'est une solution
    /// jetable sans dependance. La Phase 15 la remplacera par un vrai HUD :
    /// il suffira de desactiver ce composant, la nouvelle UI ecoutant
    /// exactement les memes evenements.
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectiveHudOverlay : MonoBehaviour
    {
        [Header("Affichage")]
        [SerializeField] private bool visible = true;
        [SerializeField] private bool onlyWhilePlaying = true;

        [Header("Mise en page")]
        [SerializeField, Range(200f, 520f)] private float panelWidth = 340f;
        [SerializeField, Range(8f, 120f)] private float marginTop = 16f;
        [SerializeField, Range(8f, 120f)] private float marginRight = 18f;

        [Header("Couleurs")]
        [SerializeField] private Color labelColor = new Color(0.72f, 0.69f, 0.62f, 0.9f);
        [SerializeField] private Color titleColor = new Color(0.97f, 0.95f, 0.9f, 1f);
        [SerializeField] private Color descriptionColor = new Color(0.8f, 0.78f, 0.73f, 0.9f);
        [SerializeField] private Color completedColor = new Color(0.55f, 0.85f, 0.6f, 1f);
        [SerializeField] private Color hintColor = new Color(0.9f, 0.8f, 0.5f, 1f);

        [Header("Notification")]
        [SerializeField, Min(0.5f)] private float notificationDuration = 3f;

        private ObjectiveData _current;
        private int _index;
        private int _total;
        private bool _sequenceComplete;

        private string _notification = string.Empty;
        private Color _notificationColor = Color.white;
        private float _notificationTimer;

        private string _hint = string.Empty;

        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _notificationStyle;
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
            EventBus.Subscribe<ObjectiveActivatedEvent>(OnObjectiveActivated);
            EventBus.Subscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
            EventBus.Subscribe<ObjectiveFailedEvent>(OnObjectiveFailed);
            EventBus.Subscribe<ObjectiveHintEvent>(OnObjectiveHint);
            EventBus.Subscribe<AllObjectivesCompletedEvent>(OnAllCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ObjectiveActivatedEvent>(OnObjectiveActivated);
            EventBus.Unsubscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
            EventBus.Unsubscribe<ObjectiveFailedEvent>(OnObjectiveFailed);
            EventBus.Unsubscribe<ObjectiveHintEvent>(OnObjectiveHint);
            EventBus.Unsubscribe<AllObjectivesCompletedEvent>(OnAllCompleted);
        }

        private void OnDestroy()
        {
            if (_pixel != null)
            {
                Destroy(_pixel);
                _pixel = null;
            }
        }

        private void Update()
        {
            if (_notificationTimer <= 0f)
            {
                return;
            }

            _notificationTimer -= Time.unscaledDeltaTime;

            if (_notificationTimer <= 0f)
            {
                _notification = string.Empty;
            }
        }

        // ------------------------------------------------------------------

        private void OnObjectiveActivated(ObjectiveActivatedEvent evt)
        {
            _current = evt.Objective;
            _index = evt.Index;
            _total = evt.Total;
            _hint = string.Empty;
            _sequenceComplete = false;
        }

        private void OnObjectiveCompleted(ObjectiveCompletedEvent evt)
        {
            string title = evt.Objective != null ? evt.Objective.Title : evt.ObjectiveId;
            ShowNotification("OBJECTIF TERMINE\n" + title, completedColor);
        }

        private void OnObjectiveFailed(ObjectiveFailedEvent evt)
        {
            string title = evt.Objective != null ? evt.Objective.Title : evt.ObjectiveId;
            ShowNotification("OBJECTIF ECHOUE\n" + title, new Color(0.9f, 0.45f, 0.4f, 1f));
        }

        private void OnObjectiveHint(ObjectiveHintEvent evt)
        {
            _hint = evt.Hint;
        }

        private void OnAllCompleted(AllObjectivesCompletedEvent evt)
        {
            _current = null;
            _hint = string.Empty;
            _sequenceComplete = true;
            ShowNotification("TOUS LES OBJECTIFS SONT TERMINES", completedColor);
        }

        private void ShowNotification(string text, Color color)
        {
            _notification = text;
            _notificationColor = color;
            _notificationTimer = notificationDuration;
        }

        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!visible)
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

            EnsureStyles();

            DrawObjectivePanel();
            DrawNotification();
        }

        private void DrawObjectivePanel()
        {
            if (_current == null && !_sequenceComplete)
            {
                return;
            }

            string label = _sequenceComplete
                ? "OBJECTIFS"
                : "OBJECTIF " + (_index + 1) + " / " + _total;

            string title = _sequenceComplete ? "Tous les objectifs sont termines" : _current.Title;
            string description = _sequenceComplete ? string.Empty : _current.Description;

            float x = Screen.width - panelWidth - marginRight;
            float y = marginTop;

            float height = 34f;
            float titleHeight = _titleStyle.CalcHeight(new GUIContent(title), panelWidth - 20f);
            height += titleHeight;

            float descriptionHeight = 0f;

            if (!string.IsNullOrEmpty(description))
            {
                descriptionHeight = _bodyStyle.CalcHeight(new GUIContent(description), panelWidth - 20f);
                height += descriptionHeight + 4f;
            }

            float hintHeight = 0f;

            if (!string.IsNullOrEmpty(_hint))
            {
                hintHeight = _bodyStyle.CalcHeight(new GUIContent("Indice : " + _hint), panelWidth - 20f);
                height += hintHeight + 6f;
            }

            Color previous = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(x, y, panelWidth, height), _pixel);

            // Liseré gauche : repere visuel discret.
            GUI.color = _sequenceComplete ? completedColor : new Color(0.85f, 0.8f, 0.65f, 0.8f);
            GUI.DrawTexture(new Rect(x, y, 2f, height), _pixel);

            float cursor = y + 8f;

            GUI.color = labelColor;
            GUI.Label(new Rect(x + 12f, cursor, panelWidth - 20f, 16f), label, _labelStyle);
            cursor += 18f;

            GUI.color = _sequenceComplete ? completedColor : titleColor;
            GUI.Label(new Rect(x + 12f, cursor, panelWidth - 20f, titleHeight), title, _titleStyle);
            cursor += titleHeight + 2f;

            if (descriptionHeight > 0f)
            {
                GUI.color = descriptionColor;
                GUI.Label(new Rect(x + 12f, cursor, panelWidth - 20f, descriptionHeight), description, _bodyStyle);
                cursor += descriptionHeight + 4f;
            }

            if (hintHeight > 0f)
            {
                GUI.color = hintColor;
                GUI.Label(new Rect(x + 12f, cursor, panelWidth - 20f, hintHeight), "Indice : " + _hint, _bodyStyle);
            }

            GUI.color = previous;
        }

        private void DrawNotification()
        {
            if (string.IsNullOrEmpty(_notification))
            {
                return;
            }

            float alpha = Mathf.Clamp01(_notificationTimer / 0.6f);

            Rect rect = new Rect(0f, Screen.height * 0.3f, Screen.width, 60f);

            Color previous = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.8f * alpha);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), _notification, _notificationStyle);

            Color color = _notificationColor;
            color.a = alpha;
            GUI.color = color;
            GUI.Label(rect, _notification, _notificationStyle);

            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.alignment = TextAnchor.UpperLeft;
                _labelStyle.fontSize = 11;
                _labelStyle.fontStyle = FontStyle.Bold;
            }

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label);
                _titleStyle.alignment = TextAnchor.UpperLeft;
                _titleStyle.fontSize = 15;
                _titleStyle.fontStyle = FontStyle.Bold;
                _titleStyle.wordWrap = true;
            }

            if (_bodyStyle == null)
            {
                _bodyStyle = new GUIStyle(GUI.skin.label);
                _bodyStyle.alignment = TextAnchor.UpperLeft;
                _bodyStyle.fontSize = 12;
                _bodyStyle.wordWrap = true;
            }

            if (_notificationStyle == null)
            {
                _notificationStyle = new GUIStyle(GUI.skin.label);
                _notificationStyle.alignment = TextAnchor.MiddleCenter;
                _notificationStyle.fontSize = 19;
                _notificationStyle.fontStyle = FontStyle.Bold;
                _notificationStyle.wordWrap = true;
            }
        }
    }
}
