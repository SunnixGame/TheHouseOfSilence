using HouseOfSilence.Items;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Inventory
{
    /// <summary>
    /// Un emplacement d'inventaire. Structure legere, copiee par valeur :
    /// le HUD peut la lire sans risquer de modifier l'inventaire.
    /// </summary>
    [System.Serializable]
    public struct InventorySlot
    {
        public ItemData Item;
        public int Count;

        public bool IsEmpty { get { return Item == null || Count <= 0; } }

        public InventorySlot(ItemData item, int count)
        {
            Item = item;
            Count = count;
        }

        public static InventorySlot Empty { get { return new InventorySlot(null, 0); } }
    }

    /// <summary>Publie a chaque modification du contenu de l'inventaire.</summary>
    public readonly struct InventoryChangedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly PlayerInventory Inventory;

        public InventoryChangedEvent(PlayerCharacter player, PlayerInventory inventory)
        {
            Player = player;
            Inventory = inventory;
        }
    }

    /// <summary>Publie quand le joueur change de slot selectionne.</summary>
    public readonly struct InventorySelectionChangedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly int SlotIndex;
        public readonly ItemData Item;

        public InventorySelectionChangedEvent(PlayerCharacter player, int slotIndex, ItemData item)
        {
            Player = player;
            SlotIndex = slotIndex;
            Item = item;
        }
    }

    /// <summary>Publie quand un objet entre dans l'inventaire. Ecoute par les objectifs (Phase 6).</summary>
    public readonly struct ItemPickedUpEvent
    {
        public readonly PlayerCharacter Player;
        public readonly ItemData Item;
        public readonly int Count;

        public ItemPickedUpEvent(PlayerCharacter player, ItemData item, int count)
        {
            Player = player;
            Item = item;
            Count = count;
        }
    }

    /// <summary>
    /// Publie quand le joueur utilise un objet (clic gauche).
    /// La lampe torche, la radio ou le briquet s'y abonneront : l'inventaire
    /// n'a jamais besoin de connaitre leur comportement.
    /// </summary>
    public readonly struct ItemUsedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly ItemData Item;
        public readonly int SlotIndex;

        public ItemUsedEvent(PlayerCharacter player, ItemData item, int slotIndex)
        {
            Player = player;
            Item = item;
            SlotIndex = slotIndex;
        }
    }

    /// <summary>Publie quand un objet est lache au sol.</summary>
    public readonly struct ItemDroppedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly ItemData Item;
        public readonly int Count;
        public readonly Vector3 Position;

        public ItemDroppedEvent(PlayerCharacter player, ItemData item, int count, Vector3 position)
        {
            Player = player;
            Item = item;
            Count = count;
            Position = position;
        }
    }

    /// <summary>Publie quand un ramassage echoue faute de place (retour au joueur).</summary>
    public readonly struct InventoryFullEvent
    {
        public readonly PlayerCharacter Player;
        public readonly ItemData Item;

        public InventoryFullEvent(PlayerCharacter player, ItemData item)
        {
            Player = player;
            Item = item;
        }
    }
}
