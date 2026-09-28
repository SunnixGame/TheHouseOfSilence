using System;
using HouseOfSilence.Demon;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Network
{
    /// <summary>
    /// Pont entre le jeu et le reseau, sans dependance a Netcode : le code du jeu signale
    /// ici ce que fait le personnage joue localement (le demon tue, crie, se teleporte,
    /// se deguise) et NetworkGameManager le relaie aux autres joueurs. Hors ligne, personne n'ecoute.
    /// </summary>
    public static class NetBridge
    {
        /// <summary>Une partie en ligne est en cours (les autres personnages sont joues a distance).</summary>
        public static bool Online { get; set; }

        /// <summary>Demon, victime, version jumpscare.</summary>
        public static event Action<DemonKill, SurvivorDeath, bool> DemonKilled;

        public static event Action<DemonPowers> DemonScreamed;

        /// <summary>Demon, point de depart, point d'arrivee.</summary>
        public static event Action<DemonPowers, Vector3, Vector3> DemonTeleported;

        /// <summary>Demon, survivant imite, duree.</summary>
        public static event Action<DemonPowers, PlayerCharacter, float> DemonDisguised;

        public static void RaiseDemonKilled(DemonKill demon, SurvivorDeath victim, bool jumpscare)
        {
            if (Online && DemonKilled != null) DemonKilled(demon, victim, jumpscare);
        }

        public static void RaiseDemonScreamed(DemonPowers demon)
        {
            if (Online && DemonScreamed != null) DemonScreamed(demon);
        }

        public static void RaiseDemonTeleported(DemonPowers demon, Vector3 from, Vector3 to)
        {
            if (Online && DemonTeleported != null) DemonTeleported(demon, from, to);
        }

        public static void RaiseDemonDisguised(DemonPowers demon, PlayerCharacter look, float duration)
        {
            if (Online && DemonDisguised != null) DemonDisguised(demon, look, duration);
        }
    }
}
