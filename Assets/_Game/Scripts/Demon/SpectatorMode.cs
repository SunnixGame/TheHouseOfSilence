using System.Collections.Generic;
using HouseOfSilence.Interaction;
using HouseOfSilence.Level;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Mode spectateur : quand le survivant joue ici est mort (jumpscare termine), sa camera
    /// suit les autres personnages encore en vie, survivants et demon, en vue a la troisieme
    /// personne. Clic gauche / fleche droite : suivant ; clic droit / fleche gauche :
    /// precedent ; souris : tourner autour ; molette : zoom. Le mode se ferme tout seul si le
    /// survivant se releve (outil de test F2) ou si l'on change de personnage.
    ///
    /// Ajoute automatiquement par PlayableCharacterSwitcher.
    /// </summary>
    [DefaultExecutionOrder(300)] // apres SurvivorViewMode (200) : c'est nous qui placons la camera
    [DisallowMultipleComponent]
    public class SpectatorMode : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float delayAfterDeath = 2.5f;
        [SerializeField] private float pivotHeight = 1.3f;
        [SerializeField] private float distance = 3.5f;
        [SerializeField] private Vector2 distanceRange = new Vector2(1.5f, 10f);
        [SerializeField] private Vector2 pitchRange = new Vector2(-20f, 70f);
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.12f;
        [SerializeField, Min(0f)] private float zoomSensitivity = 0.004f;
        [SerializeField, Min(0f)] private float collisionRadius = 0.2f;

        private PlayableCharacterSwitcher _switcher;
        private ZoneTitleDisplay _zoneTitle;

        private PlayerCharacter _me;
        private bool _active;
        private float _deadSince = -1f;
        private readonly List<Component> _targets = new List<Component>();
        private Component _target;
        private float _yaw;
        private float _pitch = 15f;

        private readonly List<Behaviour> _disabled = new List<Behaviour>();

        private InputAction _next;
        private InputAction _previous;
        private InputAction _look;
        private InputAction _zoom;
        private int _step;
        private float _nextRefresh;

        private GUIStyle _titleStyle;
        private GUIStyle _hintStyle;

        public bool IsActive { get { return _active; } }

        private void Awake()
        {
            _switcher = GetComponent<PlayableCharacterSwitcher>();
            _zoneTitle = FindAnyObjectByType<ZoneTitleDisplay>();
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

            _look = new InputAction("SpectateLook", InputActionType.Value, "<Mouse>/delta");
            _zoom = new InputAction("SpectateZoom", InputActionType.Value, "<Mouse>/scroll/y");

            _next.Enable();
            _previous.Enable();
            _look.Enable();
            _zoom.Enable();
        }

        private void OnDisable()
        {
            if (_active) Exit();

            foreach (InputAction a in new[] { _next, _previous, _look, _zoom })
            {
                if (a == null) continue;
                a.Disable();
                a.Dispose();
            }

            _next = _previous = _look = _zoom = null;
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

            if (!_active)
            {
                _step = 0;
                return;
            }

            bool inputAllowed = _me.Input == null || _me.Input.InputEnabled;
            int step = _step;
            _step = 0;

            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + 0.5f;
                RefreshTargets();
            }

            if (_target == null || !_targets.Contains(_target)) step = step != 0 ? step : 1;
            if (inputAllowed && step != 0) Cycle(step);

            if (inputAllowed)
            {
                Vector2 look = _look.ReadValue<Vector2>() * mouseSensitivity;
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, pitchRange.x, pitchRange.y);
                distance = Mathf.Clamp(distance - _zoom.ReadValue<float>() * zoomSensitivity, distanceRange.x, distanceRange.y);
            }
        }

        private void LateUpdate()
        {
            if (!_active || _me == null || _me.Camera == null) return;

            // Personne a regarder : on reste au-dessus de son propre corps.
            Transform focus = _target != null ? _target.transform : _me.transform;
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = focus.position + Vector3.up * pivotHeight;
            Vector3 dir = rotation * Vector3.back;
            float allowed = distance;

            RaycastHit[] hits = Physics.SphereCastAll(pivot, collisionRadius, dir, distance, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.distance <= 0f || h.collider.transform.IsChildOf(focus) || h.collider.transform.IsChildOf(_me.transform)) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue; // cadavres
                allowed = Mathf.Min(allowed, h.distance);
            }

            Transform cam = _me.Camera.transform;
            cam.SetPositionAndRotation(pivot + dir * Mathf.Max(0.3f, allowed), rotation);
        }

        // ------------------------------------------------------------------

        private void Enter(PlayerCharacter me)
        {
            _me = me;
            _active = true;
            _target = null;

            // Le mort ne regarde, ne vole et n'interagit plus : sa camera sert de camera spectateur.
            _disabled.Clear();
            Disable(me.GetComponent<PlayerLook>());
            Disable(me.GetComponent<FlyMode>());
            Disable(me.GetComponent<PlayerInteractor>());

            RefreshTargets();
            Cycle(1);
        }

        private void Exit()
        {
            foreach (Behaviour b in _disabled)
            {
                if (b != null) b.enabled = true;
            }

            _disabled.Clear();

            if (_zoneTitle != null && _me != null) _zoneTitle.SetPlayer(_me.transform);

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

        /// <summary>Personnages encore en vie, sauf soi : survivants puis demon.</summary>
        private void RefreshTargets()
        {
            _targets.Clear();
            if (_switcher == null) return;

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
                return;
            }

            int index = _target != null ? _targets.IndexOf(_target) : -1;
            if (index < 0) index = step > 0 ? -1 : 0;
            index = ((index + step) % _targets.Count + _targets.Count) % _targets.Count;

            _target = _targets[index];
            _yaw = _target.transform.eulerAngles.y;
            if (_zoneTitle != null) _zoneTitle.SetPlayer(_target.transform);
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

            string who = _target == null ? "personne (tout le monde est mort)" : Name(_target);
            GUI.Label(new Rect(0f, 18f, Screen.width, 30f), "MODE SPECTATEUR  —  vous regardez : " + who, _titleStyle);
            GUI.Label(new Rect(0f, 46f, Screen.width, 22f), "Clic gauche / →  suivant   ·   Clic droit / ←  precedent   ·   souris : tourner   ·   molette : zoom", _hintStyle);
        }

        private static string Name(Component c)
        {
            PlayerCharacter survivor = c as PlayerCharacter;
            return survivor != null ? survivor.DisplayName : "Le Demon";
        }
    }
}
