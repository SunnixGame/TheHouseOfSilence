using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HouseOfSilence.HorrorMenu
{
    /// <summary>
    /// Retour visuel d'une entree du menu : texte qui passe au rouge et tiret
    /// qui apparait quand l'entree est survolee ou selectionnee (souris,
    /// clavier ou manette). Le clic reste gere par le Button.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    [DisallowMultipleComponent]
    public class HorrorMenuItem : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler
    {
        [SerializeField] private Text label;
        [SerializeField] private Graphic selector;

        [SerializeField] private Color normalColor = new Color(0.72f, 0.7f, 0.66f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.75f, 0.06f, 0.04f, 1f);
        [SerializeField] private Color disabledColor = new Color(0.72f, 0.7f, 0.66f, 0.3f);

        [Tooltip("Decalage horizontal du texte quand l'entree est selectionnee.")]
        [SerializeField] private float selectedOffset = 14f;

        private Selectable _selectable;
        private RectTransform _labelRect;
        private Vector2 _labelBasePosition;
        private bool _selected;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();

            if (label != null)
            {
                _labelRect = label.rectTransform;
                _labelBasePosition = _labelRect.anchoredPosition;
            }

            Refresh(true);
        }

        private void OnEnable()
        {
            _selected = false;
            Refresh(true);
        }

        public void OnSelect(BaseEventData eventData)
        {
            _selected = true;
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _selected = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // Le survol souris deplace la selection : un seul element en surbrillance.
            if (_selectable.interactable && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(gameObject);
            }
        }

        private void Update()
        {
            Refresh(false);
        }

        private void Refresh(bool instant)
        {
            bool interactable = _selectable == null || _selectable.interactable;
            bool highlighted = interactable && _selected;
            float k = instant ? 1f : Time.unscaledDeltaTime * 12f;

            if (label != null)
            {
                Color target = !interactable ? disabledColor : (highlighted ? selectedColor : normalColor);
                label.color = Color.Lerp(label.color, target, k);

                if (_labelRect != null)
                {
                    Vector2 targetPos = _labelBasePosition + (highlighted ? new Vector2(selectedOffset, 0f) : Vector2.zero);
                    _labelRect.anchoredPosition = Vector2.Lerp(_labelRect.anchoredPosition, targetPos, k);
                }
            }

            if (selector != null)
            {
                Color c = selector.color;
                c.a = Mathf.Lerp(c.a, highlighted ? 1f : 0f, k);
                selector.color = c;
            }
        }
    }
}
