using HouseOfSilence.Interaction;
using HouseOfSilence.Level;
using HouseOfSilence.Player;
using HouseOfSilence.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Outil de test : F2 passe du survivant (vue FPS, ou TPS avec F4) au demon (vue TPS) et inversement.
    /// Le personnage non joue reste immobile dans le monde : en demon, le survivant
    /// devient une cible visible (corps affiche) pour tester les pouvoirs.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayableCharacterSwitcher : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private bool enableSwitch = true;
        [SerializeField] private string toggleBinding = "<Keyboard>/f2";
        [SerializeField] private bool startAsDemon = false;
        [SerializeField] private bool showHint = true;

        [Header("Personnages")]
        [SerializeField] private PlayerCharacter survivor;
        [SerializeField] private DemonController demon;
        [Tooltip("Corps visible du survivant, affiche seulement quand on joue le demon.")]
        [SerializeField] private GameObject survivorBody;

        [Header("HUD a rebrancher")]
        [SerializeField] private CompassHud compass;
        [SerializeField] private ZoneTitleDisplay zoneTitle;
        [SerializeField] private ForestMap3D map;

        private InputAction _toggle;
        private bool _pending;
        private bool _demonMode;
        private InputReader _survivorInput;
        private GUIStyle _hintStyle;

        public bool DemonMode { get { return _demonMode; } }

        private void Awake()
        {
            if (survivor == null) survivor = FindAnyObjectByType<PlayerCharacter>();
            if (demon == null) demon = FindAnyObjectByType<DemonController>();
            if (compass == null) compass = FindAnyObjectByType<CompassHud>();
            if (zoneTitle == null) zoneTitle = FindAnyObjectByType<ZoneTitleDisplay>();
            if (map == null) map = FindAnyObjectByType<ForestMap3D>();
            if (survivor != null) _survivorInput = survivor.GetComponent<InputReader>();
        }

        private void Start()
        {
            SetDemonMode(startAsDemon && enableSwitch);
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

            if (toggle && enableSwitch && allowed)
            {
                SetDemonMode(!_demonMode);
            }
            else if (!enableSwitch && _demonMode)
            {
                SetDemonMode(false);
            }
        }

        public void SetDemonMode(bool demonMode)
        {
            if (survivor == null || demon == null)
            {
                demonMode = false;
            }

            _demonMode = demonMode;

            if (survivor != null)
            {
                // Le survivant se fige : plus de deplacement, de regard, de lampe, de vol ni d'interaction.
                FlyMode fly = survivor.GetComponent<FlyMode>();
                if (fly != null && demonMode) fly.SetFlying(false);

                SetEnabled<PlayerMotor>(!demonMode);
                SetEnabled<PlayerLook>(!demonMode);
                SetEnabled<Flashlight>(!demonMode);
                SetEnabled<FlyMode>(!demonMode);
                SetEnabled<PlayerInteractor>(!demonMode);

                Camera survivorCamera = survivor.Camera;
                if (survivorCamera != null)
                {
                    survivorCamera.enabled = !demonMode;
                    AudioListener listener = survivorCamera.GetComponent<AudioListener>();
                    if (listener != null) listener.enabled = !demonMode;
                }

                // En vue TPS (F4), SurvivorViewMode reaffiche le corps au retour.
                if (survivorBody != null) survivorBody.SetActive(demonMode);

                // Test : on reprend le survivant bien vivant (tue par le demon juste avant).
                SurvivorDeath death = survivor.GetComponent<SurvivorDeath>();
                if (death != null && !demonMode) death.Revive();
            }

            if (demon != null)
            {
                demon.SetControlled(demonMode);
            }

            Transform body = demonMode ? demon.transform : (survivor != null ? survivor.transform : null);
            Camera view = demonMode ? demon.Camera : (survivor != null ? survivor.Camera : null);

            if (compass != null && view != null) compass.SetView(view.transform);
            if (zoneTitle != null && body != null) zoneTitle.SetPlayer(body);
            if (map != null) map.SetControlled(demonMode ? demon.transform : null, demonMode ? demon.Camera : null);
        }

        private void SetEnabled<T>(bool value) where T : Behaviour
        {
            T component = survivor.GetComponent<T>();
            if (component != null) component.enabled = value;
        }

        private void OnGUI()
        {
            if (!enableSwitch || !showHint || SurvivorJumpscare.AnyPlaying || (_survivorInput != null && !_survivorInput.InputEnabled))
            {
                return;
            }

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.LowerLeft };
                _hintStyle.normal.textColor = new Color(0.8f, 0.78f, 0.72f, 0.6f);
            }

            string text = _demonMode
                ? "DEMON   ZQSD · souris · Maj courir · 1 Vision · 2 Cri · 3 Teleport · C pleurer · E tuer (a 1 m) · N vision nocturne   ·   F2 revenir au survivant"
                : "F2  jouer le demon (test)   ·   F4  vue FPS / TPS";
            GUI.Label(new Rect(14f, Screen.height - 34f, 900f, 24f), text, _hintStyle);
        }
    }
}
