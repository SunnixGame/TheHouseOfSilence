using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Camera spectateur propre : un objet a part (pas la camera du joueur mort), donc sans
    /// l'effet VHS ni les lumieres de jumpscare qui y sont accrochees. Elle reprend les
    /// reglages de rendu de la camera du joueur (champ de vision, plans, post-traitement) :
    /// la nuit, le brouillard et la meteo, globaux, restent identiques.
    ///
    /// Tourne autour de la cible a la troisieme personne : souris pour tourner, molette pour
    /// le zoom ; le decor rapproche la camera au lieu d'etre traverse.
    /// Creee et detruite par SpectatorController (camera reinitialisee a chaque mort).
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public class SpectatorCameraController : MonoBehaviour
    {
        [SerializeField] private float pivotHeight = 1.3f;
        [SerializeField] private float distance = 3.5f;
        [SerializeField] private Vector2 distanceRange = new Vector2(1.5f, 10f);
        [SerializeField] private Vector2 pitchRange = new Vector2(-20f, 70f);
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.12f;
        [SerializeField, Min(0f)] private float zoomSensitivity = 0.004f;
        [SerializeField, Min(0f)] private float collisionRadius = 0.2f;

        private Camera _camera;
        private Transform _target;
        private float _yaw;
        private float _pitch = 15f;
        private InputAction _look;
        private InputAction _zoom;

        public Camera Camera { get { return _camera; } }

        /// <summary>Si faux (menu, pause), la souris ne tourne pas la camera.</summary>
        public bool InputAllowed { get; set; } = true;

        /// <summary>Cree la camera spectateur a partir des reglages de 'source'.</summary>
        public static SpectatorCameraController Create(Camera source)
        {
            GameObject go = new GameObject("SpectatorCamera");
            Camera cam = go.AddComponent<Camera>();

            if (source != null)
            {
                cam.CopyFrom(source); // reglages et position de depart
                cam.targetTexture = null;

                UniversalAdditionalCameraData from = source.GetComponent<UniversalAdditionalCameraData>();
                if (from != null)
                {
                    UniversalAdditionalCameraData to = go.GetComponent<UniversalAdditionalCameraData>();
                    if (to == null) to = go.AddComponent<UniversalAdditionalCameraData>();
                    to.renderPostProcessing = from.renderPostProcessing;
                    to.antialiasing = from.antialiasing;
                    to.renderShadows = from.renderShadows;
                }
            }

            cam.enabled = true;
            go.AddComponent<AudioListener>();

            SpectatorCameraController controller = go.AddComponent<SpectatorCameraController>();
            controller._camera = cam;
            return controller;
        }

        private void OnEnable()
        {
            _look = new InputAction("SpectatorLook", InputActionType.Value, "<Mouse>/delta");
            _zoom = new InputAction("SpectatorZoom", InputActionType.Value, "<Mouse>/scroll/y");
            _look.Enable();
            _zoom.Enable();
        }

        private void OnDisable()
        {
            foreach (InputAction a in new[] { _look, _zoom })
            {
                if (a == null) continue;
                a.Disable();
                a.Dispose();
            }

            _look = _zoom = null;
        }

        /// <summary>Change de cible ; la camera se replace derriere elle.</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            if (target != null) _yaw = target.eulerAngles.y;
        }

        private void Update()
        {
            if (!InputAllowed || _look == null) return;

            Vector2 look = _look.ReadValue<Vector2>() * mouseSensitivity;
            _yaw += look.x;
            _pitch = Mathf.Clamp(_pitch - look.y, pitchRange.x, pitchRange.y);
            distance = Mathf.Clamp(distance - _zoom.ReadValue<float>() * zoomSensitivity, distanceRange.x, distanceRange.y);
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = _target.position + Vector3.up * pivotHeight;
            Vector3 dir = rotation * Vector3.back;
            float allowed = distance;

            RaycastHit[] hits = Physics.SphereCastAll(pivot, collisionRadius, dir, distance, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.distance <= 0f || h.collider.transform.IsChildOf(_target)) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue; // cadavres
                allowed = Mathf.Min(allowed, h.distance);
            }

            transform.SetPositionAndRotation(pivot + dir * Mathf.Max(0.3f, allowed), rotation);
        }
    }
}
