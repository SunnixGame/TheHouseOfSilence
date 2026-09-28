using System.Collections.Generic;

namespace HouseOfSilence.Network
{
    /// <summary>Preference de role choisie dans le lobby.</summary>
    public enum RolePreference : byte
    {
        Any = 0,
        Demon = 1,
        Survivor = 2
    }

    /// <summary>Role reellement attribue (par l'hote uniquement).</summary>
    public enum PlayerRole : byte
    {
        None = 0,
        Survivor = 1,
        Demon = 2
    }

    /// <summary>Joueur du lobby : identite et preferences (tenu a jour par l'hote).</summary>
    public class LobbyPlayer
    {
        public ulong ClientId;
        public string Name = "Joueur";
        /// <summary>Survivant souhaite (index parmi les survivants), -1 = peu importe.</summary>
        public int CharacterPref = -1;
        public RolePreference RolePref = RolePreference.Any;
        /// <summary>Bot controle par l'IA, sur la machine de l'hote.</summary>
        public bool IsBot;
    }

    /// <summary>
    /// Attribution des roles et des personnages, calculee par l'hote seul :
    ///  - exactement 1 demon (s'il y a au moins deux joueurs ; seul, le joueur choisit) ;
    ///  - le demon est pris parmi ceux qui le veulent, sinon parmi les "peu importe",
    ///    sinon parmi les autres ; a egalite, celui qui l'a ete le moins souvent
    ///    (equitable d'une partie a l'autre), puis au hasard ;
    ///  - chaque survivant recoit son personnage prefere s'il est libre (conflits tires
    ///    au hasard), sinon un personnage libre.
    /// Les clients ne font qu'envoyer leurs preferences : ils ne choisissent jamais leur role.
    /// </summary>
    public static class RoleManager
    {
        /// <summary>
        /// Renvoie clientId -> index du personnage (dans la liste 'survivorIndices' + 'demonIndex').
        /// 'demonTurns' : nombre de fois ou chaque joueur a deja ete le demon (mis a jour ici).
        /// </summary>
        public static Dictionary<ulong, int> Assign(
            List<LobbyPlayer> players,
            List<int> survivorIndices,
            int demonIndex,
            Dictionary<ulong, int> demonTurns,
            System.Random rng)
        {
            Dictionary<ulong, int> result = new Dictionary<ulong, int>();
            if (players.Count == 0) return result;

            List<LobbyPlayer> survivors = new List<LobbyPlayer>(players);

            // 1. Le demon.
            if (demonIndex >= 0 && (players.Count >= 2 || players[0].RolePref == RolePreference.Demon))
            {
                LobbyPlayer demon = PickDemon(players, demonTurns, rng);
                result[demon.ClientId] = demonIndex;
                survivors.Remove(demon);

                int turns;
                demonTurns.TryGetValue(demon.ClientId, out turns);
                demonTurns[demon.ClientId] = turns + 1;
            }

            // 2. Les survivants : d'abord les preferences (conflits au hasard), puis le reste.
            List<int> free = new List<int>(survivorIndices);
            Shuffle(survivors, rng);

            foreach (LobbyPlayer p in survivors)
            {
                if (p.CharacterPref < 0 || p.CharacterPref >= survivorIndices.Count) continue;

                int wanted = survivorIndices[p.CharacterPref];
                if (!free.Contains(wanted)) continue;

                result[p.ClientId] = wanted;
                free.Remove(wanted);
            }

            Shuffle(free, rng);
            foreach (LobbyPlayer p in survivors)
            {
                if (result.ContainsKey(p.ClientId) || free.Count == 0) continue;
                result[p.ClientId] = free[0];
                free.RemoveAt(0);
            }

            return result;
        }

        private static LobbyPlayer PickDemon(List<LobbyPlayer> players, Dictionary<ulong, int> demonTurns, System.Random rng)
        {
            List<LobbyPlayer> pool = Filter(players, RolePreference.Demon);
            if (pool.Count == 0) pool = Filter(players, RolePreference.Any);
            if (pool.Count == 0) pool = new List<LobbyPlayer>(players);

            // Equite : ceux qui ont ete le demon le moins souvent.
            int fewest = int.MaxValue;
            foreach (LobbyPlayer p in pool) fewest = System.Math.Min(fewest, Turns(demonTurns, p));

            List<LobbyPlayer> fair = new List<LobbyPlayer>();
            foreach (LobbyPlayer p in pool)
            {
                if (Turns(demonTurns, p) == fewest) fair.Add(p);
            }

            return fair[rng.Next(fair.Count)];
        }

        private static List<LobbyPlayer> Filter(List<LobbyPlayer> players, RolePreference pref)
        {
            List<LobbyPlayer> list = new List<LobbyPlayer>();
            foreach (LobbyPlayer p in players)
            {
                if (p.RolePref == pref) list.Add(p);
            }

            return list;
        }

        private static int Turns(Dictionary<ulong, int> demonTurns, LobbyPlayer p)
        {
            int turns;
            return demonTurns.TryGetValue(p.ClientId, out turns) ? turns : 0;
        }

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
