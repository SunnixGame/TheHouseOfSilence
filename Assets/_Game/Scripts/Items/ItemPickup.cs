using HouseOfSilence.Interaction;
using HouseOfSilence.Inventory;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Items
{
    /// <summary>
    /// Objet pose dans le monde, ramassable par le joueur.
    ///
    /// Herite de InteractableBase : il beneficie donc automatiquement du
    /// viseur, de la surbrillance, du prompt et du cooldown.
    ///
    /// En multijoueur (Phase 18), c'est ce composant qui sera synchronise :
    /// quand un joueur ramasse la cle, l'objet disparait pour tout le monde.
    /// </summary>
    public class ItemPickup : InteractableBase
    {
        [Header("Objet")]
        [SerializeField] private ItemData item;

        [SerializeField, Min(1)] private int count = 1;

        [Header("Comportement")]
        [Tooltip("Detruit l'objet une fois entierement ramasse.")]
        [SerializeField] private bool destroyOnPickup = true;

        [Tooltip("Si l'inventaire est plein, echange cet objet contre celui du slot selectionne.")]
        [SerializeField] private bool allowSwapWhenFull = true;

        [Tooltip("Rotation lente sur place, pour attirer l'oeil.")]
        [SerializeField] private bool spinInPlace = false;

        [SerializeField] private float spinSpeed = 35f;

        public ItemData Item { get { return item; } }
        public int Count { get { return count; } }

        /// <summary>Configure l'objet a la volee (lacher d'inventaire, spawn aleatoire).</summary>
        public void Configure(ItemData newItem, int newCount)
        {
            item = newItem;
            count = Mathf.Max(1, newCount);
        }

        private void Update()
        {
            if (spinInPlace)
            {
                transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
            }
        }

        // ------------------------------------------------------------------

        public override bool IsFocusable(PlayerCharacter player)
        {
            if (item == null || count <= 0)
            {
                return false;
            }

            return base.IsFocusable(player);
        }

        public override bool CanInteract(PlayerCharacter player)
        {
            if (!base.CanInteract(player))
            {
                return false;
            }

            PlayerInventory inventory = GetInventory(player);

            if (inventory == null)
            {
                return false;
            }

            if (!inventory.IsFullFor(item))
            {
                return true;
            }

            // Inventaire plein : l'echange reste possible si le slot selectionne
            // contient un objet que le joueur a le droit de reposer.
            return allowSwapWhenFull && inventory.CanSwapSelected;
        }

        public override string GetTitle(PlayerCharacter player)
        {
            if (item == null)
            {
                return base.GetTitle(player);
            }

            return count > 1 ? item.ItemName + "  x" + count : item.ItemName;
        }

        public override string GetPrompt(PlayerCharacter player)
        {
            if (item == null)
            {
                return base.GetPrompt(player);
            }

            PlayerInventory inventory = GetInventory(player);

            if (inventory == null || !inventory.IsFullFor(item))
            {
                return "Ramasser";
            }

            if (!allowSwapWhenFull)
            {
                return "Inventaire plein";
            }

            ItemData selected = inventory.SelectedItem;

            if (selected == null)
            {
                return "Inventaire plein";
            }

            if (!inventory.CanSwapSelected)
            {
                // Typiquement l'objet maudit : impossible a reposer.
                return "Impossible de lacher " + selected.ItemName;
            }

            return "Echanger avec " + selected.ItemName;
        }

        protected override void OnInteracted(PlayerCharacter player)
        {
            PlayerInventory inventory = GetInventory(player);

            if (inventory == null || item == null)
            {
                return;
            }

            int remaining;
            bool taken;

            if (inventory.IsFullFor(item))
            {
                if (!allowSwapWhenFull)
                {
                    return;
                }

                // L'ancien objet reprend exactement la place de celui-ci.
                taken = inventory.TrySwapSelected(item, count, transform.position, out remaining);
            }
            else
            {
                taken = inventory.TryAdd(item, count, out remaining);
            }

            if (!taken)
            {
                return;
            }

            if (remaining > 0)
            {
                // Ramassage partiel : le reste demeure au sol.
                count = remaining;
                return;
            }

            count = 0;

            if (destroyOnPickup)
            {
                Destroy(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private static PlayerInventory GetInventory(PlayerCharacter player)
        {
            if (player == null)
            {
                return null;
            }

            return player.GetComponent<PlayerInventory>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (item != null && !item.Stackable)
            {
                count = 1;
            }
        }
#endif
    }
}
