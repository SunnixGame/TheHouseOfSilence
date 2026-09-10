namespace HouseOfSilence.Core
{
    /// <summary>Publie a chaque changement d'etat global du jeu.</summary>
    public readonly struct GameStateChangedEvent
    {
        public readonly GameState Previous;
        public readonly GameState Current;

        public GameStateChangedEvent(GameState previous, GameState current)
        {
            Previous = previous;
            Current = current;
        }
    }

    /// <summary>Publie quand une session demarre reellement (joueur jouable).</summary>
    public readonly struct GameStartedEvent
    {
        public readonly GameMode Mode;
        public readonly NetworkRole Role;

        public GameStartedEvent(GameMode mode, NetworkRole role)
        {
            Mode = mode;
            Role = role;
        }
    }

    /// <summary>Publie a chaque pause / reprise.</summary>
    public readonly struct GamePauseChangedEvent
    {
        public readonly bool IsPaused;

        public GamePauseChangedEvent(bool isPaused)
        {
            IsPaused = isPaused;
        }
    }

    /// <summary>Publie a la fin d'une session (victoire ou defaite).</summary>
    public readonly struct GameEndedEvent
    {
        public readonly GameResult Result;
        public readonly float PlayTime;

        public GameEndedEvent(GameResult result, float playTime)
        {
            Result = result;
            PlayTime = playTime;
        }
    }

    /// <summary>Publie au debut d'un chargement de scene.</summary>
    public readonly struct SceneLoadStartedEvent
    {
        public readonly string SceneName;

        public SceneLoadStartedEvent(string sceneName)
        {
            SceneName = sceneName;
        }
    }

    /// <summary>Publie pendant le chargement (0 a 1). Utilise par l'ecran de chargement.</summary>
    public readonly struct SceneLoadProgressEvent
    {
        public readonly string SceneName;
        public readonly float Progress;

        public SceneLoadProgressEvent(string sceneName, float progress)
        {
            SceneName = sceneName;
            Progress = progress;
        }
    }

    /// <summary>Publie quand la scene est chargee et active.</summary>
    public readonly struct SceneLoadCompletedEvent
    {
        public readonly string SceneName;
        public readonly bool Success;

        public SceneLoadCompletedEvent(string sceneName, bool success)
        {
            SceneName = sceneName;
            Success = success;
        }
    }
}
