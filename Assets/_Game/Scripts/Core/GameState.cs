namespace HouseOfSilence.Core
{
    /// <summary>
    /// Etats globaux de l'application. Un seul est actif a la fois (GameManager.State).
    /// </summary>
    public enum GameState
    {
        /// <summary>Etat initial, avant toute decision (frame 0).</summary>
        Boot = 0,

        /// <summary>Menu principal.</summary>
        MainMenu = 1,

        /// <summary>Chargement d'une scene en cours.</summary>
        Loading = 2,

        /// <summary>Partie en cours, le joueur controle son personnage.</summary>
        Playing = 3,

        /// <summary>Partie en pause (timeScale = 0).</summary>
        Paused = 4,

        /// <summary>Tous les joueurs sont morts / la partie est perdue.</summary>
        GameOver = 5,

        /// <summary>Les joueurs se sont echappes.</summary>
        Victory = 6,

        /// <summary>Fermeture de l'application demandee.</summary>
        Quitting = 7
    }

    /// <summary>
    /// Resultat d'une session. Sera etendu par l'EndingManager (Phase 31).
    /// </summary>
    public enum GameResult
    {
        None = 0,
        Victory = 1,
        Defeat = 2
    }
}
