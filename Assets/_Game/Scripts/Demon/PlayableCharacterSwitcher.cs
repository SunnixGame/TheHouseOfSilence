using System.Collections.Generic;
using HouseOfSilence.Interaction;
using HouseOfSilence.Level;
using HouseOfSilence.Player;
using HouseOfSilence.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Personnage joue : au lancement, un personnage est tire au hasard parmi tous les
    /// survivants (celui d'origine et les PNJ jouables de NpcBystanders) et le demon.
    /// F2 passe au personnage suivant (survivants dans l'ordre, puis le demon).
    ///
    /// Les personnages non joues restent immobiles dans le monde, corps visible : ce sont
    /// des cibles pour le demon (kill, vision, teleportation) et des temoins pour les autres.
    /// Survivant joue : vue FPS, ou TPS avec F4. Demon : vue TPS.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayableCharacterSwitcher : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private bool enableSwitch = true;
        [SerializeField] private string toggleBinding = "<Keyboard>/f2";
        [Tooltip("Personnage tire au hasard au lancement (survivants et demon).")]
        [SerializeField] private bool randomStart = true;
        [Tooltip("Sans tirage au hasard : commencer avec le demon plutot que le survivant d'origine.")]
        [SerializeField] private bool startAsDemon = false;
        [SerializeField] private bool showHint = true;

        [Header("Personnages")]
        [Tooltip("Survivant d'origine (les autres sont trouves au lancement).")]
        [SerializeField] private PlayerCharacter survivor;
        [SerializeField] private DemonController demon;
        [Tooltip("Corps visible du survivant d'origine (les autres : leur enfant 'SurvivorBody').")]
        [SerializeField] private GameObject survivorBody;

        [Header("HUD a rebrancher")]
        [SerializeField] private CompassHud compass;
        [SerializeField] private ZoneTitleDisplay zoneTitle;
        [SerializeField] private ForestMap3D map;

        private readonly List<PlayerCharacter> _survivors = new List<PlayerCharacter>();
        private InputAction _toggle;
        private bool _pending;
        private int _current = -1;
        private InputReader _survivorInput;
        private GUIStyle _hintStyle;
        private GUIStyle _nameStyle;
        private float _showNameUntil;
        private bool _locked;

        /// <summary>On joue le demon.</summary>
        public bool DemonMode { get { return _current >= 0 && _current >= _survivors.Count; } }

        /// <summary>Survivant joue, ou null si on joue le demon.</summary>
        public PlayerCharacter ControlledSurvivor { get { return _current >= 0 && _current < _survivors.Count ? _survivors[_current] : null; } }

        private int CharacterCount { get { return _survivors.Count + (demon != null ? 1 : 0); } }

        /// <summary>
        /// Tous les personnages jouables, dans un ordre identique sur chaque machine :
        /// survivant d'origine, PNJ par identifiant, puis le demon.
        /// </summary>
        public List<Component> Characters()
        {
            CollectSurvivors();
            List<Component> all = new List<Component>(_survivors);
            if (demon != null) all.Add(demon);
            return all;
        }

        /// <summary>
        /// En ligne : donne le controle de ce personnage (survivant ou demon) et bloque F2.
        /// Le demon, s'il n'est pas le notre, est pilote a distance.
        /// </summary>
        public void ControlOnly(Component character)
        {
            CollectSurvivors();
            _locked = true;

            int index = character == demon && demon != null ? _survivors.Count : _survivors.IndexOf(character as PlayerCharacter);
            if (index < 0) return;

            if (demon != null) demon.ExternallyDriven = character != demon;
            Select(index, false);
        }

        /// <summary>Fin de partie en ligne : F2 et le demon local redeviennent disponibles.</summary>
        public void Unlock()
        {
            _locked = false;
            if (demon != null) demon.ExternallyDriven = false;
        }

        private void Awake()
        {
            if (survivor == null) survivor = FindAnyObjectByType<PlayerCharacter>();
            if (demon == null) demon = FindAnyObjectByType<DemonController>();
            if (compass == null) compass = FindAnyObjectByType<CompassHud>();
            if (zoneTitle == null) zoneTitle = FindAnyObjectByType<ZoneTitleDisplay>();
            if (map == null) map = FindAnyObjectByType<ForestMap3D>();
            if (survivor != null) _survivorInput = survivor.GetComponent<InputReader>();

            // Survivant joue mort : mode spectateur sur les personnages encore en vie.
            if (GetComponent<SpectatorController>() == null) gameObject.AddComponent<SpectatorController>();
        }

        // Apres NpcBystanders (PNJ changes en survivants) et RandomSpawner.
        private void Start()
        {
            CollectSurvivors();

            int start;
            if (enableSwitch && randomStart && CharacterCount > 0)
            {
                start = Random.Range(0, CharacterCount);
            }
            else
            {
                start = enableSwitch && startAsDemon && demon != null ? _survivors.Count : Mathf.Max(0, _survivors.IndexOf(survivor));
            }

            Select(start, false);
        }

        private void OnEnable()
        {
            _toggle = new InputAction("SwitchCharacter", InputActionType.Button, toggleBinding);
            _toggle.performed += _ => _pending = true;
            _toggle.Enable();
        }

        private void OnDisable()
        {
            if (_toggle != null)
            {
                _toggle.Disable();
                _toggle.Dispose();
                _toggle = null;
            }
        }

        private void Update()
        {
            bool toggle = _pending;
            _pending = false;

            bool allowed = _survivorInput == null || _survivorInput.InputEnabled;

            if (toggle && enableSwitch && !_locked && allowed && CharacterCount > 1)
            {
                CollectSurvivors();
                Select((_current + 1) % CharacterCount, true);
            }
        }

        /// <summary>Compatibilite : true = le demon, false = le survivant d'origine.</summary>
        public void SetDemonMode(bool demonMode)
        {
            CollectSurvivors();
            Select(demonMode && demon != null ? _survivors.Count : Mathf.Max(0, _survivors.IndexOf(survivor)), true);
        }

        private void CollectSurvivors()
        {
            PlayerCharacter current = ControlledSurvivor;
            bool demonMode = DemonMode;

            _survivors.Clear();
            foreach (PlayerCharacter p in FindObjectsByType<PlayerCharacter>())
            {
                if (p != null && p.isActiveAndEnabled) _survivors.Add(p);
            }

            // Survivant d'origine en premier, puis les PNJ dans l'ordre de leur identifiant.
            _survivors.Sort((a, b) => a == b ? 0 : a == survivor ? -1 : b == survivor ? 1 : a.PlayerId.CompareTo(b.PlayerId));

            if (demonMode) _current = _survivors.Count;
            else if (current != null) _current = _survivors.IndexOf(current);
        }

        /// <summary>
        /// Donne le controle au personnage 'index' (survivants, puis le demon).
        /// 'reviveTarget' : un survivant repris alors qu'il est mort se releve (outil de test).
        /// </summary>
        private void Select(int index, bool reviveTarget)
        {
            if (CharacterCount == 0) return;

            _current = Mathf.Clamp(index, 0, CharacterCount - 1);
            PlayerCharacter controlled = ControlledSurvivor;
            bool demonMode = controlled == null && demon != null;

            foreach (PlayerCharacter s in _survivors)
            {
                ApplySurvivor(s, s == controlled, reviveTarget);
            }

            if (demon != null)
            {
                demon.SetControlled(demonMode);
            }

            Transform body = demonMode ? demon.transform : (controlled != null ? controlled.transform : null);
            Camera view = demonMode ? demon.Camera : (controlled != null ? controlled.Camera : null);

            if (compass != null && view != null) compass.SetView(view.transform);
            if (zoneTitle != null && body != null) zoneTitle.SetPlayer(body);
            if (map != null) map.SetControlled(body, view);

            _showNameUntil = Time.time + 4f;
        }

        private void ApplySurvivor(PlayerCharacter s, bool controlled, bool reviveTarget)
        {
            // Le survivant non joue se fige : plus de deplacement, de regard, de lampe, de vol ni
            // d'interaction. Son InputReader reste actif : les actions sont partagees entre tous.
            FlyMode fly = s.GetComponent<FlyMode>();
            if (fly != null && !controlled) fly.SetFlying(false);

            SetEnabled<PlayerMotor>(s, controlled);
            SetEnabled<PlayerLook>(s, controlled);
            SetEnabled<Flashlight>(s, controlled);
            SetEnabled<FlyMode>(s, controlled);
            SetEnabled<PlayerInteractor>(s, controlled);

            Camera cam = s.Camera;
            if (cam != null)
            {
                cam.enabled = controlled;
                AudioListener listener = cam.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = controlled;
            }

            // Corps : visible des autres ; pour le joueur, SurvivorViewMode l'affiche en TPS (F4).
            GameObject bodyObject = s == survivor && survivorBody != null ? survivorBody : FindBody(s);
            if (bodyObject != null) bodyObject.SetActive(!controlled);

            // Joueur local (HUD de peur, objectifs...).
            if (s.IsLocalPlayer != controlled)
            {
                s.SetIdentity(s.PlayerId, s.DisplayName, controlled);

                if (PlayerManager.HasInstance && PlayerManager.Instance != null)
                {
                    PlayerManager.Instance.Unregister(s);
                    PlayerManager.Instance.Register(s);
                }
            }

            // Test : on reprend le survivant bien vivant (tue par le demon juste avant).
            SurvivorDeath death = s.GetComponent<SurvivorDeath>();
            if (death != null && controlled && reviveTarget) death.Revive();
        }

        private static GameObject FindBody(PlayerCharacter s)
        {
            Transform body = s.transform.Find("SurvivorBody");
            return body != null ? body.gameObject : null;
        }

        private static void SetEnabled<T>(PlayerCharacter s, bool value) where T : Behaviour
        {
            T component = s.GetComponent<T>();
            if (component != null) component.enabled = value;
        }

        private void OnGUI()
        {
            if ((!enableSwitch && !_locked) || !showHint || SurvivorJumpscare.AnyPlaying || (_survivorInput != null && !_survivorInput.InputEnabled))
            {
                return;
            }

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.LowerLeft };
                _hintStyle.normal.textColor = new Color(0.8f, 0.78f, 0.72f, 0.6f);
                _nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            }

            PlayerCharacter controlled = ControlledSurvivor;
            string who = controlled != null ? controlled.DisplayName : "Le Demon";

            string text = DemonMode
                ? "DEMON   ZQSD · souris · Maj courir · 1 Vision · 2 Cri · 3 Teleport · C pleurer · E tuer (a 1 m) · N vision nocturne   ·   F2 personnage suivant"
                : "Vous jouez : " + who + "   ·   F2  personnage suivant   ·   F4  vue FPS / TPS";
            if (!_locked) GUI.Label(new Rect(14f, Screen.height - 34f, 1100f, 24f), text, _hintStyle);

            // Nom du personnage tire, quelques secondes apres chaque changement.
            float left = _showNameUntil - Time.time;
            if (left > 0f)
            {
                _nameStyle.normal.textColor = new Color(0.85f, 0.8f, 0.7f, Mathf.Clamp01(left));
                GUI.Label(new Rect(0f, Screen.height * 0.12f, Screen.width, 40f), "Vous jouez : " + who, _nameStyle);
            }
        }
    }
}
