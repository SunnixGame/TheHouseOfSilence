using System.Collections.Generic;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Network
{
    /// <summary>
    /// Menus du multijoueur (HUD local) :
    ///  - accueil : nom du joueur, creer une partie, rejoindre avec un code, jouer en solo ;
    ///  - lobby : code de la partie (a copier), joueurs connectes, choix du personnage et
    ///    du role souhaite (Demon / Survivant / Peu importe), lancement par l'hote seul ;
    ///  - chargement.
    /// Les choix sont des preferences : c'est l'hote qui attribue les roles (RoleManager).
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterSelectionUI : MonoBehaviour
    {
        private NetworkGameManager _game;
        private LobbyManager _lobby;
        private string _nameField;
        private string _codeField = "";
        private readonly List<string> _survivorNames = new List<string>();

        private GUIStyle _titleStyle;
        private GUIStyle _textStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _codeStyle;

        private void Awake()
        {
            _game = GetComponent<NetworkGameManager>();
            _lobby = GetComponent<LobbyManager>();
        }

        private void RefreshSurvivorNames()
        {
            _survivorNames.Clear();
            if (_game.Switcher == null) return;

            foreach (Component c in _game.Switcher.Characters())
            {
                PlayerCharacter survivor = c as PlayerCharacter;
                if (survivor != null) _survivorNames.Add(survivor.DisplayName);
            }
        }

        private void OnGUI()
        {
            NetPhase phase = _game.Phase;
            if (phase != NetPhase.Menu && phase != NetPhase.Connecting && phase != NetPhase.Lobby && phase != NetPhase.Loading) return;

            EnsureStyles();
            if (_nameField == null) _nameField = _lobby.LocalName;

            float w = phase == NetPhase.Lobby ? 620f : 460f;
            float h = phase == NetPhase.Lobby ? 520f : 330f;
            Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.88f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUILayout.BeginArea(new Rect(box.x + 20f, box.y + 16f, w - 40f, h - 32f));
            GUILayout.Label("THE HOUSE OF SILENCE", _titleStyle);
            GUILayout.Space(8f);

            switch (phase)
            {
                case NetPhase.Menu: DrawMenu(); break;
                case NetPhase.Connecting: DrawConnecting(); break;
                case NetPhase.Lobby: DrawLobby(); break;
                case NetPhase.Loading: GUILayout.Label("Chargement de la partie pour tous les joueurs...", _textStyle); break;
            }

            if (!string.IsNullOrEmpty(_game.Status))
            {
                GUILayout.Space(6f);
                GUILayout.Label(_game.Status, _smallStyle);
            }

            GUILayout.EndArea();
        }

        private void DrawMenu()
        {
            GUILayout.Label("Votre nom :", _textStyle);
            _nameField = GUILayout.TextField(_nameField ?? "", 20);
            GUILayout.Space(10f);

            if (GUILayout.Button("Creer une partie (vous etes l'hote)", GUILayout.Height(34f)))
            {
                ApplyName();
                _lobby.CreateGame();
            }

            GUILayout.Space(8f);
            GUILayout.Label("Rejoindre avec le code de la partie :", _textStyle);
            GUILayout.BeginHorizontal();
            _codeField = GUILayout.TextField(_codeField ?? "", 12, GUILayout.Width(200f));
            if (GUILayout.Button("Rejoindre", GUILayout.Height(24f)))
            {
                ApplyName();
                _lobby.JoinGame(_codeField);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            if (GUILayout.Button("Jouer en solo", GUILayout.Height(28f))) _game.PlaySolo();
        }

        private void ApplyName()
        {
            _lobby.SetPreferences(_nameField, _lobby.LocalCharacterPref, _lobby.LocalRolePref);
            _nameField = _lobby.LocalName;
        }

        private void DrawConnecting()
        {
            GUILayout.Label("Connexion...", _textStyle);
            if (GUILayout.Button("Annuler", GUILayout.Height(28f))) _lobby.Leave("");
        }

        private void DrawLobby()
        {
            if (_survivorNames.Count == 0) RefreshSurvivorNames();

            // Code a partager.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Code de la partie :", _textStyle, GUILayout.Width(170f));
            GUILayout.Label(string.IsNullOrEmpty(_lobby.JoinCode) ? "..." : _lobby.JoinCode, _codeStyle);
            if (!string.IsNullOrEmpty(_lobby.JoinCode) && GUILayout.Button("Copier", GUILayout.Width(80f), GUILayout.Height(26f)))
            {
                GUIUtility.systemCopyBuffer = _lobby.JoinCode;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            // Joueurs connectes.
            GUILayout.Label("Joueurs (" + _lobby.Players.Count + " / " + _game.MaxPlayers + ")", _textStyle);
            foreach (LobbyPlayer p in _lobby.Players)
            {
                string host = p.ClientId == 0 ? "  [hote]" : "";
                string me = p.ClientId == _game.LocalId ? "  (vous)" : "";
                GUILayout.Label("•  " + p.Name + host + me + "   —   " + CharacterLabel(p.CharacterPref) + "  ·  " + RoleLabel(p.RolePref), _smallStyle);
            }

            GUILayout.Space(10f);

            // Mes choix.
            GUILayout.Label("Personnage souhaite (si vous etes survivant) :", _textStyle);
            GUILayout.BeginHorizontal();
            int character = _lobby.LocalCharacterPref;
            if (Toggle(character == -1, "Peu importe")) character = -1;
            for (int i = 0; i < _survivorNames.Count; i++)
            {
                if (Toggle(character == i, _survivorNames[i])) character = i;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Role souhaite :", _textStyle);
            GUILayout.BeginHorizontal();
            RolePreference role = _lobby.LocalRolePref;
            if (Toggle(role == RolePreference.Any, "Peu importe")) role = RolePreference.Any;
            if (Toggle(role == RolePreference.Demon, "Demon")) role = RolePreference.Demon;
            if (Toggle(role == RolePreference.Survivor, "Survivant")) role = RolePreference.Survivor;
            GUILayout.EndHorizontal();

            if (character != _lobby.LocalCharacterPref || role != _lobby.LocalRolePref)
            {
                _lobby.SetPreferences(_lobby.LocalName, character, role);
            }

            GUILayout.Label("Un seul demon ; l'hote l'attribue en respectant au mieux les souhaits.", _smallStyle);
            GUILayout.Space(10f);

            if (_game.IsHost)
            {
                if (GUILayout.Button("Lancer la partie", GUILayout.Height(36f))) _game.LaunchRound();
            }
            else
            {
                GUILayout.Label("En attente du lancement par l'hote...", _textStyle);
            }

            if (GUILayout.Button("Quitter", GUILayout.Height(26f))) _lobby.Leave("");
        }

        private static bool Toggle(bool on, string label)
        {
            return GUILayout.Toggle(on, label, "Button", GUILayout.Height(26f)) && !on;
        }

        private string CharacterLabel(int index)
        {
            if (index < 0 || index >= _survivorNames.Count) return "perso : peu importe";
            return "perso : " + _survivorNames[index];
        }

        private static string RoleLabel(RolePreference pref)
        {
            return pref == RolePreference.Demon ? "veut etre Demon" : pref == RolePreference.Survivor ? "veut etre Survivant" : "role : peu importe";
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;

            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _titleStyle.normal.textColor = new Color(0.85f, 0.2f, 0.15f);
            _textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _textStyle.normal.textColor = new Color(0.9f, 0.88f, 0.84f);
            _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _smallStyle.normal.textColor = new Color(0.75f, 0.72f, 0.68f);
            _codeStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _codeStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        }
    }
}
