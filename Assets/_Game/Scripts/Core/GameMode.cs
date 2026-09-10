namespace HouseOfSilence.Core
{
    /// <summary>
    /// Mode de session. Le prototype tourne en SinglePlayer ; Cooperative
    /// sera active quand Netcode for GameObjects sera branche (Phase 18).
    /// </summary>
    public enum GameMode
    {
        SinglePlayer = 0,
        Cooperative = 1
    }

    /// <summary>
    /// Role reseau. Reste sur Offline tant que le multijoueur n'est pas actif.
    /// Le host aura l'autorite sur l'IA, les objectifs et les objets de quete.
    /// </summary>
    public enum NetworkRole
    {
        Offline = 0,
        Host = 1,
        Client = 2
    }

    /// <summary>Helpers de lecture pour le mode courant.</summary>
    public static class GameModeExtensions
    {
        public static bool IsMultiplayer(this GameMode mode)
        {
            return mode == GameMode.Cooperative;
        }

        /// <summary>
        /// Vrai si cette machine a l'autorite sur la simulation (IA, objectifs, evenements).
        /// En solo, toujours vrai.
        /// </summary>
        public static bool HasAuthority(this NetworkRole role)
        {
            return role != NetworkRole.Client;
        }
    }
}
