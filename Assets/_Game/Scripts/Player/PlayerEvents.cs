using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>Etat de deplacement courant du joueur.</summary>
    public enum PlayerMovementState
    {
        Idle = 0,
        Walking = 1,
        Running = 2,
        Crouching = 3,
        CrouchWalking = 4,
        Airborne = 5
    }

    /// <summary>Publie quand un joueur apparait et s'enregistre aupres du PlayerManager.</summary>
    public readonly struct PlayerSpawnedEvent
    {
        public readonly PlayerCharacter Player;

        public PlayerSpawnedEvent(PlayerCharacter player)
        {
            Player = player;
        }
    }

    /// <summary>Publie quand un joueur disparait (deconnexion, destruction, changement de scene).</summary>
    public readonly struct PlayerDespawnedEvent
    {
        public readonly PlayerCharacter Player;

        public PlayerDespawnedEvent(PlayerCharacter player)
        {
            Player = player;
        }
    }

    /// <summary>Publie a la mort d'un joueur. La Phase 13 y branchera le mode spectateur.</summary>
    public readonly struct PlayerDiedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly string Cause;

        public PlayerDiedEvent(PlayerCharacter player, string cause)
        {
            Player = player;
            Cause = cause;
        }
    }

    /// <summary>Publie a chaque changement de vie (UI, effets de peur...).</summary>
    public readonly struct PlayerHealthChangedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly float Current;
        public readonly float Max;

        public PlayerHealthChangedEvent(PlayerCharacter player, float current, float max)
        {
            Player = player;
            Current = current;
            Max = max;
        }
    }

    /// <summary>Publie a chaque changement d'endurance (barre de stamina du HUD).</summary>
    public readonly struct PlayerStaminaChangedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly float Current;
        public readonly float Max;
        public readonly bool IsExhausted;

        public PlayerStaminaChangedEvent(PlayerCharacter player, float current, float max, bool isExhausted)
        {
            Player = player;
            Current = current;
            Max = max;
            IsExhausted = isExhausted;
        }
    }

    /// <summary>Publie quand l'etat de deplacement change (animations, audio, IA).</summary>
    public readonly struct PlayerMovementStateChangedEvent
    {
        public readonly PlayerCharacter Player;
        public readonly PlayerMovementState Previous;
        public readonly PlayerMovementState Current;

        public PlayerMovementStateChangedEvent(PlayerCharacter player, PlayerMovementState previous, PlayerMovementState current)
        {
            Player = player;
            Previous = previous;
            Current = current;
        }
    }

    /// <summary>
    /// Publie a chaque pas. Le NoiseManager (Phase 44) s'y abonnera pour
    /// alimenter l'ouie de la creature ; l'audio y branchera les sons de pas.
    /// Intensity : 0 (silencieux) a 1 (course).
    /// </summary>
    public readonly struct PlayerFootstepEvent
    {
        public readonly PlayerCharacter Player;
        public readonly Vector3 Position;
        public readonly float Intensity;

        public PlayerFootstepEvent(PlayerCharacter player, Vector3 position, float intensity)
        {
            Player = player;
            Position = position;
            Intensity = intensity;
        }
    }
}
