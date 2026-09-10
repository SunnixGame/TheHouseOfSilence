using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Interaction
{
    /// <summary>
    /// Contrat de tout objet avec lequel le joueur peut interagir :
    /// portes, objets a ramasser, interrupteurs, tableaux electriques,
    /// tiroirs, cachettes, porte de sortie...
    ///
    /// Dans la quasi-totalite des cas, on n'implemente PAS cette interface
    /// directement : on herite de InteractableBase, qui fournit deja le
    /// comportement commun (texte, verrouillage, maintien, surbrillance,
    /// UnityEvent, usage unique).
    ///
    /// Implementer l'interface a la main n'a de sens que pour greffer
    /// l'interaction sur une classe qui herite deja d'autre chose.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Transform de l'objet : utilise pour la distance et les effets.</summary>
        Transform Transform { get; }

        /// <summary>
        /// Duree de maintien de la touche, en secondes. 0 = interaction instantanee.
        /// Utile pour reparer un tableau electrique ou demarrer un generateur.
        /// </summary>
        float HoldDuration { get; }

        /// <summary>
        /// Vrai si l'objet doit apparaitre comme cible sous le viseur.
        /// Un objet deja consomme renvoie false et devient invisible pour le systeme.
        /// </summary>
        bool IsFocusable(PlayerCharacter player);

        /// <summary>
        /// Vrai si l'interaction peut aboutir maintenant.
        /// Une porte verrouillee reste focalisable (pour afficher "Verrouillee")
        /// mais renvoie false ici.
        /// </summary>
        bool CanInteract(PlayerCharacter player);

        /// <summary>
        /// Nom de l'objet vise, affiche AU DESSUS de l'action.
        /// Sert a lever l'ambiguite quand un meme objet propose plusieurs
        /// actions selon son etat : on lit "Porte du bureau" puis "Fermer",
        /// au lieu d'un simple "Fermer" hors contexte.
        /// Renvoyer une chaine vide pour n'afficher que l'action.
        /// </summary>
        string GetTitle(PlayerCharacter player);

        /// <summary>Action proposee, ex : "Ouvrir", "Ramasser", "Verrouillee".</summary>
        string GetPrompt(PlayerCharacter player);

        /// <summary>Le viseur vient de se poser sur l'objet.</summary>
        void OnFocusEnter(PlayerCharacter player);

        /// <summary>Le viseur vient de quitter l'objet.</summary>
        void OnFocusExit(PlayerCharacter player);

        /// <summary>Interaction validee (appui court, ou maintien termine).</summary>
        void Interact(PlayerCharacter player);

        /// <summary>Progression du maintien, de 0 a 1. Appele uniquement si HoldDuration &gt; 0.</summary>
        void OnHoldProgress(PlayerCharacter player, float progress);

        /// <summary>Le maintien a ete interrompu avant la fin.</summary>
        void OnHoldCancelled(PlayerCharacter player);
    }
}
