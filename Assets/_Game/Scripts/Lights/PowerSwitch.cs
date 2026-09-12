using HouseOfSilence.Core;
using HouseOfSilence.Interaction;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Lights
{
    /// <summary>
    /// Disjoncteur general. Retablit (ou coupe) le courant de toute la maison.
    ///
    /// Refuse de fonctionner tant que le fusible n'est pas installe si
    /// Require Fuse est coche : le joueur voit "Il manque un fusible" et
    /// comprend qu'il lui faut d'abord reparer le tableau.
    ///
    /// C'est l'objectif 4 du vertical slice ("Retablir l'electricite").
    /// Poser un ObjectiveTrigger dessus suffit a le relier.
    /// </summary>
    public class PowerSwitch : InteractableBase
    {
        [Header("Regles")]
        [SerializeField] private bool requireFuse = true;

        [Tooltip("Permet aussi de couper le courant une fois retabli (utile pour tester).")]
        [SerializeField] private bool allowTurningOff = true;

        [Header("Textes")]
        [SerializeField] private string powerOnPrompt = "Retablir le courant";
        [SerializeField] private string powerOffPrompt = "Couper le courant";
        [SerializeField] private string missingFusePrompt = "Il manque un fusible";

        [Header("Bruit")]
        [Tooltip("Un disjoncteur qui claque s'entend dans toute la maison.")]
        [SerializeField, Min(0f)] private float noiseRadius = 16f;

        protected override void Awake()
        {
            base.Awake();

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = "Disjoncteur";
            }

            if (holdDuration <= 0f)
            {
                holdDuration = 1f;
            }
        }

        private static LightManager Manager
        {
            get { return LightManager.HasInstance ? LightManager.Instance : null; }
        }

        private bool FuseReady()
        {
            if (!requireFuse)
            {
                return true;
            }

            LightManager manager = Manager;
            return manager == null || manager.IsFuseInstalled;
        }

        public override bool CanInteract(PlayerCharacter player)
        {
            if (!base.CanInteract(player))
            {
                return false;
            }

            LightManager manager = Manager;

            if (manager != null && manager.IsPowered)
            {
                return allowTurningOff;
            }

            return FuseReady();
        }

        public override string GetPrompt(PlayerCharacter player)
        {
            if (!Interactable)
            {
                return base.GetPrompt(player);
            }

            LightManager manager = Manager;

            if (manager != null && manager.IsPowered)
            {
                return allowTurningOff ? powerOffPrompt : "Courant retabli";
            }

            return FuseReady() ? powerOnPrompt : missingFusePrompt;
        }

        protected override void OnInteracted(PlayerCharacter player)
        {
            LightManager manager = LightManager.Instance;

            if (manager == null)
            {
                return;
            }

            manager.SetPowered(!manager.IsPowered);
            Noise.Emit(transform.position, noiseRadius, NoiseType.Interaction, gameObject);
        }
    }
}
