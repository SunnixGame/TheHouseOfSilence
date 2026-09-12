using UnityEngine;

namespace HouseOfSilence.Lights
{
    /// <summary>Etat logique d'une lampe.</summary>
    public enum LightState
    {
        Off = 0,
        On = 1,
        Flickering = 2,
        Broken = 3
    }

    /// <summary>Publie a chaque changement d'etat d'une lampe. La peur et l'audio s'y abonnent.</summary>
    public readonly struct LightStateChangedEvent
    {
        public readonly LightController Light;
        public readonly LightState Previous;
        public readonly LightState Current;
        public readonly Vector3 Position;

        public LightStateChangedEvent(LightController light, LightState previous, LightState current, Vector3 position)
        {
            Light = light;
            Previous = previous;
            Current = current;
            Position = position;
        }
    }

    /// <summary>Publie quand le courant general est retabli ou coupe.</summary>
    public readonly struct PowerStateChangedEvent
    {
        public readonly bool IsPowered;

        public PowerStateChangedEvent(bool isPowered)
        {
            IsPowered = isPowered;
        }
    }

    /// <summary>Publie quand le fusible est installe dans le tableau electrique.</summary>
    public readonly struct FuseInstalledEvent
    {
        public readonly Vector3 Position;

        public FuseInstalledEvent(Vector3 position)
        {
            Position = position;
        }
    }
}
