using HouseOfSilence.Player;

namespace HouseOfSilence.Horror
{
    /// <summary>Paliers de peur, du plus calme au plus intense.</summary>
    public enum FearLevel
    {
        /// <summary>0 - 20</summary>
        Calm = 0,

        /// <summary>20 - 40</summary>
        Worried = 1,

        /// <summary>40 - 60</summary>
        Stressed = 2,

        /// <summary>60 - 80</summary>
        Afraid = 3,

        /// <summary>80 - 100</summary>
        Panic = 4
    }

    /// <summary>
    /// Publie quand la peur varie de maniere significative (pas a chaque frame).
    /// Le HUD, l'audio et les effets s'y abonnent.
    /// </summary>
    public readonly struct FearChangedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly float Current;
        public readonly float Normalized;
        public readonly FearLevel Level;

        public FearChangedEvent(PlayerCharacter player, float current, float normalized, FearLevel level)
        {
            Player = player;
            Current = current;
            Normalized = normalized;
            Level = level;
        }
    }

    /// <summary>
    /// Publie uniquement au franchissement d'un palier. C'est sur cet evenement
    /// que les hallucinations (Phase 14) et la musique dynamique (Phase 14)
    /// se brancheront.
    /// </summary>
    public readonly struct FearLevelChangedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly FearLevel Previous;
        public readonly FearLevel Current;

        public FearLevelChangedEvent(PlayerCharacter player, FearLevel previous, FearLevel current)
        {
            Player = player;
            Previous = previous;
            Current = current;
        }

        public bool IsRising { get { return Current > Previous; } }
    }
}
