using HouseOfSilence.Core;
using HouseOfSilence.Interaction;
using HouseOfSilence.Inventory;
using HouseOfSilence.Items;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Lights
{
    /// <summary>
    /// Tableau electrique : reclame un fusible de l'inventaire.
    /// Une fois le fusible en place, le disjoncteur (PowerSwitch) peut etre abaisse.
    ///
    /// C'est l'objectif 3 du vertical slice ("Reparer le tableau electrique").
    /// Poser un ObjectiveTrigger dessus suffit a le relier.
    /// </summary>
    public class FuseBox : InteractableBase
    {
        [Header("Fusible")]
        [Tooltip("Objet requis. Vide = aucun objet necessaire.")]
        [SerializeField] private ItemData requiredItem;

        [SerializeField, Min(1)] private int requiredCount = 1;

        [Tooltip("Retire l'objet de l'inventaire une fois installe.")]
        [SerializeField] private bool consumeItem = true;

        [Header("Textes")]
        [SerializeField] private string installPrompt = "Installer le fusible";
        [SerializeField] private string missingItemPrompt = "Il manque un fusible";
        [SerializeField] private string repairedPrompt = "Tableau repare";

        [Header("Bruit")]
        [SerializeField, Min(0f)] private float noiseRadius = 6f;

        private bool _repaired;

        /// <summary>Vrai une fois le fusible en place.</summary>
        public bool IsRepaired { get { return _repaired; } }

        protected override void Awake()
        {
            base.Awake();

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = "Tableau electrique";
            }

            if (holdDuration <= 0f)
            {
                holdDuration = 1.5f;
            }
        }

        private void Start()
        {
            LightManager manager = LightManager.HasInstance ? LightManager.Instance : null;

            if (manager != null && manager.IsFuseInstalled)
            {
                _repaired = true;
            }
        }

        private bool HasRequiredItem(PlayerCharacter player)
        {
            if (requiredItem == null)
            {
                return true;
            }

            PlayerInventory inventory = player != null ? player.GetComponent<PlayerInventory>() : null;

            return inventory != null && inventory.CountOf(requiredItem) >= requiredCount;
        }

        public override bool CanInteract(PlayerCharacter player)
        {
            if (_repaired)
            {
                return false;
            }

            return base.CanInteract(player) && HasRequiredItem(player);
        }

        public override string GetPrompt(PlayerCharacter player)
        {
            if (_repaired)
            {
                return repairedPrompt;
            }

            if (!Interactable)
            {
                return base.GetPrompt(player);
            }

            return HasRequiredItem(player) ? installPrompt : missingItemPrompt;
        }

        protected override void OnInteracted(PlayerCharacter player)
        {
            if (requiredItem != null && consumeItem)
            {
                PlayerInventory inventory = player != null ? player.GetComponent<PlayerInventory>() : null;

                if (inventory == null || !inventory.Remove(requiredItem, requiredCount))
                {
                    return;
                }
            }

            _repaired = true;

            LightManager manager = LightManager.Instance;

            if (manager != null)
            {
                manager.SetFuseInstalled(true, transform.position);
            }

            Noise.Emit(transform.position, noiseRadius, NoiseType.Interaction, gameObject);
            Debug.Log("[Lumieres] Tableau electrique repare.", this);
        }
    }
}
