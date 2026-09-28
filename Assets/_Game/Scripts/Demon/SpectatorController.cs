using System.Collections.Generic;
using HouseOfSilence.Interaction;
using HouseOfSilence.Level;
using HouseOfSilence.Network;
using HouseOfSilence.Player;
using HouseOfSilence.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Mode spectateur : quand le survivant joue ici est mort (jumpscare termine), il
    /// devient spectateur.
    ///  - sa camera et son ecoute audio sont coupees, son effet VHS aussi (local) ;
    ///  - une camera spectateur neuve (SpectatorCameraController) suit les joueurs encore
    ///    en vie ; la nuit, le brouillard et la meteo (globaux) restent les memes ;
    ///  - clic gauche / fleche droite : suivant ; clic droit / fleche gauche : precedent ;
    ///  - le joueur observe est affiche en haut de l'ecran.
    /// En ligne, la liste vient de GameStateManager (le demon y reste anonyme) ; en solo,
    /// ce sont les personnages encore en vie. Se ferme si le survivant se releve (F2 en
    /// solo) ou si l'on change de personnage.
    ///
    /// Ajoute automatiquement par PlayableCharacterSwitcher.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpectatorController : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float delayAfterDeath = 2.5f;

        private PlayableCharacterSwitcher _switcher;
        private ZoneTitleDisplay _zoneTitle;
        private CompassHud _compass;

        private PlayerCharacter _me;
        private bool _active;
        private float _deadSince = -1f;
        private SpectatorCameraController _camera;
        private readonly List<Component> _targets = new List<Component>();
        private Component _target;
        private float _nextRefresh;

        private readonly List<Behaviour> _disabled = new List<Behaviour>();
        private AudioListener _myListener;

        private InputAction _next;
        private InputAction _previous;
        private int _step;

        private GUIStyle _titleStyle;
        private GUIStyle _hintStyle;

        public bool IsActive { get { return _active; } }

        private void Awake()
        {
            _switcher = GetComponent<PlayableCharacterSwitcher>();
            _zoneTitle = FindAnyObjectByType<ZoneTitleDisplay>();
            _compass = FindAnyObjectByType<CompassHud>();
        }

        private void OnEnable()
        {
            _next = new InputAction("SpectateNext", InputActionType.Button);
            _next.AddBinding("<Mouse>/leftButton");
            _next.AddBinding("<Keyboard>/rightArrow");
            _next.performed += _ => _step++;

            _previous = new InputAction("SpectatePrevious", InputActionType.Button);
            _previous.AddBinding("<Mouse>/rightButton");
            _previous.AddBinding("<Keyboard>/leftArrow");
            _previous.performed += _ => _step--;

            _next.Enable();
            _previous.Enable();
        }

        private void OnDisable()
        {
            if (_active) Exit();

            foreach (InputAction a in new[] { _next, _previous })
            {
                if (a == null) continue;
                a.Disable();
                a.Dispose();
            }

            _next = _previous = null;
        }

        private void Update()
        {
            PlayerCharacter me = _switcher != null ? _switcher.ControlledSurvivor : null;
            SurvivorDeath death = me != null ? me.GetComponent<SurvivorDeath>() : null;
            bool dead = death != null && death.IsDead;

            if (dead)
            {
                if (_deadSince < 0f) _deadSince = Time.time;
            }
            else
            {
                _deadSince = -1f;
            }

            bool want = dead && !SurvivorJumpscare.AnyPlaying && Time.time - _deadSince >= delayAfterDeath;

            if (_active && (!want || me != _me)) Exit();
            if (!_active && want) Enter(me);

            int step = _step;
            _step = 0;
            if (!_active) return;

            bool inputAllowed = _me.Input == null || _me.Input.InputEnabled;
            if (_camera != null) _camera.InputAllowed = inputAllowed;

            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + 0.5f;
                RefreshTargets();
            }

            // La cible est morte ou partie : on passe a la suivante.
            if (_target == null || !_targets.Contains(_target)) step = step != 0 ? step : 1;
            if (step != 0 && (inputAllowed || _target == null || !_targets.Contains(_target))) Cycle(step);
        }

        // ------------------------------------------------------------------

        private void Enter(PlayerCharacter me)
        {
            _me = me;
            _active = true;
            _target = null;
            _disabled.Clear();

            // Le mort ne regarde, ne vole et n'interagit plus ; son effet VHS (local) s'eteint.
            Disable(me.GetComponent<PlayerLook>());
            Disable(me.GetComponent<FlyMode>());
            Disable(me.GetComponent<PlayerInteractor>());
            Disable(me.GetComponent<Flashlight>());
            Disable(me.GetComponent<DemonProximityVHS>());

            // Sa camera (avec le quad VHS et les lumieres de jumpscare) est coupee...
            Camera own = me.Camera;
            if (own != null)
            {
                _myListener = own.GetComponent<AudioListener>();
                Disable(_myListener);
                Disable(own);
            }

            // ... et remplacee par une camera spectateur neuve.
            _camera = SpectatorCameraController.Create(own);
            if (_compass != null) _compass.SetView(_camera.transform);

            RefreshTargets();
            Cycle(1);
        }

        private void Exit()
        {
            if (_camera != null) Destroy(_camera.gameObject);
            _camera = null;

            foreach (Behaviour b in _disabled)
            {
                if (b != null) b.enabled = true;
            }

            _disabled.Clear();

            if (_me != null)
            {
                if (_zoneTitle != null) _zoneTitle.SetPlayer(_me.transform);
                if (_compass != null && _me.Camera != null) _compass.SetView(_me.Camera.transform);
            }

            _active = false;
            _target = null;
            _me = null;
        }

        private void Disable(Behaviour b)
        {
            if (b == null || !b.enabled) return;
            b.enabled = false;
            _disabled.Add(b);
        }

        /// <summary>Joueurs encore en vie, sauf soi.</summary>
        private void RefreshTargets()
        {
            _targets.Clear();
            if (_switcher == null) return;

            NetworkGameManager game = NetworkGameManager.Instance;
            bool online = NetBridge.Online && game != null && game.GameState.View.Count > 0;

            if (online)
            {
                // Survivants joues et vivants (d'apres l'hote), puis la creature elle-meme.
                foreach (PlayerView v in game.GameState.View)
                {
                    if (v.IsMe || !v.Alive || !v.Connected || v.Character < 0) continue;
                    Component c = v.Character < game.Characters.Count ? game.Characters[v.Character] : null;
                    if (c != null && !(c is DemonController) && !_targets.Contains(c)) _targets.Add(c);
                }

                foreach (Component c in game.Characters)
                {
                    if (c is DemonController) _targets.Add(c);
                }

                return;
            }

            foreach (Component c in _switcher.Characters())
            {
                if (c == null || c == _me) continue;

                SurvivorDeath death = c.GetComponent<SurvivorDeath>();
                if (death != null && death.IsDead) continue;

                _targets.Add(c);
            }
        }

        private void Cycle(int step)
        {
            if (_targets.Count == 0)
            {
                _target = null;
                if (_camera != null && _me != null) _camera.SetTarget(_me.transform);
                return;
            }

            int index = _target != null ? _targets.IndexOf(_target) : -1;
            if (index < 0) index = step > 0 ? -1 : 0;
            index = ((index + step) % _targets.Count + _targets.Count) % _targets.Count;

            _target = _targets[index];
            if (_camera != null) _camera.SetTarget(_target.transform);
            if (_zoneTitle != null) _zoneTitle.SetPlayer(_target.transform);
        }

        private string TargetName()
        {
            if (_target == null) return "personne (tout le monde est mort)";

            NetworkGameManager game = NetworkGameManager.Instance;
            if (NetBridge.Online && game != null)
            {
                int index = game.Characters.IndexOf(_target);
                if (index >= 0) return game.NameOf(index);
            }

            PlayerCharacter survivor = _target as PlayerCharacter;
            return survivor != null ? survivor.DisplayName : "Le Demon";
        }

        private void OnGUI()
        {
            if (!_active || SurvivorJumpscare.AnyPlaying) return;

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                _titleStyle.normal.textColor = new Color(0.9f, 0.25f, 0.2f, 0.95f);
                _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter };
                _hintStyle.normal.textColor = new Color(0.85f, 0.82f, 0.78f, 0.8f);
            }

            GUI.Label(new Rect(0f, 18f, Screen.width, 30f), "SPECTATEUR  —  vous observez : " + TargetName(), _titleStyle);
            GUI.Label(new Rect(0f, 46f, Screen.width, 22f), "Clic gauche / →  suivant   ·   Clic droit / ←  precedent   ·   souris : tourner   ·   molette : zoom   ·   TAB : joueurs", _hintStyle);
        }
    }
}
