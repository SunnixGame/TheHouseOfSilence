using UnityEngine;

namespace HouseOfSilence.Doors
{
    /// <summary>Etat d'animation d'une porte.</summary>
    public enum DoorState
    {
        Closed = 0,
        Opening = 1,
        Open = 2,
        Closing = 3,
        Broken = 4
    }

    /// <summary>Qui est a l'origine de l'action sur la porte.</summary>
    public enum DoorActor
    {
        Player = 0,
        Creature = 1,
        Script = 2,
        HorrorEvent = 3
    }

    /// <summary>Publie a chaque changement d'etat d'une porte.</summary>
    public readonly struct DoorStateChangedEvent
    {
        public readonly DoorBase Door;
        public readonly DoorState Previous;
        public readonly DoorState Current;
        public readonly DoorActor Actor;

        public DoorStateChangedEvent(DoorBase door, DoorState previous, DoorState current, DoorActor actor)
        {
            Door = door;
            Previous = previous;
            Current = current;
            Actor = actor;
        }
    }

    /// <summary>Publie quand une porte est verrouillee ou deverrouillee.</summary>
    public readonly struct DoorLockChangedEvent
    {
        public readonly DoorBase Door;
        public readonly bool IsLocked;

        public DoorLockChangedEvent(DoorBase door, bool isLocked)
        {
            Door = door;
            IsLocked = isLocked;
        }
    }

    /// <summary>Publie quand une porte cede (creature, pied-de-biche).</summary>
    public readonly struct DoorBrokenEvent
    {
        public readonly DoorBase Door;
        public readonly Vector3 Position;
        public readonly DoorActor Actor;

        public DoorBrokenEvent(DoorBase door, Vector3 position, DoorActor actor)
        {
            Door = door;
            Position = position;
            Actor = actor;
        }
    }
}
