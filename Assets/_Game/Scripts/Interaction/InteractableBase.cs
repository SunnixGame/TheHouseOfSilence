using HouseOfSilence.Core;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.Events;

namespace HouseOfSilence.Interaction
{
    /// <summary>
    /// Base commune a tous les objets interactifs.
    ///
    /// Fournit deja : texte de prompt, etat verrouille, usage unique,
    /// interaction a maintien, surbrillance, UnityEvent pour le cablage
    /// dans l'Inspector, et publication sur l'EventBus.
    ///
    /// Pour creer un nouvel interactif, il suffit d'heriter et de redefinir
    /// OnInteracted(). Exemples a venir : Door, Pickup, LightSwitch, FuseBox,
    /// Drawer, HidingSpot, EscapeDoor.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class InteractableBase : MonoBehaviour, IInteractable
    {
        [Header("Prompt")]
        [Tooltip("Nom de l'objet, affiche au dessus de l'action. Vide = pas de titre.")]
        [SerializeField] protected string displayName = "";

        [Tooltip("Action proposee au joueur quand il vise l'objet.")]
        [SerializeField] protected string promptText = "Interagir";

        [Tooltip("Texte affiche quand l'objet est visible mais bloque (verrouille, deja pris...).")]
        [SerializeField] protected string blockedPromptText = "Bloque";

        [Header("Regles")]
        [Tooltip("Decoche pour desactiver temporairement l'objet sans le supprimer.")]
        [SerializeField] protected bool interactable = true;

        [Tooltip("Si coche, l'objet disparait du systeme apres une seule interaction.")]
        [SerializeField] protected bool singleUse = false;

        [Tooltip("Duree de maintien de la touche, en secondes. 0 = instantane.")]
        [SerializeField, Min(0f)] protected float holdDuration = 0f;

        [Tooltip("Delai minimal entre deux interactions, en secondes.")]
        [SerializeField, Min(0f)] protected float cooldown = 0.25f;

        [Header("Evenements")]
        [Tooltip("Cable dans l'Inspector : permet de declencher n'importe quoi sans ecrire de code.")]
        [SerializeField] protected UnityEvent onInteracted;

        private InteractableHighlight _highlight;
        private float _lastInteractionTime = -999f;
        private bool _consumed;

        /// <summary>Vrai si l'objet a ete consomme (usage unique deja utilise).</summary>
        public bool IsConsumed { get { return _consumed; } }

        /// <summary>Active ou desactive l'objet a chaud (verrouillage par un objectif, un evenement...).</summary>
        public bool Interactable
        {
            get { return interactable; }
            set { interactable = value; }
        }

        public Transform Transform { get { return transform; } }

        public float HoldDuration { get { return holdDuration; } }

        protected virtual void Awake()
        {
            _highlight = GetComponent<InteractableHighlight>();

            if (_highlight == null)
            {
                _highlight = GetComponentInChildren<InteractableHighlight>();
            }
        }

        // ------------------------------------------------------------------
        // IInteractable
        // ------------------------------------------------------------------

        public virtual bool IsFocusable(PlayerCharacter player)
        {
            if (_consumed || !enabled || !gameObject.activeInHierarchy)
            {
                return false;
            }

            return true;
        }

        public virtual bool CanInteract(PlayerCharacter player)
        {
            if (!IsFocusable(player) || !interactable)
            {
                return false;
            }

            if (Time.time - _lastInteractionTime < cooldown)
            {
                return false;
            }

            return true;
        }

        public virtual string GetTitle(PlayerCharacter player)
        {
            return displayName;
        }

        public virtual string GetPrompt(PlayerCharacter player)
        {
            if (!interactable)
            {
                return string.IsNullOrEmpty(blockedPromptText) ? promptText : blockedPromptText;
            }

            return promptText;
        }

        public virtual void OnFocusEnter(PlayerCharacter player)
        {
            if (_highlight != null)
            {
                _highlight.SetHighlighted(true);
            }
        }

        public virtual void OnFocusExit(PlayerCharacter player)
        {
            if (_highlight != null)
            {
                _highlight.SetHighlighted(false);
            }
        }

        public void Interact(PlayerCharacter player)
        {
            if (!CanInteract(player))
            {
                OnInteractionRefused(player);
                return;
            }

            _lastInteractionTime = Time.time;

            OnInteracted(player);

            if (onInteracted != null)
            {
                onInteracted.Invoke();
            }

            EventBus.Publish(new InteractionPerformedEvent(player, this, transform.position));

            if (singleUse)
            {
                _consumed = true;
                OnFocusExit(player);
            }
        }

        public virtual void OnHoldProgress(PlayerCharacter player, float progress) { }

        public virtual void OnHoldCancelled(PlayerCharacter player) { }

        // ------------------------------------------------------------------
        // A redefinir
        // ------------------------------------------------------------------

        /// <summary>Comportement propre a l'objet. C'est la seule methode obligatoire.</summary>
        protected abstract void OnInteracted(PlayerCharacter player);

        /// <summary>
        /// Appele quand le joueur tente d'interagir alors que CanInteract est faux.
        /// Sert a jouer un son de poignee bloquee, un message, etc.
        /// </summary>
        protected virtual void OnInteractionRefused(PlayerCharacter player) { }

        /// <summary>Remet l'objet a son etat initial (nouvelle partie, rechargement).</summary>
        public virtual void ResetInteractable()
        {
            _consumed = false;
            _lastInteractionTime = -999f;

            if (_highlight != null)
            {
                _highlight.SetHighlighted(false);
            }
        }
    }
}
