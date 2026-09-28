using System.Collections;
using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Demon;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HouseOfSilence.Network
{
    /// <summary>Ligne de la liste des joueurs, telle que ce joueur a le droit de la voir.</summary>
    public class PlayerView
    {
        public string Name = "";
        /// <summary>Personnage joue, -1 si cache (demon inconnu de ce joueur).</summary>
        public int Character = -1;
        public bool Alive = true;
        public bool Connected = true;
        /// <summary>Demon, uniquement si ce joueur a le droit de le savoir.</summary>
        public bool IsDemon;
        public bool IsMe;
    }

    /// <summary>
    /// Etat de la partie, avec l'hote comme seule autorite : qui joue quoi, qui est mort,
    /// fin de partie. Les clients ne font que demander (le demon demande une mise a mort)
    /// et recoivent le resultat.
    ///
    /// - Mort : l'hote verifie que le demandeur joue bien le demon, que la victime est en
    ///   vie et a portee, puis annonce la mort a tous (sinon il la annule chez le demandeur).
    /// - Victoire : le demon gagne quand tous les survivants joues sont morts ; les
    ///   survivants gagnent a l'aube (NetworkTimeOfDay) ou si le demon quitte la partie.
    /// - Liste des joueurs : chacun recoit sa propre version ; l'identite du demon n'est
    ///   envoyee qu'au demon lui-meme (et aux morts si revealDemonToDead), puis a tous en
    ///   fin de partie.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameStateManager : MonoBehaviour
    {
        private const string KillRequestMessage = "HOS_KillReq";
        private const string ViewMessage = "HOS_View";
        private const string GameOverMessage = "HOS_GameOver";

        [Tooltip("Les survivants morts (spectateurs) voient qui est le demon.")]
        [SerializeField] private bool revealDemonToDead = false;
        [Tooltip("Distance max. acceptee par l'hote entre le demon et sa victime (m).")]
        [SerializeField, Min(1f)] private float killTolerance = 4f;
        [SerializeField, Min(3f)] private float resultSeconds = 10f;

        private class RoundPlayer
        {
            public ulong ClientId;
            public string Name;
            public int Character;
            public PlayerRole Role;
            public bool Alive = true;
            public bool Connected = true;
            public bool IsBot;
        }

        private NetworkGameManager _game;
        private readonly List<RoundPlayer> _round = new List<RoundPlayer>();
        private bool _roundActive;
        private bool _hadSurvivors;
        private bool _hadDemon;
        private float _nextViews;
        private float _nextCheck;
        private GUIStyle _bigStyle;
        private GUIStyle _textStyle;

        /// <summary>Liste des joueurs vue par ce joueur (TAB, spectateur).</summary>
        public List<PlayerView> View { get; private set; } = new List<PlayerView>();

        public PlayerRole LocalRole { get; private set; } = PlayerRole.None;

        /// <summary>Resultat affiche en fin de partie (vide pendant la partie).</summary>
        public string ResultTitle { get; private set; } = "";
        public string ResultReason { get; private set; } = "";

        private void Awake()
        {
            _game = GetComponent<NetworkGameManager>();
            _game.Register(KillRequestMessage, OnKillRequest);
            _game.Register(ViewMessage, OnViewMessage);
            _game.Register(GameOverMessage, OnGameOverMessage);
        }

        public void ResetLocal()
        {
            View = new List<PlayerView>();
            LocalRole = PlayerRole.None;
            ResultTitle = "";
            ResultReason = "";
        }

        public void SetLocalRole(PlayerRole role)
        {
            LocalRole = role;
        }

        // ------------------------------------------------------------------
        // Hote : debut de partie
        // ------------------------------------------------------------------

        public void ServerStartRound(Dictionary<ulong, int> assignments, List<LobbyPlayer> players, List<Component> characters)
        {
            _round.Clear();

            foreach (KeyValuePair<ulong, int> a in assignments)
            {
                string playerName = "Joueur";
                bool bot = false;
                foreach (LobbyPlayer p in players)
                {
                    if (p.ClientId != a.Key) continue;
                    playerName = p.IsBot ? p.Name + " (IA)" : p.Name;
                    bot = p.IsBot;
                }

                bool demon = a.Value >= 0 && a.Value < characters.Count && characters[a.Value] is DemonController;
                _round.Add(new RoundPlayer
                {
                    ClientId = a.Key,
                    Name = playerName,
                    Character = a.Value,
                    Role = demon ? PlayerRole.Demon : PlayerRole.Survivor,
                    IsBot = bot
                });
            }

            _hadSurvivors = _round.Exists(p => p.Role == PlayerRole.Survivor);
            _hadDemon = _round.Exists(p => p.Role == PlayerRole.Demon);
            _roundActive = true;
            _nextViews = 0f;
            _nextCheck = Time.unscaledTime + 2f;
        }

        /// <summary>Client qui joue ce personnage (ulong.MaxValue si personne).</summary>
        public ulong OwnerOf(int character)
        {
            foreach (RoundPlayer p in _round)
            {
                if (p.Character == character && p.Connected) return p.ClientId;
            }

            return ulong.MaxValue;
        }

        /// <summary>Nom du joueur qui joue ce personnage, s'il est connu ici.</summary>
        public string PlayerNameOf(int character)
        {
            foreach (PlayerView v in View)
            {
                if (v.Character == character && character >= 0) return v.Name;
            }

            return null;
        }

        /// <summary>Hote : personnages joues par des bots, et le role de chacun.</summary>
        public List<KeyValuePair<int, PlayerRole>> BotCharacters()
        {
            List<KeyValuePair<int, PlayerRole>> list = new List<KeyValuePair<int, PlayerRole>>();
            foreach (RoundPlayer p in _round)
            {
                if (p.IsBot) list.Add(new KeyValuePair<int, PlayerRole>(p.Character, p.Role));
            }

            return list;
        }

        /// <summary>Hote : survivants de la partie encore en vie (cibles du demon IA).</summary>
        public List<int> AliveSurvivorCharacters()
        {
            List<int> list = new List<int>();
            foreach (RoundPlayer p in _round)
            {
                if (p.Role == PlayerRole.Survivor && p.Alive && p.Connected) list.Add(p.Character);
            }

            return list;
        }

        /// <summary>Hote : le demon IA tue (verifie comme pour un joueur, puis annonce a tous).</summary>
        public void ServerBotKill(int demon, int victim, bool jumpscare)
        {
            if (!_game.IsHost || !_roundActive) return;

            RoundPlayer owner = null;
            foreach (RoundPlayer p in _round)
            {
                if (p.Character == demon) owner = p;
            }

            if (owner == null || !owner.IsBot || !IsValidKill(owner.ClientId, demon, victim)) return;

            foreach (RoundPlayer p in _round)
            {
                if (p.Character == victim) p.Alive = false;
            }

            byte d = (byte)demon;
            byte v = (byte)victim;
            _game.ApplyKill(demon, victim, jumpscare);
            _game.BroadcastEvent(_game.LocalId, NetworkGameManager.EventKill, w =>
            {
                w.WriteValueSafe(d);
                w.WriteValueSafe(v);
                w.WriteValueSafe(jumpscare);
            });

            _nextViews = 0f;
            _nextCheck = 0f;
        }

        public void OnClientLeft(ulong clientId)
        {
            foreach (RoundPlayer p in _round)
            {
                if (p.ClientId == clientId) p.Connected = false;
            }

            _nextViews = 0f;
        }

        private void Update()
        {
            if (!_game.IsHost || !_roundActive) return;

            if (Time.unscaledTime >= _nextViews)
            {
                _nextViews = Time.unscaledTime + 2f;
                SendViews();
            }

            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.5f;
                CheckVictory();
            }
        }

        // ------------------------------------------------------------------
        // Morts (autorite de l'hote)
        // ------------------------------------------------------------------

        /// <summary>Le demon joue ici vient de tuer : demande de validation a l'hote.</summary>
        public void RequestKill(int demon, int victim, bool jumpscare)
        {
            using (FastBufferWriter w = new FastBufferWriter(16, Allocator.Temp))
            {
                w.WriteValueSafe((byte)demon);
                w.WriteValueSafe((byte)victim);
                w.WriteValueSafe(jumpscare);
                _game.SendToServer(KillRequestMessage, w);
            }
        }

        private void OnKillRequest(ulong sender, FastBufferReader r)
        {
            byte demon, victim;
            bool jumpscare;
            r.ReadValueSafe(out demon);
            r.ReadValueSafe(out victim);
            r.ReadValueSafe(out jumpscare);

            if (!_game.IsHost) return;

            if (!IsValidKill(sender, demon, victim))
            {
                // Refusee : le demandeur a deja joue la mort chez lui, on la defait.
                _game.SendEventTo(sender, NetworkGameManager.EventRevive, w => w.WriteValueSafe(victim));
                return;
            }

            foreach (RoundPlayer p in _round)
            {
                if (p.Character == victim) p.Alive = false;
            }

            // Tout le monde joue la mort (le demandeur l'a deja jouee chez lui).
            if (sender != _game.LocalId) _game.ApplyKill(demon, victim, jumpscare);
            _game.BroadcastEvent(sender, NetworkGameManager.EventKill, w =>
            {
                w.WriteValueSafe(demon);
                w.WriteValueSafe(victim);
                w.WriteValueSafe(jumpscare);
            });

            _nextViews = 0f;
            _nextCheck = 0f;
        }

        private bool IsValidKill(ulong sender, int demon, int victim)
        {
            if (!_roundActive || OwnerOf(demon) != sender) return false;

            DemonController demonCharacter = _game.Get<DemonController>(demon);
            SurvivorDeath death = _game.Get<SurvivorDeath>(victim);
            if (demonCharacter == null || death == null) return false;

            foreach (RoundPlayer p in _round)
            {
                if (p.Character == victim && !p.Alive) return false;
            }

            Vector3 d = demonCharacter.transform.position - death.transform.position;
            d.y = 0f;
            return d.magnitude <= killTolerance;
        }

        // ------------------------------------------------------------------
        // Victoire
        // ------------------------------------------------------------------

        private void CheckVictory()
        {
            int survivorsAlive = 0;
            bool demonHere = false;

            foreach (RoundPlayer p in _round)
            {
                if (p.Role == PlayerRole.Survivor && p.Alive && p.Connected) survivorsAlive++;
                if (p.Role == PlayerRole.Demon && p.Connected) demonHere = true;
            }

            if (_hadSurvivors && survivorsAlive == 0)
            {
                EndRound(PlayerRole.Demon, "Tous les survivants sont morts.");
            }
            else if (_hadDemon && !demonHere)
            {
                EndRound(PlayerRole.Survivor, "Le demon a quitte la partie.");
            }
            else if (_game.TimeOfDay.DawnReached)
            {
                EndRound(PlayerRole.Survivor, "L'aube se leve : les survivants ont tenu toute la nuit.");
            }
        }

        private void EndRound(PlayerRole winner, string reason)
        {
            _roundActive = false;

            using (FastBufferWriter w = new FastBufferWriter(1024, Allocator.Temp))
            {
                w.WriteValueSafe((byte)winner);
                w.WriteValueSafe(reason);
                w.WriteValueSafe((byte)_round.Count);

                // Fin de partie : tout est revele.
                foreach (RoundPlayer p in _round)
                {
                    w.WriteValueSafe(p.Name);
                    w.WriteValueSafe((short)p.Character);
                    w.WriteValueSafe(p.Alive);
                    w.WriteValueSafe(p.Connected);
                    w.WriteValueSafe(p.Role == PlayerRole.Demon);
                    w.WriteValueSafe(p.ClientId);
                }

                _game.SendToAll(GameOverMessage, w);
            }
        }

        private void OnGameOverMessage(ulong sender, FastBufferReader r)
        {
            byte winner;
            string reason;
            byte count;
            r.ReadValueSafe(out winner);
            r.ReadValueSafe(out reason);
            r.ReadValueSafe(out count);

            List<PlayerView> view = new List<PlayerView>();
            for (int i = 0; i < count; i++)
            {
                PlayerView v = new PlayerView();
                short character;
                ulong id;
                r.ReadValueSafe(out v.Name);
                r.ReadValueSafe(out character);
                r.ReadValueSafe(out v.Alive);
                r.ReadValueSafe(out v.Connected);
                r.ReadValueSafe(out v.IsDemon);
                r.ReadValueSafe(out id);
                v.Character = character;
                v.IsMe = id == _game.LocalId;
                view.Add(v);
            }

            View = view;
            bool demonWon = winner == (byte)PlayerRole.Demon;
            bool iWon = LocalRole != PlayerRole.None && (LocalRole == PlayerRole.Demon) == demonWon;
            ResultTitle = demonWon ? "LE DEMON L'EMPORTE" : "LES SURVIVANTS L'EMPORTENT";
            ResultReason = reason;

            GameManager game = GameManager.HasInstance ? GameManager.Instance : null;
            if (game != null) game.EndGame(iWon ? GameResult.Victory : GameResult.Defeat);

            _game.EnterGameOver();
            StartCoroutine(BackToLobby());
        }

        private IEnumerator BackToLobby()
        {
            yield return new WaitForSecondsRealtime(resultSeconds);
            if (_game.Phase == NetPhase.GameOver) _game.ReturnToLobby();
        }

        // ------------------------------------------------------------------
        // Liste des joueurs, par destinataire
        // ------------------------------------------------------------------

        private void SendViews()
        {
            foreach (RoundPlayer recipient in _round)
            {
                if (!recipient.Connected || recipient.IsBot) continue;

                bool knowsDemon = recipient.Role == PlayerRole.Demon || (revealDemonToDead && !recipient.Alive);

                using (FastBufferWriter w = new FastBufferWriter(1024, Allocator.Temp))
                {
                    w.WriteValueSafe((byte)_round.Count);

                    foreach (RoundPlayer p in _round)
                    {
                        bool hidden = p.Role == PlayerRole.Demon && !knowsDemon;
                        w.WriteValueSafe(p.Name);
                        w.WriteValueSafe((short)(hidden ? -1 : p.Character));
                        w.WriteValueSafe(p.Alive);
                        w.WriteValueSafe(p.Connected);
                        w.WriteValueSafe(p.Role == PlayerRole.Demon && knowsDemon);
                        w.WriteValueSafe(p.ClientId == recipient.ClientId);
                    }

                    _game.SendTo(recipient.ClientId, ViewMessage, w);
                }
            }
        }

        private void OnViewMessage(ulong sender, FastBufferReader r)
        {
            if (_game.Phase == NetPhase.GameOver) return; // la liste de fin de partie reste affichee

            byte count;
            r.ReadValueSafe(out count);

            List<PlayerView> view = new List<PlayerView>();
            for (int i = 0; i < count; i++)
            {
                PlayerView v = new PlayerView();
                short character;
                r.ReadValueSafe(out v.Name);
                r.ReadValueSafe(out character);
                r.ReadValueSafe(out v.Alive);
                r.ReadValueSafe(out v.Connected);
                r.ReadValueSafe(out v.IsDemon);
                r.ReadValueSafe(out v.IsMe);
                v.Character = character;
                view.Add(v);
            }

            View = view;
        }

        // ------------------------------------------------------------------
        // Ecran de fin
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (_game.Phase != NetPhase.GameOver || string.IsNullOrEmpty(ResultTitle)) return;

            if (_bigStyle == null)
            {
                _bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _textStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, richText = true };
                _textStyle.normal.textColor = new Color(0.9f, 0.88f, 0.84f);
            }

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;

            bool demonWon = ResultTitle.Contains("DEMON");
            _bigStyle.normal.textColor = demonWon ? new Color(0.9f, 0.15f, 0.1f) : new Color(0.85f, 0.85f, 0.6f);

            float y = Screen.height * 0.28f;
            GUI.Label(new Rect(0f, y, Screen.width, 50f), ResultTitle, _bigStyle);
            GUI.Label(new Rect(0f, y + 50f, Screen.width, 30f), ResultReason, _textStyle);

            y += 100f;
            foreach (PlayerView v in View)
            {
                GUI.Label(new Rect(0f, y, Screen.width, 26f), PlayerListUI.Line(v, true), _textStyle);
                y += 26f;
            }

            GUI.Label(new Rect(0f, y + 20f, Screen.width, 26f), "Retour au lobby dans quelques secondes...", _textStyle);
        }
    }
}
