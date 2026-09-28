using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace HouseOfSilence.Network
{
    /// <summary>
    /// Lobby avant chaque partie : session Unity Relay (Multiplayer Services) creee par
    /// l'hote, rejointe par code ; liste des joueurs avec leurs preferences (personnage,
    /// role souhaite). Les clients envoient leurs preferences, l'hote tient la liste et la
    /// renvoie a tous ; il est seul a pouvoir lancer la partie (NetworkGameManager).
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyManager : MonoBehaviour
    {
        private const string PrefsMessage = "HOS_Prefs";
        private const string RosterMessage = "HOS_Roster";
        private const string RejectMessage = "HOS_Reject";
        private const string NameKey = "hos_player_name";

        private NetworkGameManager _game;
        private ISession _session;
        private float _nextRoster;

        /// <summary>Joueurs du lobby (tenus par l'hote, copies chez les clients).</summary>
        public readonly List<LobbyPlayer> Players = new List<LobbyPlayer>();

        /// <summary>Hote : nombre de fois ou chaque joueur a ete le demon (equite).</summary>
        public readonly Dictionary<ulong, int> DemonTurns = new Dictionary<ulong, int>();

        /// <summary>Code de la partie a donner aux autres joueurs.</summary>
        public string JoinCode { get; private set; } = "";

        public string LocalName { get; private set; } = "Joueur";
        public int LocalCharacterPref { get; private set; } = -1;
        public RolePreference LocalRolePref { get; private set; } = RolePreference.Any;
        public bool Busy { get; private set; }

        private void Awake()
        {
            _game = GetComponent<NetworkGameManager>();
            LocalName = PlayerPrefs.GetString(NameKey, "Joueur " + UnityEngine.Random.Range(100, 1000));

            _game.Register(PrefsMessage, OnPrefsMessage);
            _game.Register(RosterMessage, OnRosterMessage);
            _game.Register(RejectMessage, OnRejectMessage);
        }

        private void Update()
        {
            // L'hote renvoie regulierement la liste (securite si un message s'est perdu).
            if (_game.IsHost && _game.Phase == NetPhase.Lobby && Time.unscaledTime >= _nextRoster)
            {
                BroadcastRoster();
            }
        }

        // ------------------------------------------------------------------
        // Session
        // ------------------------------------------------------------------

        public async void CreateGame()
        {
            if (Busy) return;
            Busy = true;
            _game.SetPhase(NetPhase.Connecting);
            _game.Status = "Creation de la partie...";

            try
            {
                await PrepareServices();

                SessionOptions options = new SessionOptions
                {
                    Name = LocalName,
                    MaxPlayers = _game.MaxPlayers
                }.WithRelayNetwork();

                ISession session = await MultiplayerService.Instance.CreateSessionAsync(options);
                if (!await KeepSession(session)) return;

                JoinCode = session.Code;
                _game.Status = "";
                _game.SetPhase(NetPhase.Lobby);
            }
            catch (Exception e)
            {
                Fail(e);
            }
            finally
            {
                Busy = false;
            }
        }

        public async void JoinGame(string code)
        {
            if (Busy) return;

            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                _game.Status = "Entrez le code de la partie.";
                return;
            }

            Busy = true;
            _game.SetPhase(NetPhase.Connecting);
            _game.Status = "Connexion a la partie " + code + "...";

            try
            {
                await PrepareServices();

                ISession session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                if (!await KeepSession(session)) return;

                JoinCode = code;
                _game.Status = "";
                _game.SetPhase(NetPhase.Lobby);
            }
            catch (Exception e)
            {
                Fail(e);
            }
            finally
            {
                Busy = false;
            }
        }

        private async System.Threading.Tasks.Task PrepareServices()
        {
            _game.PrepareNetwork();

            if (UnityServices.Instance == null || UnityServices.Instance.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                // Profil propre a cette fenetre : deux instances sur le meme PC = deux joueurs.
                AuthenticationService.Instance.SwitchProfile("hos" + UnityEngine.Random.Range(0, 1000000));
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        /// <summary>Annule pendant la connexion : on ressort aussitot de la session.</summary>
        private async System.Threading.Tasks.Task<bool> KeepSession(ISession session)
        {
            if (_game.Phase == NetPhase.Connecting)
            {
                _session = session;
                return true;
            }

            await session.LeaveAsync();
            _game.ShutdownNetwork();
            return false;
        }

        private void Fail(Exception e)
        {
            Debug.LogException(e);
            _session = null;
            _game.ShutdownNetwork();
            Players.Clear();
            JoinCode = "";
            _game.OnLeftOnline("Echec : " + e.Message);
        }

        /// <summary>Quitte la partie en ligne (menu, deconnexion, refus de l'hote).</summary>
        public async void Leave(string reason)
        {
            ISession session = _session;
            _session = null;
            Players.Clear();
            JoinCode = "";
            _game.OnLeftOnline(reason);

            try
            {
                if (session != null) await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LobbyManager] " + e.Message);
            }

            _game.ShutdownNetwork();
        }

        // ------------------------------------------------------------------
        // Joueurs et preferences
        // ------------------------------------------------------------------

        /// <summary>Change nom / personnage / role souhaites (envoyes a l'hote si en ligne).</summary>
        public void SetPreferences(string playerName, int characterPref, RolePreference rolePref)
        {
            playerName = string.IsNullOrWhiteSpace(playerName) ? "Joueur" : playerName.Trim();
            if (playerName.Length > 20) playerName = playerName.Substring(0, 20);

            bool changed = playerName != LocalName || characterPref != LocalCharacterPref || rolePref != LocalRolePref;
            LocalName = playerName;
            LocalCharacterPref = characterPref;
            LocalRolePref = rolePref;
            PlayerPrefs.SetString(NameKey, LocalName);

            if (changed && _game.Online) SendPrefs();
        }

        /// <summary>Connecte (hote ou client) : on s'annonce a l'hote.</summary>
        public void OnLocalConnected()
        {
            SendPrefs();
        }

        private void SendPrefs()
        {
            using (FastBufferWriter w = new FastBufferWriter(128, Allocator.Temp))
            {
                w.WriteValueSafe(LocalName);
                w.WriteValueSafe((short)LocalCharacterPref);
                w.WriteValueSafe((byte)LocalRolePref);
                _game.SendToServer(PrefsMessage, w);
            }
        }

        private void OnPrefsMessage(ulong sender, FastBufferReader r)
        {
            if (!_game.IsHost) return;

            string playerName;
            short character;
            byte role;
            r.ReadValueSafe(out playerName);
            r.ReadValueSafe(out character);
            r.ReadValueSafe(out role);

            LobbyPlayer p = Find(sender);
            if (p == null)
            {
                p = new LobbyPlayer { ClientId = sender };
                Players.Add(p);
            }

            p.Name = string.IsNullOrWhiteSpace(playerName) ? "Joueur" : playerName;
            p.CharacterPref = character;
            p.RolePref = role <= (byte)RolePreference.Survivor ? (RolePreference)role : RolePreference.Any;
            BroadcastRoster();
        }

        /// <summary>Hote : un joueur arrive. Refuse si la partie est deja lancee.</summary>
        public void OnClientJoined(ulong clientId)
        {
            if (!_game.IsHost || clientId == _game.LocalId) return;
            if (_game.Phase == NetPhase.Lobby) return; // il va envoyer ses preferences

            using (FastBufferWriter w = new FastBufferWriter(64, Allocator.Temp))
            {
                w.WriteValueSafe("Partie deja en cours : attendez la fin et rejoignez le lobby.");
                _game.SendTo(clientId, RejectMessage, w);
            }
        }

        public void OnClientLeft(ulong clientId)
        {
            LobbyPlayer p = Find(clientId);
            if (p == null) return;

            Players.Remove(p);
            if (_game.IsHost) BroadcastRoster();
        }

        private void OnRejectMessage(ulong sender, FastBufferReader r)
        {
            string reason;
            r.ReadValueSafe(out reason);
            Leave(reason);
        }

        private void BroadcastRoster()
        {
            _nextRoster = Time.unscaledTime + 2f;

            using (FastBufferWriter w = new FastBufferWriter(1024, Allocator.Temp))
            {
                w.WriteValueSafe(JoinCode ?? "");
                w.WriteValueSafe((byte)Players.Count);

                foreach (LobbyPlayer p in Players)
                {
                    w.WriteValueSafe(p.ClientId);
                    w.WriteValueSafe(p.Name);
                    w.WriteValueSafe((short)p.CharacterPref);
                    w.WriteValueSafe((byte)p.RolePref);
                }

                _game.SendToAll(RosterMessage, w);
            }
        }

        private void OnRosterMessage(ulong sender, FastBufferReader r)
        {
            if (_game.IsHost) return; // l'hote a deja la liste de reference

            string code;
            byte count;
            r.ReadValueSafe(out code);
            r.ReadValueSafe(out count);

            Players.Clear();
            for (int i = 0; i < count; i++)
            {
                ulong id;
                string playerName;
                short character;
                byte role;
                r.ReadValueSafe(out id);
                r.ReadValueSafe(out playerName);
                r.ReadValueSafe(out character);
                r.ReadValueSafe(out role);
                Players.Add(new LobbyPlayer { ClientId = id, Name = playerName, CharacterPref = character, RolePref = (RolePreference)role });
            }

            if (!string.IsNullOrEmpty(code)) JoinCode = code;
        }

        public LobbyPlayer Find(ulong clientId)
        {
            foreach (LobbyPlayer p in Players)
            {
                if (p.ClientId == clientId) return p;
            }

            return null;
        }

        /// <summary>Hote : les joueurs du lobby presents dans 'ids' (ceux qui ont charge la partie).</summary>
        public List<LobbyPlayer> PlayersIn(HashSet<ulong> ids)
        {
            List<LobbyPlayer> list = new List<LobbyPlayer>();
            foreach (LobbyPlayer p in Players)
            {
                if (ids.Contains(p.ClientId)) list.Add(p);
            }

            return list;
        }
    }
}
