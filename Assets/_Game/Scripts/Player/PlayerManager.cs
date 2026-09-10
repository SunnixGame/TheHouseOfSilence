using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Utilities;
using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Registre des joueurs presents dans la partie (1 a 4).
    ///
    /// Sert de point d'entree unique a tous les systemes qui ont besoin de
    /// "trouver un joueur" : IA (cible la plus proche), peur (isolement),
    /// objectifs, evenements horrifiques, UI.
    ///
    /// Aucun systeme ne doit faire de FindObjectOfType pour chercher un joueur.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerManager : MonoSingleton<PlayerManager>
    {
        [Header("Fin de partie")]
        [Tooltip("Declenche la defaite quand tous les joueurs sont morts.")]
        [SerializeField] private bool endGameWhenAllPlayersDead = true;

        [Tooltip("Delai avant l'ecran de defaite, en secondes.")]
        [SerializeField, Min(0f)] private float defeatDelay = 2.5f;

        private readonly List<PlayerCharacter> _players = new List<PlayerCharacter>(4);
        private float _defeatTimer = -1f;

        /// <summary>Le PlayerManager vit dans la scene de jeu, pas entre les scenes.</summary>
        protected override bool IsPersistent { get { return false; } }

        /// <summary>Tous les joueurs enregistres.</summary>
        public IReadOnlyList<PlayerCharacter> Players { get { return _players; } }

        /// <summary>Nombre de joueurs dans la partie.</summary>
        public int PlayerCount { get { return _players.Count; } }

        /// <summary>Le joueur controle sur cette machine (null si aucun).</summary>
        public PlayerCharacter LocalPlayer { get; private set; }

        /// <summary>Nombre de joueurs encore en vie.</summary>
        public int AlivePlayerCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < _players.Count; i++)
                {
                    if (_players[i] != null && _players[i].IsAlive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // ------------------------------------------------------------------
        // Enregistrement
        // ------------------------------------------------------------------

        public void Register(PlayerCharacter player)
        {
            if (player == null || _players.Contains(player))
            {
                return;
            }

            _players.Add(player);

            if (player.IsLocalPlayer)
            {
                LocalPlayer = player;
            }
        }

        public void Unregister(PlayerCharacter player)
        {
            if (player == null)
            {
                return;
            }

            _players.Remove(player);

            if (LocalPlayer == player)
            {
                LocalPlayer = null;

                for (int i = 0; i < _players.Count; i++)
                {
                    if (_players[i] != null && _players[i].IsLocalPlayer)
                    {
                        LocalPlayer = _players[i];
                        break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Requetes
        // ------------------------------------------------------------------

        /// <summary>Renvoie le joueur portant cet identifiant, ou null.</summary>
        public PlayerCharacter GetPlayerById(int id)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] != null && _players[i].PlayerId == id)
                {
                    return _players[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Joueur vivant le plus proche d'une position. Utilise par l'IA et les evenements.
        /// distance vaut float.MaxValue si aucun joueur n'est trouve.
        /// </summary>
        public PlayerCharacter GetNearestPlayer(Vector3 position, out float distance, bool aliveOnly = true)
        {
            PlayerCharacter nearest = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < _players.Count; i++)
            {
                PlayerCharacter player = _players[i];

                if (player == null)
                {
                    continue;
                }

                if (aliveOnly && !player.IsAlive)
                {
                    continue;
                }

                float sqr = (player.FeetPosition - position).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = player;
                }
            }

            distance = nearest != null ? Mathf.Sqrt(bestSqr) : float.MaxValue;
            return nearest;
        }

        /// <summary>
        /// Nombre de coequipiers vivants a portee. Servira au systeme de peur
        /// (l'isolement fait monter la peur, la proximite la fait baisser).
        /// </summary>
        public int CountNearbyPlayers(PlayerCharacter reference, float radius)
        {
            if (reference == null || radius <= 0f)
            {
                return 0;
            }

            float radiusSqr = radius * radius;
            int count = 0;

            for (int i = 0; i < _players.Count; i++)
            {
                PlayerCharacter player = _players[i];

                if (player == null || player == reference || !player.IsAlive)
                {
                    continue;
                }

                if ((player.FeetPosition - reference.FeetPosition).sqrMagnitude <= radiusSqr)
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------
        // Defaite collective
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
        }

        private void OnPlayerDied(PlayerDiedEvent evt)
        {
            if (!endGameWhenAllPlayersDead)
            {
                return;
            }

            if (_players.Count == 0 || AlivePlayerCount > 0)
            {
                return;
            }

            _defeatTimer = defeatDelay;
        }

        private void Update()
        {
            if (_defeatTimer < 0f)
            {
                return;
            }

            _defeatTimer -= Time.deltaTime;

            if (_defeatTimer > 0f)
            {
                return;
            }

            _defeatTimer = -1f;

            GameManager game = GameManager.Instance;

            if (game != null)
            {
                game.EndGame(GameResult.Defeat);
            }
        }
    }
}
