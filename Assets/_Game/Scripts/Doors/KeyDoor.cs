using HouseOfSilence.Inventory;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Doors
{
    /// <summary>
    /// Porte qui reclame une cle de l'inventaire.
    ///
    /// La porte ne connait aucun ItemData : elle demande une SERRURE par son
    /// identifiant ("office", "basement"). N'importe quel objet dont le keyId
    /// correspond l'ouvre. On peut donc ajouter un passe-partout plus tard
    /// sans modifier une seule porte.
    /// </summary>
    public class KeyDoor : DoorBase
    {
        [Header("Serrure")]
        [Tooltip("Identifiant de la serrure. Doit correspondre au Key Id d'un ItemData.")]
        [SerializeField] private string lockId = "office";

        [Tooltip("La cle est retiree de l'inventaire apres usage.")]
        [SerializeField] private bool consumeKey = false;

        [Tooltip("Une fois ouverte, la porte reste deverrouillee.")]
        [SerializeField] private bool stayUnlocked = true;

        [Header("Textes")]
        [SerializeField] private string missingKeyPrompt = "Verrouillee - il faut une cle";
        [SerializeField] private string unlockPrompt = "Deverrouiller";

        /// <summary>Identifiant de la serrure, en lecture seule.</summary>
        public string LockId { get { return lockId; } }

        protected override void Awake()
        {
            base.Awake();

            // Une porte a cle demarre necessairement verrouillee.
            if (!IsLocked)
            {
                Lock();
            }
        }

        protected override bool CanUnlockWith(PlayerCharacter player)
        {
            PlayerInventory inventory = GetInventory(player);

            if (inventory == null)
            {
                return false;
            }

            return inventory.HasKeyFor(lockId);
        }

        protected override bool TryUnlockWith(PlayerCharacter player)
        {
            PlayerInventory inventory = GetInventory(player);

            if (inventory == null)
            {
                return false;
            }

            if (consumeKey)
            {
                if (!inventory.ConsumeKeyFor(lockId))
                {
                    return false;
                }

                Debug.Log("[Porte] Serrure '" + lockId + "' ouverte, la cle est restee dedans.", this);
                return true;
            }

            if (!inventory.HasKeyFor(lockId))
            {
                return false;
            }

            Debug.Log("[Porte] Serrure '" + lockId + "' deverrouillee.", this);
            return true;
        }

        protected override string GetLockedPrompt(PlayerCharacter player)
        {
            return string.IsNullOrEmpty(missingKeyPrompt) ? base.GetLockedPrompt(player) : missingKeyPrompt;
        }

        protected override string GetUnlockPrompt(PlayerCharacter player)
        {
            return string.IsNullOrEmpty(unlockPrompt) ? base.GetUnlockPrompt(player) : unlockPrompt;
        }

        protected override void OnClosed(DoorActor actor)
        {
            base.OnClosed(actor);

            if (!stayUnlocked)
            {
                Lock();
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
    }
}
