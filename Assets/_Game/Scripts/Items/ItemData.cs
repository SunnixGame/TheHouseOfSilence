using UnityEngine;

namespace HouseOfSilence.Items
{
    /// <summary>Categories d'objets. Sert au tri, au HUD et aux conditions d'objectifs.</summary>
    public enum ItemType
    {
        Misc = 0,
        Key = 1,
        Fuse = 2,
        Tool = 3,
        QuestItem = 4,
        Consumable = 5
    }

    /// <summary>
    /// Definition d'un objet. C'est une DONNEE, jamais un comportement :
    /// un meme ItemData est partage par toutes les instances du jeu.
    ///
    /// Creation : clic droit dans le Project >
    /// Create > House of Silence > Item Data
    ///
    /// Ranger les assets dans Assets/_Game/ScriptableObjects/Items/.
    /// </summary>
    [CreateAssetMenu(fileName = "Item_New", menuName = "House of Silence/Item Data", order = 0)]
    public class ItemData : ScriptableObject
    {
        [Header("Identite")]
        [Tooltip("Identifiant technique, unique et stable. Sert aux sauvegardes et au reseau.")]
        [SerializeField] private string itemId = "item_new";

        [Tooltip("Nom affiche au joueur.")]
        [SerializeField] private string itemName = "Objet";

        [TextArea(2, 4)]
        [SerializeField] private string description = "";

        [SerializeField] private Sprite icon;

        [SerializeField] private ItemType itemType = ItemType.Misc;

        [Header("Empilement")]
        [SerializeField] private bool stackable = false;

        [SerializeField, Min(1)] private int maxStack = 1;

        [Header("Utilisation")]
        [Tooltip("L'objet peut etre utilise avec le clic gauche.")]
        [SerializeField] private bool usable = false;

        [Tooltip("L'objet est retire de l'inventaire apres utilisation.")]
        [SerializeField] private bool consumeOnUse = false;

        [Tooltip("L'objet peut etre lache au sol.")]
        [SerializeField] private bool droppable = true;

        [Header("Serrures")]
        [Tooltip("Identifiant de serrure ouverte par cet objet. Vide = n'ouvre rien. Ex : 'office', 'basement'.")]
        [SerializeField] private string keyId = "";

        [Header("Monde")]
        [Tooltip("Prefab pose au sol quand l'objet est lache. Laisser vide pour utiliser le prefab par defaut.")]
        [SerializeField] private GameObject worldPrefab;

        [Header("Audio")]
        [Tooltip("Emplacements prevus pour la Phase 14. Peuvent rester vides.")]
        [SerializeField] private AudioClip pickupSound;

        [SerializeField] private AudioClip useSound;

        // ------------------------------------------------------------------

        public string ItemId { get { return itemId; } }
        public string ItemName { get { return itemName; } }
        public string Description { get { return description; } }
        public Sprite Icon { get { return icon; } }
        public ItemType Type { get { return itemType; } }
        public bool Stackable { get { return stackable; } }
        public int MaxStack { get { return stackable ? Mathf.Max(1, maxStack) : 1; } }
        public bool Usable { get { return usable; } }
        public bool ConsumeOnUse { get { return consumeOnUse; } }
        public bool Droppable { get { return droppable; } }
        public string KeyId { get { return keyId; } }
        public GameObject WorldPrefab { get { return worldPrefab; } }
        public AudioClip PickupSound { get { return pickupSound; } }
        public AudioClip UseSound { get { return useSound; } }

        /// <summary>Vrai si cet objet ouvre la serrure demandee.</summary>
        public bool OpensLock(string lockId)
        {
            if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(lockId))
            {
                return false;
            }

            return string.Equals(keyId, lockId, System.StringComparison.OrdinalIgnoreCase);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(itemId))
            {
                itemId = name.ToLowerInvariant();
            }

            if (!stackable)
            {
                maxStack = 1;
            }
        }
#endif
    }
}
