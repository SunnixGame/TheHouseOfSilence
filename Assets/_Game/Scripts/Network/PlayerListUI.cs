using HouseOfSilence.Demon;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Network
{
    /// <summary>
    /// Liste des joueurs, affichee tant que TAB est enfonce (HUD local) :
    ///
    ///   JOUEURS
    ///   ● Player 1 — Vivant
    ///   † Player 2 — Mort
    ///   ◆ Player 4 — Demon
    ///
    /// En ligne, elle vient de GameStateManager : un survivant ne voit pas qui est le demon
    /// (il apparait comme un joueur vivant), sauf en fin de partie. En solo : les
    /// personnages de la scene. L'heure et la meteo partagees sont rappelees en bas.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerListUI : MonoBehaviour
    {
        [SerializeField] private string binding = "<Keyboard>/tab";

        private NetworkGameManager _game;
        private InputAction _show;
        private GUIStyle _titleStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _footStyle;

        private void Awake()
        {
            _game = GetComponent<NetworkGameManager>();
        }

        private void OnEnable()
        {
            _show = new InputAction("PlayerList", InputActionType.Button, binding);
            _show.Enable();
        }

        private void OnDisable()
        {
            if (_show == null) return;
            _show.Disable();
            _show.Dispose();
            _show = null;
        }

        /// <summary>Ligne de liste en texte riche (aussi utilisee par l'ecran de fin).</summary>
        public static string Line(PlayerView v, bool revealAll)
        {
            string me = v.IsMe ? "  <color=#9a9a9a>(vous)</color>" : "";
            string gone = v.Connected ? "" : "  <color=#9a9a9a>(deconnecte)</color>";

            if (v.IsDemon) return "<color=#e03a2a>◆</color>  " + v.Name + " — Demon" + me + gone;
            if (!v.Alive) return "<color=#8a8a8a>†</color>  " + v.Name + " — Mort" + me + gone;
            return "<color=#45d45a>●</color>  " + v.Name + " — Vivant" + me + gone;
        }

        private void OnGUI()
        {
            if (_show == null || !_show.IsPressed()) return;
            if (_game.Phase != NetPhase.Playing && _game.Phase != NetPhase.Solo && _game.Phase != NetPhase.GameOver) return;

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                _titleStyle.normal.textColor = new Color(0.9f, 0.88f, 0.84f);
                _rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
                _rowStyle.normal.textColor = new Color(0.9f, 0.88f, 0.84f);
                _footStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperCenter };
                _footStyle.normal.textColor = new Color(0.7f, 0.68f, 0.64f);
            }

            bool online = _game.Phase != NetPhase.Solo;
            int rows = online ? _game.GameState.View.Count : SoloCount();
            float w = 420f;
            float h = 80f + rows * 26f;
            Rect box = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.18f, w, h);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(box.x, box.y + 10f, w, 28f), "JOUEURS", _titleStyle);
            float y = box.y + 44f;

            if (online)
            {
                foreach (PlayerView v in _game.GameState.View)
                {
                    GUI.Label(new Rect(box.x + 24f, y, w - 48f, 24f), Line(v, false), _rowStyle);
                    y += 26f;
                }

                if (_game.TimeOfDay.Active)
                {
                    GUI.Label(new Rect(box.x, box.y + h - 26f, w, 20f), _game.TimeOfDay.Clock() + "   ·   " + _game.TimeOfDay.WeatherName(), _footStyle);
                }
            }
            else
            {
                DrawSolo(box, ref y);
            }
        }

        private int SoloCount()
        {
            return _game.Switcher != null ? _game.Switcher.Characters().Count : 0;
        }

        private void DrawSolo(Rect box, ref float y)
        {
            if (_game.Switcher == null) return;

            PlayerCharacter controlled = _game.Switcher.ControlledSurvivor;

            foreach (Component c in _game.Switcher.Characters())
            {
                PlayerCharacter survivor = c as PlayerCharacter;
                SurvivorDeath death = c.GetComponent<SurvivorDeath>();

                PlayerView v = new PlayerView
                {
                    Name = survivor != null ? survivor.DisplayName : "Le Demon",
                    Alive = death == null || !death.IsDead,
                    IsDemon = c is DemonController,
                    IsMe = survivor != null ? survivor == controlled : _game.Switcher.DemonMode
                };

                GUI.Label(new Rect(box.x + 24f, y, box.width - 48f, 24f), Line(v, true), _rowStyle);
                y += 26f;
            }
        }
    }
}
