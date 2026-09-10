using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.Events;

namespace HouseOfSilence.Interaction
{
    /// <summary>
    /// Interactif generique, entierement configurable dans l'Inspector.
    ///
    /// Couvre la majorite des besoins sans ecrire de code :
    /// - bouton a usage unique ;
    /// - interrupteur a deux etats (texte different selon l'etat) ;
    /// - action a maintien (reparation, demarrage) via Hold Duration.
    ///
    /// Sert de banc d'essai pour la Phase 3, et restera utile ensuite pour
    /// tous les petits declencheurs de decor.
    /// </summary>
    public class SimpleInteractable : InteractableBase
    {
        [Header("Mode bascule")]
        [Tooltip("Si coche, l'objet alterne entre deux etats a chaque interaction.")]
        [SerializeField] private bool toggleMode = false;

        [Tooltip("Texte affiche quand l'objet est deja active (mode bascule uniquement).")]
        [SerializeField] private string alternatePromptText = "Desactiver";

        [SerializeField] private bool startActivated = false;

        [Header("Evenements de bascule")]
        [SerializeField] private UnityEvent onActivated;
        [SerializeField] private UnityEvent onDeactivated;

        [Header("Debug")]
        [SerializeField] private bool logToConsole = true;

        private bool _isActivated;

        /// <summary>Etat courant en mode bascule.</summary>
        public bool IsActivated { get { return _isActivated; } }

        protected override void Awake()
        {
            base.Awake();
            _isActivated = startActivated;
        }

        public override string GetPrompt(PlayerCharacter player)
        {
            if (!Interactable)
            {
                return base.GetPrompt(player);
            }

            if (toggleMode && _isActivated && !string.IsNullOrEmpty(alternatePromptText))
            {
                return alternatePromptText;
            }

            return base.GetPrompt(player);
        }

        protected override void OnInteracted(PlayerCharacter player)
        {
            if (toggleMode)
            {
                _isActivated = !_isActivated;

                if (_isActivated)
                {
                    if (onActivated != null)
                    {
                        onActivated.Invoke();
                    }
                }
                else if (onDeactivated != null)
                {
                    onDeactivated.Invoke();
                }
            }

            if (logToConsole)
            {
                string who = player != null ? player.DisplayName : "Joueur";
                string state = toggleMode ? (_isActivated ? " -> ACTIVE" : " -> DESACTIVE") : string.Empty;
                Debug.Log("[Interaction] " + who + " a interagi avec '" + name + "'" + state, this);
            }
        }

        protected override void OnInteractionRefused(PlayerCharacter player)
        {
            if (logToConsole)
            {
                Debug.Log("[Interaction] '" + name + "' refuse l'interaction (verrouille ou en cooldown).", this);
            }
        }

        public override void ResetInteractable()
        {
            base.ResetInteractable();
            _isActivated = startActivated;
        }
    }
}
