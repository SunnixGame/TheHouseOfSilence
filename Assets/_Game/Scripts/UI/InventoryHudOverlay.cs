using HouseOfSilence.Core;
using HouseOfSilence.Inventory;
using HouseOfSilence.Items;
using UnityEngine;

namespace HouseOfSilence.UI
{
    /// <summary>
    /// Barre d'inventaire temporaire, en IMGUI.
    ///
    /// Comme InteractionPromptOverlay, c'est un affichage jetable destine a
    /// tester la Phase 4 sans dependre d'un Canvas ni de TextMeshPro.
    /// La Phase 15 le remplacera : la vraie UI ecoutera exactement les memes
    /// evenements (InventoryChangedEvent, InventorySelectionChangedEvent).
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryHudOverlay : MonoBehaviour
    {
        [Header("Affichage")]
        [SerializeField] private bool visible = true;

        [Tooltip("N'affiche la barre que pendant l'etat Playing.")]
        [SerializeField] private bool onlyWhilePlaying = true;

        [Header("Mise en page")]
        [SerializeField, Range(40f, 120f)] private float slotSize = 74f;
        [SerializeField, Range(2f, 20f)] private float slotSpacing = 6f;
        [SerializeField, Range(10f, 80f)] private float marginBottom = 26f;

        [Header("Couleurs")]
        [SerializeField] private Color slotColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color selectedColor = new Color(0.95f, 0.85f, 0.6f, 0.9f);
        [SerializeField] private Color textColor = new Color(0.95f, 0.93f, 0.88f, 1f);
        [SerializeField] private Color emptyTextColor = new Color(1f, 1f, 1f, 0.25f);

        private PlayerInventory _inventory;
        private InventoryIconRenderer _iconRenderer;
        private int _selectedIndex;
        private string _message = string.Empty;
        private float _messageTimer;
        private ItemData _lastDropped;
        private int _lastDropFrame = -10;

        private Texture2D _pixel;
        private GUIStyle _slotStyle;
        private GUIStyle _countStyle;

        private void Awake()
        {
            _iconRenderer = GetComponent<InventoryIconRenderer>();

            if (_iconRenderer == null)
            {
                _iconRenderer = gameObject.AddComponent<InventoryIconRenderer>();
            }

            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
            _pixel.hideFlags = HideFlags.HideAndDontSave;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<InventoryChangedEvent>(OnInventoryChanged);
            EventBus.Subscribe<InventorySelectionChangedEvent>(OnSelectionChanged);
            EventBus.Subscribe<InventoryFullEvent>(OnInventoryFull);
            EventBus.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
            EventBus.Subscribe<ItemDroppedEvent>(OnItemDropped);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<InventoryChangedEvent>(OnInventoryChanged);
            EventBus.Unsubscribe<InventorySelectionChangedEvent>(OnSelectionChanged);
            EventBus.Unsubscribe<InventoryFullEvent>(OnInventoryFull);
            EventBus.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);
            EventBus.Unsubscribe<ItemDroppedEvent>(OnItemDropped);
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
            if (_messageTimer > 0f)
            {
                _messageTimer -= Time.unscaledDeltaTime;

                if (_messageTimer <= 0f)
                {
                    _message = string.Empty;
                }
            }
        }

        // ------------------------------------------------------------------

        private void OnInventoryChanged(InventoryChangedEvent evt)
        {
            _inventory = evt.Inventory;
        }

        private void OnSelectionChanged(InventorySelectionChangedEvent evt)
        {
            _selectedIndex = evt.SlotIndex;
        }

        private void OnInventoryFull(InventoryFullEvent evt)
        {
            string itemName = evt.Item != null ? evt.Item.ItemName : "objet";
            ShowMessage("Inventaire plein - impossible de prendre " + itemName);
        }

        private void OnItemDropped(ItemDroppedEvent evt)
        {
            if (evt.Item == null)
            {
                return;
            }

            _lastDropped = evt.Item;
            _lastDropFrame = Time.frameCount;

            ShowMessage(evt.Item.ItemName + " laisse au sol");
        }

        private void OnItemPickedUp(ItemPickedUpEvent evt)
        {
            if (evt.Item == null)
            {
                return;
            }

            string suffix = evt.Count > 1 ? " x" + evt.Count : string.Empty;

            // Un lacher publie dans la meme frame signifie un echange :
            // on l'annonce en une seule ligne, plus claire pour le joueur.
            if (_lastDropped != null && _lastDropFrame == Time.frameCount)
            {
                ShowMessage(evt.Item.ItemName + suffix + " pris  -  " + _lastDropped.ItemName + " laisse au sol");
                _lastDropped = null;
                return;
            }

            ShowMessage(evt.Item.ItemName + suffix + " ramasse");
        }

        private void ShowMessage(string message)
        {
            _message = message;
            _messageTimer = 2.5f;
        }

        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!visible || _inventory == null)
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

            int slotCount = _inventory.SlotCount;
            float totalWidth = slotCount * slotSize + (slotCount - 1) * slotSpacing;
            float startX = (Screen.width - totalWidth) * 0.5f;
            float y = Screen.height - marginBottom - slotSize;

            Color previous = GUI.color;

            for (int i = 0; i < slotCount; i++)
            {
                Rect rect = new Rect(startX + i * (slotSize + slotSpacing), y, slotSize, slotSize);
                DrawSlot(rect, i);
            }

            DrawSelectedName(startX, y, totalWidth);
            DrawMessage();

            GUI.color = previous;
        }

        private void DrawSlot(Rect rect, int index)
        {
            InventorySlot slot = _inventory.GetSlot(index);
            bool selected = index == _selectedIndex;

            // Fond
            GUI.color = slotColor;
            GUI.DrawTexture(rect, _pixel);

            // Cadre
            GUI.color = selected ? selectedColor : new Color(1f, 1f, 1f, 0.2f);
            DrawBorder(rect, selected ? 2f : 1f);

            // Contenu
            GUI.color = Color.white;

            if (slot.IsEmpty)
            {
                _slotStyle.normal.textColor = emptyTextColor;
                GUI.Label(rect, (index + 1).ToString(), _slotStyle);
                return;
            }

            ItemData item = slot.Item;

            // 1. Le mesh de l'objet, rendu en 3D et qui tourne (prefere).
            Texture preview = _iconRenderer != null ? _iconRenderer.GetPreview(item) : null;

            if (preview != null)
            {
                GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), preview, ScaleMode.ScaleToFit);
            }
            else if (item.Icon != null)
            {
                // 2. Icone 2D si l'objet en a une.
                Rect iconRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 24f);
                GUI.DrawTexture(iconRect, item.Icon.texture, ScaleMode.ScaleToFit);
            }
            else
            {
                // 3. Sinon, son nom.
                _slotStyle.normal.textColor = textColor;
                GUI.Label(new Rect(rect.x + 3f, rect.y + 4f, rect.width - 6f, rect.height - 18f), ShortName(item.ItemName), _slotStyle);
            }

            if (slot.Count > 1)
            {
                _countStyle.normal.textColor = textColor;
                GUI.Label(new Rect(rect.x, rect.yMax - 18f, rect.width - 6f, 16f), "x" + slot.Count, _countStyle);
            }
        }

        private void DrawSelectedName(float startX, float y, float totalWidth)
        {
            ItemData item = _inventory.SelectedItem;

            if (item == null)
            {
                return;
            }

            _slotStyle.normal.textColor = textColor;

            string text = item.ItemName;

            if (item.Usable)
            {
                text += "   [Clic gauche] Utiliser";
            }

            if (item.Droppable)
            {
                text += "   [G] Lacher";
            }

            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(startX + 1f, y - 23f, totalWidth, 20f), text, _slotStyle);

            GUI.color = Color.white;
            GUI.Label(new Rect(startX, y - 24f, totalWidth, 20f), text, _slotStyle);
        }

        private void DrawMessage()
        {
            if (string.IsNullOrEmpty(_message))
            {
                return;
            }

            _slotStyle.normal.textColor = textColor;

            Rect rect = new Rect(0f, Screen.height * 0.62f, Screen.width, 22f);

            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), _message, _slotStyle);

            GUI.color = Color.white;
            GUI.Label(rect, _message, _slotStyle);
        }

        private void DrawBorder(Rect rect, float thickness)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _pixel);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), _pixel);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _pixel);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), _pixel);
        }

        private static string ShortName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || itemName.Length <= 14)
            {
                return itemName;
            }

            return itemName.Substring(0, 13) + ".";
        }

        private void EnsureStyles()
        {
            if (_slotStyle == null)
            {
                _slotStyle = new GUIStyle(GUI.skin.label);
                _slotStyle.alignment = TextAnchor.MiddleCenter;
                _slotStyle.fontSize = 12;
                _slotStyle.wordWrap = true;
                _slotStyle.richText = false;
            }

            if (_countStyle == null)
            {
                _countStyle = new GUIStyle(GUI.skin.label);
                _countStyle.alignment = TextAnchor.MiddleRight;
                _countStyle.fontSize = 12;
                _countStyle.fontStyle = FontStyle.Bold;
            }
        }
    }
}
