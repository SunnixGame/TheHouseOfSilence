using HouseOfSilence.Core;
using HouseOfSilence.Items;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Inventory
{
    /// <summary>
    /// Inventaire du joueur : 6 emplacements, un seul selectionne a la fois.
    ///
    /// L'inventaire ne connait le comportement d'aucun objet. Utiliser un objet
    /// publie simplement ItemUsedEvent : la lampe torche, la radio ou le briquet
    /// s'y abonnent chacun de leur cote (Phases 24-25).
    ///
    /// A poser sur le GameObject du joueur.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInventory : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private InputReader input;

        [Header("Configuration")]
        [Tooltip("Nombre d'emplacements. Le cahier des charges en prevoit 6.")]
        [SerializeField, Range(1, 6)] private int slotCount = 6;

        [Tooltip("Prefab utilise pour lacher un objet qui n'a pas de World Prefab propre.")]
        [SerializeField] private GameObject defaultDropPrefab;

        [Header("Lacher")]
        [Tooltip("Distance devant le joueur ou l'objet est pose.")]
        [SerializeField, Min(0.2f)] private float dropDistance = 0.9f;

        [Tooltip("Impulsion appliquee si l'objet lache possede un Rigidbody.")]
        [SerializeField, Min(0f)] private float dropForce = 1.6f;

        [Header("Debug")]
        [SerializeField] private bool logToConsole = true;

        private InventorySlot[] _slots;
        private int _selectedIndex;

        // ------------------------------------------------------------------
        // Lecture
        // ------------------------------------------------------------------

        public int SlotCount { get { return _slots != null ? _slots.Length : slotCount; } }

        public int SelectedIndex { get { return _selectedIndex; } }

        public InventorySlot SelectedSlot { get { return GetSlot(_selectedIndex); } }

        public ItemData SelectedItem { get { return GetSlot(_selectedIndex).Item; } }

        /// <summary>Contenu d'un emplacement. Renvoie un slot vide si l'index est invalide.</summary>
        public InventorySlot GetSlot(int index)
        {
            if (_slots == null || index < 0 || index >= _slots.Length)
            {
                return InventorySlot.Empty;
            }

            return _slots[index];
        }

        /// <summary>Vrai si l'inventaire ne peut plus rien accueillir de cet objet.</summary>
        public bool IsFullFor(ItemData item)
        {
            if (item == null)
            {
                return true;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    return false;
                }

                if (item.Stackable && _slots[i].Item == item && _slots[i].Count < item.MaxStack)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Nombre total d'exemplaires d'un objet.</summary>
        public int CountOf(ItemData item)
        {
            if (item == null)
            {
                return 0;
            }

            int total = 0;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Item == item)
                {
                    total += _slots[i].Count;
                }
            }

            return total;
        }

        public bool HasItem(ItemData item)
        {
            return CountOf(item) > 0;
        }

        /// <summary>Vrai si l'inventaire contient une cle correspondant a cette serrure.</summary>
        public bool HasKeyFor(string lockId)
        {
            return FindKeySlot(lockId) >= 0;
        }

        /// <summary>
        /// Retire une cle correspondant a la serrure (pour les cles a usage unique).
        /// Renvoie false si aucune cle ne correspond.
        /// </summary>
        public bool ConsumeKeyFor(string lockId)
        {
            int index = FindKeySlot(lockId);

            if (index < 0)
            {
                return false;
            }

            RemoveAt(index, 1);
            return true;
        }

        private int FindKeySlot(string lockId)
        {
            if (string.IsNullOrEmpty(lockId))
            {
                return -1;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                ItemData item = _slots[i].Item;

                if (item != null && _slots[i].Count > 0 && item.OpensLock(lockId))
                {
                    return i;
                }
            }

            return -1;
        }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponent<PlayerCharacter>();
            }

            if (input == null)
            {
                input = GetComponent<InputReader>();
            }

            _slots = new InventorySlot[Mathf.Clamp(slotCount, 1, 6)];

            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = InventorySlot.Empty;
            }
        }

        private void OnEnable()
        {
            if (input == null)
            {
                return;
            }

            input.OnSlotChanged += CycleSelection;
            input.OnUseItem += UseSelected;
            input.OnDropItem += DropSelected;
        }

        private void OnDisable()
        {
            if (input == null)
            {
                return;
            }

            input.OnSlotChanged -= CycleSelection;
            input.OnUseItem -= UseSelected;
            input.OnDropItem -= DropSelected;
        }

        private void Start()
        {
            PublishChanged();
            PublishSelection();
        }

        // ------------------------------------------------------------------
        // Ajout / retrait
        // ------------------------------------------------------------------

        /// <summary>
        /// Tente d'ajouter un objet.
        /// </summary>
        /// <param name="remaining">Quantite qui n'a pas pu entrer (0 si tout est passe).</param>
        /// <returns>Vrai si au moins un exemplaire a ete ajoute.</returns>
        public bool TryAdd(ItemData item, int count, out int remaining)
        {
            remaining = count;

            if (item == null || count <= 0 || _slots == null)
            {
                return false;
            }

            int added = 0;

            // 1. On complete les piles existantes.
            if (item.Stackable)
            {
                for (int i = 0; i < _slots.Length && remaining > 0; i++)
                {
                    if (_slots[i].Item != item || _slots[i].Count >= item.MaxStack)
                    {
                        continue;
                    }

                    int space = item.MaxStack - _slots[i].Count;
                    int transfer = Mathf.Min(space, remaining);

                    _slots[i].Count += transfer;
                    remaining -= transfer;
                    added += transfer;
                }
            }

            // 2. On remplit les emplacements libres.
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty)
                {
                    continue;
                }

                int transfer = Mathf.Min(item.MaxStack, remaining);

                _slots[i] = new InventorySlot(item, transfer);
                remaining -= transfer;
                added += transfer;
            }

            if (added <= 0)
            {
                EventBus.Publish(new InventoryFullEvent(player, item));
                return false;
            }

            if (logToConsole)
            {
                Debug.Log("[Inventaire] +" + added + " " + item.ItemName + (remaining > 0 ? "  (inventaire plein, " + remaining + " laisse au sol)" : ""));
            }

            PublishChanged();
            EventBus.Publish(new ItemPickedUpEvent(player, item, added));

            // Si rien n'etait selectionne, on selectionne l'objet ramasse.
            if (SelectedItem == null)
            {
                SelectFirstOccupiedSlot();
            }

            return true;
        }

        /// <summary>Version courte quand le reste importe peu.</summary>
        public bool TryAdd(ItemData item, int count = 1)
        {
            int remaining;
            return TryAdd(item, count, out remaining);
        }

        /// <summary>Retire une quantite dans un emplacement precis.</summary>
        public bool RemoveAt(int index, int count = 1)
        {
            if (_slots == null || index < 0 || index >= _slots.Length || count <= 0)
            {
                return false;
            }

            if (_slots[index].IsEmpty)
            {
                return false;
            }

            _slots[index].Count -= count;

            if (_slots[index].Count <= 0)
            {
                _slots[index] = InventorySlot.Empty;
            }

            PublishChanged();

            if (index == _selectedIndex)
            {
                PublishSelection();
            }

            return true;
        }

        /// <summary>Retire une quantite d'un objet, quel que soit l'emplacement.</summary>
        public bool Remove(ItemData item, int count = 1)
        {
            if (item == null || count <= 0 || CountOf(item) < count)
            {
                return false;
            }

            int left = count;

            for (int i = 0; i < _slots.Length && left > 0; i++)
            {
                if (_slots[i].Item != item)
                {
                    continue;
                }

                int take = Mathf.Min(_slots[i].Count, left);
                _slots[i].Count -= take;
                left -= take;

                if (_slots[i].Count <= 0)
                {
                    _slots[i] = InventorySlot.Empty;
                }
            }

            PublishChanged();
            PublishSelection();
            return true;
        }

        /// <summary>Vide l'inventaire (nouvelle partie, mort).</summary>
        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = InventorySlot.Empty;
            }

            _selectedIndex = 0;
            PublishChanged();
            PublishSelection();
        }

        // ------------------------------------------------------------------
        // Selection
        // ------------------------------------------------------------------

        /// <summary>Selectionne un emplacement par son index.</summary>
        public void SelectSlot(int index)
        {
            if (_slots == null || _slots.Length == 0)
            {
                return;
            }

            int clamped = Mathf.Clamp(index, 0, _slots.Length - 1);

            if (clamped == _selectedIndex)
            {
                return;
            }

            _selectedIndex = clamped;
            PublishSelection();
        }

        /// <summary>Passe au slot suivant (+1) ou precedent (-1), en bouclant.</summary>
        public void CycleSelection(int direction)
        {
            if (_slots == null || _slots.Length == 0 || direction == 0)
            {
                return;
            }

            int step = direction > 0 ? 1 : -1;
            int index = _selectedIndex;

            index = (index + step + _slots.Length) % _slots.Length;

            _selectedIndex = index;
            PublishSelection();
        }

        private void SelectFirstOccupiedSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].IsEmpty)
                {
                    SelectSlot(i);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------
        // Utilisation et lacher
        // ------------------------------------------------------------------

        /// <summary>Utilise l'objet selectionne (clic gauche).</summary>
        public void UseSelected()
        {
            InventorySlot slot = SelectedSlot;

            if (slot.IsEmpty || !slot.Item.Usable)
            {
                return;
            }

            ItemData item = slot.Item;

            if (logToConsole)
            {
                Debug.Log("[Inventaire] Utilisation de " + item.ItemName);
            }

            EventBus.Publish(new ItemUsedEvent(player, item, _selectedIndex));

            if (item.ConsumeOnUse)
            {
                RemoveAt(_selectedIndex, 1);
            }
        }

        /// <summary>
        /// Vrai si l'objet actuellement selectionne peut etre echange contre un
        /// objet ramasse. Faux si le slot est vide ou si l'objet ne peut pas
        /// etre lache (objet de quete).
        /// </summary>
        public bool CanSwapSelected
        {
            get
            {
                InventorySlot slot = SelectedSlot;
                return !slot.IsEmpty && slot.Item.Droppable;
            }
        }

        /// <summary>
        /// Echange le contenu du slot selectionne contre un nouvel objet.
        /// L'ancien objet est repose au sol a la position indiquee (typiquement
        /// la ou se trouvait l'objet ramasse) : rien n'est jamais perdu.
        ///
        /// Utilise quand l'inventaire est plein : le joueur choisit ce qu'il
        /// remplace en selectionnant le slot avant d'appuyer sur Interagir.
        /// </summary>
        /// <param name="remaining">Quantite du nouvel objet qui n'a pas pu entrer.</param>
        /// <returns>Vrai si l'echange a eu lieu.</returns>
        public bool TrySwapSelected(ItemData item, int count, Vector3 dropPosition, out int remaining)
        {
            remaining = count;

            if (item == null || count <= 0 || _slots == null)
            {
                return false;
            }

            InventorySlot slot = SelectedSlot;

            if (slot.IsEmpty)
            {
                return false;
            }

            ItemData previousItem = slot.Item;
            int previousCount = slot.Count;

            if (!previousItem.Droppable)
            {
                if (logToConsole)
                {
                    Debug.Log("[Inventaire] " + previousItem.ItemName + " ne peut pas etre lache : echange impossible.");
                }

                return false;
            }

            // On repose l'ancien objet AVANT de vider le slot : si l'apparition
            // echoue (prefab manquant), l'inventaire reste intact.
            if (SpawnItemInWorld(previousItem, previousCount, dropPosition, Vector3.zero) == null)
            {
                return false;
            }

            int transfer = Mathf.Min(item.MaxStack, count);

            _slots[_selectedIndex] = new InventorySlot(item, transfer);
            remaining = count - transfer;

            PublishChanged();
            PublishSelection();

            EventBus.Publish(new ItemDroppedEvent(player, previousItem, previousCount, dropPosition));
            EventBus.Publish(new ItemPickedUpEvent(player, item, transfer));

            if (logToConsole)
            {
                Debug.Log("[Inventaire] Echange : " + previousItem.ItemName + " repose au sol, " + item.ItemName + " pris en main.");
            }

            return true;
        }

        /// <summary>Lache l'objet selectionne devant le joueur.</summary>
        public void DropSelected()
        {
            InventorySlot slot = SelectedSlot;

            if (slot.IsEmpty || !slot.Item.Droppable)
            {
                return;
            }

            ItemData item = slot.Item;

            Vector3 origin = player != null ? player.EyePosition : transform.position + Vector3.up * 1.5f;
            Vector3 forward = player != null && player.Camera != null ? player.Camera.transform.forward : transform.forward;

            Vector3 position = origin + forward * dropDistance;

            // On evite de lacher l'objet dans un mur.
            RaycastHit hit;

            if (Physics.Raycast(origin, forward, out hit, dropDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                position = hit.point - forward * 0.15f;
            }

            if (SpawnItemInWorld(item, 1, position, forward * dropForce) == null)
            {
                return;
            }

            RemoveAt(_selectedIndex, 1);

            EventBus.Publish(new ItemDroppedEvent(player, item, 1, position));

            if (logToConsole)
            {
                Debug.Log("[Inventaire] " + item.ItemName + " lache au sol.");
            }
        }

        /// <summary>
        /// Fait apparaitre un objet dans le monde, pret a etre ramasse.
        /// Renvoie null si aucun prefab n'est disponible : l'appelant doit alors
        /// annuler son operation pour ne pas faire disparaitre l'objet.
        /// </summary>
        public GameObject SpawnItemInWorld(ItemData item, int count, Vector3 position, Vector3 impulse)
        {
            if (item == null || count <= 0)
            {
                return null;
            }

            GameObject prefab = item.WorldPrefab != null ? item.WorldPrefab : defaultDropPrefab;

            if (prefab == null)
            {
                Debug.LogWarning("[Inventaire] Impossible de poser '" + item.ItemName + "' : aucun World Prefab sur l'objet et aucun Default Drop Prefab sur l'inventaire.", this);
                return null;
            }

            GameObject instance = Instantiate(prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            ItemPickup pickup = instance.GetComponent<ItemPickup>();

            if (pickup == null)
            {
                pickup = instance.GetComponentInChildren<ItemPickup>();
            }

            if (pickup != null)
            {
                pickup.Configure(item, count);
            }
            else
            {
                Debug.LogWarning("[Inventaire] Le prefab '" + prefab.name + "' n'a pas de composant ItemPickup : l'objet ne sera pas ramassable.", instance);
            }

            Rigidbody body = instance.GetComponent<Rigidbody>();

            if (body != null && impulse.sqrMagnitude > 0.0001f)
            {
                body.AddForce(impulse, ForceMode.Impulse);
            }

            return instance;
        }

        // ------------------------------------------------------------------

        private void PublishChanged()
        {
            EventBus.Publish(new InventoryChangedEvent(player, this));
        }

        private void PublishSelection()
        {
            EventBus.Publish(new InventorySelectionChangedEvent(player, _selectedIndex, SelectedItem));
        }
    }
}
