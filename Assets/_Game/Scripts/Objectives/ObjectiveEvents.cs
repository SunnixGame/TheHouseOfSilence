namespace HouseOfSilence.Objectives
{
    /// <summary>Etat d'un objectif dans la partie en cours.</summary>
    public enum ObjectiveState
    {
        /// <summary>Pas encore accessible : les objectifs precedents ne sont pas finis.</summary>
        Locked = 0,

        /// <summary>En cours. C'est celui affiche dans le HUD.</summary>
        Active = 1,

        /// <summary>Termine.</summary>
        Completed = 2,

        /// <summary>Rate definitivement (coequipier mort avec l'objet, objet detruit...).</summary>
        Failed = 3
    }

    /// <summary>Publie quand un objectif devient actif.</summary>
    public readonly struct ObjectiveActivatedEvent
    {
        public readonly ObjectiveData Objective;
        public readonly int Index;
        public readonly int Total;

        public ObjectiveActivatedEvent(ObjectiveData objective, int index, int total)
        {
            Objective = objective;
            Index = index;
            Total = total;
        }
    }

    /// <summary>Publie quand un objectif est termine. Les portes d'objectif s'y abonnent.</summary>
    public readonly struct ObjectiveCompletedEvent
    {
        public readonly ObjectiveData Objective;
        public readonly string ObjectiveId;

        public ObjectiveCompletedEvent(ObjectiveData objective, string objectiveId)
        {
            Objective = objective;
            ObjectiveId = objectiveId;
        }
    }

    /// <summary>Publie quand un objectif echoue.</summary>
    public readonly struct ObjectiveFailedEvent
    {
        public readonly ObjectiveData Objective;
        public readonly string ObjectiveId;

        public ObjectiveFailedEvent(ObjectiveData objective, string objectiveId)
        {
            Objective = objective;
            ObjectiveId = objectiveId;
        }
    }

    /// <summary>
    /// Publie quand toute la chaine est terminee.
    /// La Phase 30 y branchera l'ouverture de la sortie et le passage de la
    /// maison en etat de panique.
    /// </summary>
    public readonly struct AllObjectivesCompletedEvent
    {
        public readonly int CompletedCount;

        public AllObjectivesCompletedEvent(int completedCount)
        {
            CompletedCount = completedCount;
        }
    }

    /// <summary>Publie quand l'indice de l'objectif courant devient disponible.</summary>
    public readonly struct ObjectiveHintEvent
    {
        public readonly ObjectiveData Objective;
        public readonly string Hint;

        public ObjectiveHintEvent(ObjectiveData objective, string hint)
        {
            Objective = objective;
            Hint = hint;
        }
    }
}
