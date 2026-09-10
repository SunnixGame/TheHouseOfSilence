using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Interaction
{
    /// <summary>
    /// Publie quand la cible sous le viseur change (y compris vers "aucune cible").
    /// L'UI d'interaction se contente d'ecouter cet evenement : elle n'a besoin
    /// de connaitre ni le joueur, ni les objets interactifs.
    /// </summary>
    public readonly struct InteractionTargetChangedEvent
    {
        public readonly PlayerCharacter Player;

        /// <summary>Cible courante, ou null si le viseur ne vise rien d'interactif.</summary>
        public readonly IInteractable Target;

        /// <summary>Nom de l'objet, affiche au dessus de l'action. Peut etre vide.</summary>
        public readonly string Title;

        /// <summary>Action proposee, vide si aucune cible.</summary>
        public readonly string Prompt;

        /// <summary>Faux si l'objet est visible mais bloque (porte verrouillee, main pleine...).</summary>
        public readonly bool CanInteract;

        /// <summary>Duree de maintien requise, 0 si instantane.</summary>
        public readonly float HoldDuration;

        public InteractionTargetChangedEvent(PlayerCharacter player, IInteractable target, string title, string prompt, bool canInteract, float holdDuration)
        {
            Player = player;
            Target = target;
            Title = title;
            Prompt = prompt;
            CanInteract = canInteract;
            HoldDuration = holdDuration;
        }

        public bool HasTarget { get { return Target != null; } }
    }

    /// <summary>
    /// Publie quand une interaction aboutit.
    /// Les objectifs (Phase 6), l'audio et l'IA (bruit) s'y abonneront.
    /// </summary>
    public readonly struct InteractionPerformedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly IInteractable Target;
        public readonly Vector3 Position;

        public InteractionPerformedEvent(PlayerCharacter player, IInteractable target, Vector3 position)
        {
            Player = player;
            Target = target;
            Position = position;
        }
    }

    /// <summary>Progression d'une interaction a maintien, de 0 a 1 (barre de progression du HUD).</summary>
    public readonly struct InteractionHoldProgressEvent
    {
        public readonly PlayerCharacter Player;
        public readonly float Progress;
        public readonly bool IsHolding;

        public InteractionHoldProgressEvent(PlayerCharacter player, float progress, bool isHolding)
        {
            Player = player;
            Progress = progress;
            IsHolding = isHolding;
        }
    }
}
