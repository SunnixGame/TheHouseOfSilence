using System.Collections.Generic;
using HouseOfSilence.Demon;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Network
{
    /// <summary>
    /// Menus du multijoueur (HUD local) :
    ///  - accueil : nom du joueur, creer une partie, rejoindre avec un code, jouer en solo ;
    ///  - lobby : code de la partie (a copier), joueurs et bots IA, selection du personnage
    ///    par portrait (rendu du modele, CharacterPortraits), role souhaite (Demon /
    ///    Survivant / Peu importe) ; l'hote ajoute ou retire des bots et lance la partie ;
    ///  - chargement.
    /// Les choix sont des preferences : c'est l'hote qui attribue les roles (RoleManager).
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterSelectionUI : MonoBehaviour
    {
        private class Choice
        {
            public string Name;
            public Texture Portrait;
        }

        private const float CardWidth = 118f;
        private const float PortraitHeight = 148f;

        private NetworkGameManager _game;
        private LobbyManager _lobby;
        private CharacterPortraits _portraits;
        private string _nameField;
        private string _codeField = "";

        private readonly List<Choice> _survivors = new List<Choice>();
        private Texture _demonPortrait;
        private PlayableCharacterSwitcher _preparedFor;

        // Clics appliques apres le dessin (changer la liste pendant OnGUI casse la mise en page).
        private readonly List<System.Action> _actions = new List<System.Action>();

        private GUIStyle _titleStyle;
        private GUIStyle _textStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _codeStyle;
        private GUIStyle _cardNameStyle;
        private GUIStyle _cardWantStyle;

        private void Awake()
        {
            _game = GetComponent<NetworkGameManager>();
            _lobby = GetComponent<LobbyManager>();
            _portraits = GetComponent<CharacterPortraits>();
        }

        private void Update()
        {
            if (_actions.Count > 0)
            {
                List<System.Action> actions = new List<System.Action>(_actions);
                _actions.Clear();
                foreach (System.Action a in actions) a();
            }

            // Portraits prepares hors OnGUI (rendu 3D), une fois par scene.
            if (_game.Phase == NetPhase.Lobby && _game.Switcher != null && _preparedFor != _game.Switcher)
            {
                PrepareChoices();
            }
        }

        private void PrepareChoices()
        {
            _preparedFor = _game.Switcher;
            _survivors.Clear();
            _demonPortrait = null;

            foreach (Component c in _game.Switcher.Characters())
            {
                Texture portrait = _portraits != null ? _portraits.Get(c) : null;
                PlayerCharacter survivor = c as PlayerCharacter;

                if (survivor != null) _survivors.Add(new Choice { Name = survivor.DisplayName, Portrait = portrait });
                else _demonPortrait = portrait;
            }
        }

        private void OnGUI()
        {
            NetPhase phase = _game.Phase;
            if (phase != NetPhase.Menu && phase != NetPhase.Connecting && phase != NetPhase.Lobby && phase != NetPhase.Loading) return;

            EnsureStyles();
            if (_nameField == null) _nameField = _lobby.LocalName;

            bool lobby = phase == NetPhase.Lobby;
            float w = lobby ? Mathf.Min(Screen.width - 40f, 780f) : 460f;
            float h = lobby ? Mathf.Min(Screen.height - 40f, 700f) : 330f;
            Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUILayout.BeginArea(new Rect(box.x + 20f, box.y + 14f, w - 40f, h - 28f));
            GUILayout.Label("THE HOUSE OF SILENCE", _titleStyle);
            GUILayout.Space(6f);

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

        // ------------------------------------------------------------------
        // Accueil
        // ------------------------------------------------------------------

        private void DrawMenu()
        {
            GUILayout.Label("Votre nom :", _textStyle);
            _nameField = GUILayout.TextField(_nameField ?? "", 20);
            GUILayout.Space(10f);

            if (GUILayout.Button("Creer une partie (vous etes l'hote)", GUILayout.Height(34f)))
            {
                ApplyName();
                _actions.Add(_lobby.CreateGame);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Rejoindre avec le code de la partie :", _textStyle);
            GUILayout.BeginHorizontal();
            _codeField = GUILayout.TextField(_codeField ?? "", 12, GUILayout.Width(200f));
            if (GUILayout.Button("Rejoindre", GUILayout.Height(24f)))
            {
                ApplyName();
                string code = _codeField;
                _actions.Add(() => _lobby.JoinGame(code));
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            if (GUILayout.Button("Jouer en solo", GUILayout.Height(28f))) _actions.Add(_game.PlaySolo);
        }

        private void ApplyName()
        {
            _lobby.SetPreferences(_nameField, _lobby.LocalCharacterPref, _lobby.LocalRolePref);
            _nameField = _lobby.LocalName;
        }

        private void DrawConnecting()
        {
            GUILayout.Label("Connexion...", _textStyle);
            if (GUILayout.Button("Annuler", GUILayout.Height(28f))) _actions.Add(() => _lobby.Leave(""));
        }

        // ------------------------------------------------------------------
        // Lobby
        // ------------------------------------------------------------------

        private void DrawLobby()
        {
            bool host = _game.IsHost;

            // Code a partager.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Code de la partie :", _textStyle, GUILayout.Width(170f));
            GUILayout.Label(string.IsNullOrEmpty(_lobby.JoinCode) ? "..." : _lobby.JoinCode, _codeStyle);
            if (!string.IsNullOrEmpty(_lobby.JoinCode) && GUILayout.Button("Copier", GUILayout.Width(80f), GUILayout.Height(26f)))
            {
                GUIUtility.systemCopyBuffer = _lobby.JoinCode;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            DrawPlayers(host);
            GUILayout.Space(8f);

            // Selection du personnage (portraits).
            GUILayout.Label("Votre personnage (si vous etes survivant) :", _textStyle);
            // Rangee de cartes centree.
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            int character = _lobby.LocalCharacterPref;
            bool wantsDemon = _lobby.LocalRolePref == RolePreference.Demon; // cartes estompees : servent si pas demon
            if (Card(character == -1, "Peu importe", null, WhoWants(-1), wantsDemon)) character = -1;
            for (int i = 0; i < _survivors.Count; i++)
            {
                GUILayout.Space(10f);
                if (Card(character == i, _survivors[i].Name, _survivors[i].Portrait, WhoWants(i), wantsDemon)) character = i;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            // Role souhaite, avec un apercu qui suit les choix : le demon, ou le survivant choisi.
            GUILayout.BeginHorizontal();
            DrawPreview();
            GUILayout.Space(12f);

            GUILayout.BeginVertical();
            GUILayout.Label("Role souhaite :", _textStyle);
            GUILayout.BeginHorizontal();
            RolePreference role = _lobby.LocalRolePref;
            if (Toggle(role == RolePreference.Any, "Peu importe")) role = RolePreference.Any;
            if (Toggle(role == RolePreference.Demon, "Demon")) role = RolePreference.Demon;
            if (Toggle(role == RolePreference.Survivor, "Survivant")) role = RolePreference.Survivor;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (character != _lobby.LocalCharacterPref || role != _lobby.LocalRolePref)
            {
                _actions.Add(() => _lobby.SetPreferences(_lobby.LocalName, character, role));
            }

            GUILayout.Label("Un seul demon ; l'hote l'attribue en respectant au mieux les souhaits. Deux joueurs sur le meme personnage : tirage au sort.", _smallStyle);
            GUILayout.FlexibleSpace();

            if (host)
            {
                if (GUILayout.Button("Lancer la partie", GUILayout.Height(36f))) _actions.Add(_game.LaunchRound);
            }
            else
            {
                GUILayout.Label("En attente du lancement par l'hote...", _textStyle);
            }

            if (GUILayout.Button("Quitter", GUILayout.Height(26f))) _actions.Add(() => _lobby.Leave(""));
        }

        private void DrawPlayers(bool host)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Joueurs (" + _lobby.Players.Count + " / " + _game.MaxPlayers + ")", _textStyle);
            GUILayout.FlexibleSpace();
            if (host && _lobby.Players.Count < _game.MaxPlayers && GUILayout.Button("+ Ajouter un bot (IA)", GUILayout.Width(170f), GUILayout.Height(24f)))
            {
                _actions.Add(_lobby.AddBot);
            }
            GUILayout.EndHorizontal();

            foreach (LobbyPlayer p in _lobby.Players)
            {
                GUILayout.BeginHorizontal();

                string tag = p.IsBot ? "  [IA]" : p.ClientId == 0 ? "  [hote]" : "";
                string me = !p.IsBot && p.ClientId == _game.LocalId ? "  (vous)" : "";
                GUILayout.Label("•  " + p.Name + tag + me + "   —   " + CharacterLabel(p.CharacterPref) + "  ·  " + RoleLabel(p.RolePref), _smallStyle);

                if (host && p.IsBot)
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(BotRoleButton(p.RolePref), GUILayout.Width(150f), GUILayout.Height(20f)))
                    {
                        ulong id = p.ClientId;
                        RolePreference next = (RolePreference)(((int)p.RolePref + 1) % 3);
                        _actions.Add(() => _lobby.SetBotRole(id, next));
                    }
                    ulong botId = p.ClientId;
                    if (GUILayout.Button("Retirer", GUILayout.Width(70f), GUILayout.Height(20f))) _actions.Add(() => _lobby.RemoveBot(botId));
                }

                GUILayout.EndHorizontal();
            }
        }

        /// <summary>Apercu de ce que l'on souhaite jouer (mis a jour a chaque choix).</summary>
        private void DrawPreview()
        {
            RolePreference role = _lobby.LocalRolePref;
            int character = _lobby.LocalCharacterPref;
            bool survivorKnown = character >= 0 && character < _survivors.Count;

            Texture portrait;
            string caption;

            if (role == RolePreference.Demon)
            {
                portrait = _demonPortrait;
                caption = "Demon";
            }
            else if (survivorKnown)
            {
                portrait = _survivors[character].Portrait;
                caption = role == RolePreference.Survivor ? _survivors[character].Name : _survivors[character].Name + " ?";
            }
            else
            {
                portrait = null;
                caption = role == RolePreference.Survivor ? "Survivant" : "Surprise";
            }

            GUILayout.BeginVertical(GUILayout.Width(92f));
            Rect r = GUILayoutUtility.GetRect(92f, 112f, GUILayout.Width(92f), GUILayout.Height(112f));
            Color previous = GUI.color;
            GUI.color = role == RolePreference.Demon ? new Color(0.9f, 0.12f, 0.08f, 1f) : new Color(0.35f, 0.3f, 0.3f, 1f);
            GUI.DrawTexture(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), Texture2D.whiteTexture);
            GUI.color = previous;

            if (portrait != null)
            {
                GUI.DrawTexture(r, portrait, ScaleMode.ScaleAndCrop);
            }
            else
            {
                GUI.color = new Color(0.08f, 0.06f, 0.06f, 1f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(r, "?", _codeStyle);
            }

            GUILayout.Label(caption, _cardNameStyle, GUILayout.Width(92f));
            GUILayout.EndVertical();
        }

        /// <summary>Carte cliquable : portrait (ou "?"), nom, joueurs qui l'ont choisie.</summary>
        private bool Card(bool selected, string label, Texture portrait, string wanted, bool dimmed)
        {
            GUILayout.BeginVertical(GUILayout.Width(CardWidth));

            Rect r = GUILayoutUtility.GetRect(CardWidth, PortraitHeight, GUILayout.Width(CardWidth), GUILayout.Height(PortraitHeight));
            Color previous = GUI.color;
            GUI.color = selected ? new Color(0.95f, 0.3f, 0.2f, 1f) : new Color(0.25f, 0.22f, 0.22f, 1f);
            GUI.DrawTexture(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), Texture2D.whiteTexture);
            GUI.color = previous;

            if (portrait != null)
            {
                GUI.DrawTexture(r, portrait, ScaleMode.ScaleAndCrop);
            }
            else
            {
                GUI.color = new Color(0.08f, 0.06f, 0.06f, 1f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(r, "?", _codeStyle);
            }

            if (dimmed)
            {
                // Role Demon souhaite : le personnage ne servira que si l'on est finalement survivant.
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = previous;
            }

            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);

            GUILayout.Label(label, _cardNameStyle, GUILayout.Width(CardWidth));
            // Toujours une ligne (vide ou non) : la mise en page ne bouge pas quand les choix changent.
            GUILayout.Label(wanted ?? "", _cardWantStyle, GUILayout.Width(CardWidth), GUILayout.Height(16f));

            GUILayout.EndVertical();
            return clicked && !selected;
        }

        /// <summary>Noms des autres joueurs qui souhaitent ce personnage.</summary>
        private string WhoWants(int character)
        {
            string names = "";
            foreach (LobbyPlayer p in _lobby.Players)
            {
                if (p.CharacterPref != character || (!p.IsBot && p.ClientId == _game.LocalId)) continue;
                names += (names.Length > 0 ? ", " : "") + p.Name;
            }

            return names;
        }

        private static bool Toggle(bool on, string label)
        {
            return GUILayout.Toggle(on, label, "Button", GUILayout.Height(26f)) && !on;
        }

        private string CharacterLabel(int index)
        {
            if (index < 0 || index >= _survivors.Count) return "perso : peu importe";
            return "perso : " + _survivors[index].Name;
        }

        private static string RoleLabel(RolePreference pref)
        {
            return pref == RolePreference.Demon ? "veut etre Demon" : pref == RolePreference.Survivor ? "veut etre Survivant" : "role : peu importe";
        }

        private static string BotRoleButton(RolePreference pref)
        {
            return pref == RolePreference.Demon ? "Bot : Demon" : pref == RolePreference.Survivor ? "Bot : Survivant" : "Bot : peu importe";
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
            _codeStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _codeStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            _cardNameStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            _cardNameStyle.normal.textColor = new Color(0.92f, 0.9f, 0.86f);
            _cardWantStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperCenter, clipping = TextClipping.Clip };
            _cardWantStyle.normal.textColor = new Color(0.95f, 0.6f, 0.45f);
        }
    }
}
