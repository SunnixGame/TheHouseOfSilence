using UnityEngine;

namespace HouseOfSilence.Core
{
    /// <summary>
    /// Nature d'un bruit emis dans le monde. Sert a moduler la reaction de
    /// la creature : un pas discret n'a pas le meme poids qu'une porte qui claque.
    /// </summary>
    public enum NoiseType
    {
        Footstep = 0,
        Run = 1,
        Door = 2,
        Object = 3,
        Interaction = 4,
        Player = 5,
        Monster = 6,
        Explosion = 7,
        Event = 8
    }

    /// <summary>
    /// Un bruit vient d'etre emis quelque part dans la maison.
    ///
    /// L'ouie de la creature (Phase 17) et le NoiseManager (Phase 44) s'y
    /// abonneront. Emettre un bruit ne coute donc rien tant que personne
    /// n'ecoute : on peut en instrumenter le jeu des maintenant.
    /// </summary>
    public readonly struct NoiseEmittedEvent
    {
        /// <summary>Position d'origine du bruit.</summary>
        public readonly Vector3 Position;

        /// <summary>Rayon d'audibilite, en metres.</summary>
        public readonly float Radius;

        public readonly NoiseType Type;

        /// <summary>Objet a l'origine du bruit (peut etre null).</summary>
        public readonly GameObject Source;

        /// <summary>Instant d'emission (Time.time).</summary>
        public readonly float Timestamp;

        public NoiseEmittedEvent(Vector3 position, float radius, NoiseType type, GameObject source, float timestamp)
        {
            Position = position;
            Radius = radius;
            Type = type;
            Source = source;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Point d'entree unique pour emettre un bruit.
    ///
    /// Tout le jeu passe par Noise.Emit(...). En Phase 44, le NoiseManager
    /// s'abonnera a NoiseEmittedEvent pour tenir un historique et repondre
    /// aux requetes de l'IA : cette API n'aura pas a changer.
    /// </summary>
    public static class Noise
    {
        /// <summary>Emet un bruit audible dans un rayon donne.</summary>
        public static void Emit(Vector3 position, float radius, NoiseType type, GameObject source = null)
        {
            if (radius <= 0f)
            {
                return;
            }

            EventBus.Publish(new NoiseEmittedEvent(position, radius, type, source, Time.time));
        }
    }
}
