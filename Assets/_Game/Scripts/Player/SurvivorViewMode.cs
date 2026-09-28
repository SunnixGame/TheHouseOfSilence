using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Vue du survivant : F4 bascule entre la vue FPS et la vue TPS (par-dessus l'epaule).
    ///
    /// En TPS, la camera recule derriere le pivot (PlayerLook continue de gerer le regard,
    /// le corps suit donc la souris) et le decor la rapproche au lieu d'etre traverse.
    /// Le corps du survivant est affiche ; s'il n'y en a pas dans la scene, une capsule
    /// provisoire le remplace.
    ///
    /// Ajoute automatiquement par PlayerCharacter si absent. Rien ne se passe quand les
    /// inputs sont coupes (pause, carte) ni quand le survivant est fige (PlayerLook
    /// desactive : on joue le demon) ; la camera revient alors en FPS le temps du blocage.
    /// </summary>
    [DefaultExecutionOrder(200)] // apres PlayerLook et PlayerMotor (pivot deja place)
    [DisallowMultipleComponent]
    public class SurvivorViewMode : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private string toggleBinding = "<Keyboard>/f4";
        [SerializeField] private bool startInThirdPerson = false;

        [Header("References")]
        [Tooltip("Corps visible du survivant (enfant du joueur). Vide = enfant 'SurvivorBody', sinon capsule provisoire.")]
        [SerializeField] private GameObject body;

        [Header("Camera TPS")]
        [SerializeField, Min(0.5f)] private float distance = 2.3f;
        [Tooltip("Decalage par-dessus l'epaule droite (m).")]
        [SerializeField] private float shoulderOffset = 0.4f;
        [Tooltip("Hauteur ajoutee au-dessus des yeux (m).")]
        [SerializeField] private float heightOffset = 0.15f;
        [Tooltip("Rayon de la sphere qui empeche la camera de traverser le decor.")]
        [SerializeField, Min(0f)] private float collisionRadius = 0.2f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [Tooltip("Vitesse a laquelle la camera recule apres un obstacle.")]
        [SerializeField, Min(0.1f)] private float returnSpeed = 6f;

        private PlayerLook _look;
        private InputReader _input;
        private Transform _camera;
        private Transform _pivot;
        private Vector3 _fpsLocalPosition;
        private Quaternion _fpsLocalRotation;
        private InputAction _toggle;
        private bool _pending;
        private bool _wantThirdPerson;
        private bool _applied;
        private float _currentDistance;

        /// <summary>La vue TPS est reellement active cette frame.</summary>
        public bool IsThirdPerson { get { return _applied; } }

        /// <summary>
        /// Distance a sauter devant la camera pour que les raycasts de visee partent
        /// a hauteur des yeux du personnage et non derriere lui.
        /// </summary>
        public float AimRayOffset
        {
            get
            {
                if (!_applied || _camera == null || _pivot == null) return 0f;
                return Mathf.Max(0f, Vector3.Dot(_pivot.position - _camera.position, _camera.forward));
            }
        }

        private void Awake()
        {
            _look = GetComponent<PlayerLook>();
            _input = GetComponent<InputReader>();

            PlayerCharacter character = GetComponent<PlayerCharacter>();
            Camera cam = character != null ? character.Camera : GetComponentInChildren<Camera>(true);

            if (cam != null)
            {
                _camera = cam.transform;
                _pivot = _camera.parent != null ? _camera.parent : _camera;
                _fpsLocalPosition = _camera.localPosition;
                _fpsLocalRotation = _camera.localRotation;
            }

            if (body == null)
            {
                Transform found = transform.Find("SurvivorBody");
                if (found != null) body = found.gameObject;
            }

            _wantThirdPerson = startInThirdPerson;
            _currentDistance = distance;
        }

        private void OnEnable()
        {
            _toggle = new InputAction("SurvivorView", InputActionType.Button, toggleBinding);
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

            Apply(false, true);
        }

        public void SetThirdPerson(bool thirdPerson)
        {
            _wantThirdPerson = thirdPerson;
        }

        private void LateUpdate()
        {
            bool inputAllowed = _input == null || _input.InputEnabled;
            bool controllable = _look == null || _look.enabled;

            if (_pending && inputAllowed && controllable)
            {
                _wantThirdPerson = !_wantThirdPerson;
            }

            _pending = false;

            // Survivant fige (on joue le demon) : retour FPS, mais le corps reste
            // gere par qui l'a fige (PlayableCharacterSwitcher l'affiche deja).
            bool active = _wantThirdPerson && controllable;
            Apply(active, controllable);

            if (active)
            {
                PlaceCamera();
            }
        }

        private void Apply(bool thirdPerson, bool manageBody)
        {
            if (thirdPerson == _applied || _camera == null)
            {
                return;
            }

            _applied = thirdPerson;

            if (thirdPerson)
            {
                _currentDistance = distance;
                EnsureBody();
            }
            else
            {
                _camera.localPosition = _fpsLocalPosition;
                _camera.localRotation = _fpsLocalRotation;
            }

            if (manageBody && body != null)
            {
                body.SetActive(thirdPerson);
            }
        }

        private void PlaceCamera()
        {
            Quaternion rotation = _pivot.rotation;
            Vector3 origin = _pivot.position + Vector3.up * heightOffset;
            Vector3 desired = origin + rotation * new Vector3(shoulderOffset, 0f, -distance);

            Vector3 dir = desired - origin;
            float length = dir.magnitude;
            float allowed = length;

            if (length > 0.001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(origin, collisionRadius, dir / length, length, collisionMask, QueryTriggerInteraction.Ignore);

                foreach (RaycastHit h in hits)
                {
                    if (h.collider.transform.IsChildOf(transform) || h.distance <= 0f) continue;
                    allowed = Mathf.Min(allowed, h.distance);
                }
            }

            // Un obstacle rapproche aussitot ; la camera recule ensuite en douceur.
            float ratio = length > 0.001f ? allowed / length : 1f;
            float target = distance * ratio;
            _currentDistance = target < _currentDistance
                ? target
                : Mathf.MoveTowards(_currentDistance, target, returnSpeed * Time.deltaTime);

            float t = distance > 0.001f ? _currentDistance / distance : 1f;
            Vector3 position = origin + dir * t;

            _camera.position = position;
            _camera.localRotation = _fpsLocalRotation;
        }

        /// <summary>Pas de corps dans la scene : une capsule provisoire pour voir ou l'on est.</summary>
        private void EnsureBody()
        {
            if (body != null)
            {
                return;
            }

            CharacterController controller = GetComponent<CharacterController>();
            float height = controller != null ? controller.height : 1.8f;
            float radius = controller != null ? controller.radius : 0.35f;

            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "SurvivorBody_Placeholder";
            Destroy(capsule.GetComponent<Collider>());
            capsule.transform.SetParent(transform, false);
            capsule.transform.localPosition = Vector3.up * (height * 0.5f);
            capsule.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            capsule.SetActive(false);
            body = capsule;
        }
    }
}
