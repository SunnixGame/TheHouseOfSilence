using HouseOfSilence.Core;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Interaction
{
    /// <summary>
    /// Detecte l'objet interactif vise par le joueur et declenche l'interaction.
    ///
    /// Detection en deux temps :
    /// 1. un Raycast precis depuis la camera ;
    /// 2. si rien n'est touche, un SphereCast legerement plus large, qui pardonne
    ///    les visees imprecises sur les petits objets (cle, fusible, poignee).
    ///
    /// Le premier collider touche fait autorite : un objet interactif derriere
    /// un mur n'est jamais selectionne, la ligne de vue est donc garantie.
    ///
    /// A poser sur le GameObject du joueur.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInteractor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private InputReader input;

        [Tooltip("Origine des raycasts. Laisser vide pour utiliser la camera du joueur.")]
        [SerializeField] private Transform rayOrigin;

        [Header("Detection")]
        [Tooltip("Portee d'interaction, en metres.")]
        [SerializeField, Min(0.5f)] private float interactionDistance = 3f;

        [Tooltip("Rayon du SphereCast d'assistance a la visee.")]
        [SerializeField, Range(0f, 0.5f)] private float assistRadius = 0.14f;

        [Tooltip("Couches testees. Laisser sur Everything tant qu'aucun layer dedie n'existe.")]
        [SerializeField] private LayerMask interactionMask = ~0;

        [Tooltip("Frequence de detection, en secondes. 0 = a chaque frame.")]
        [SerializeField, Range(0f, 0.2f)] private float detectionInterval = 0.05f;

        [Header("Debug")]
        [SerializeField] private bool drawDebugRay = false;

        private readonly RaycastHit[] _hits = new RaycastHit[12];

        private SurvivorViewMode _viewMode;
        private IInteractable _currentTarget;
        private string _cachedPrompt = string.Empty;
        private string _cachedTitle = string.Empty;
        private bool _cachedCanInteract;
        private float _detectionTimer;
        private float _holdTimer;
        private bool _isHolding;
        private bool _holdConsumed;

        /// <summary>Cible actuellement visee, ou null.</summary>
        public IInteractable CurrentTarget { get { return _currentTarget; } }

        /// <summary>Progression du maintien en cours, de 0 a 1.</summary>
        public float HoldProgress
        {
            get
            {
                if (!_isHolding || _currentTarget == null || _currentTarget.HoldDuration <= 0f)
                {
                    return 0f;
                }

                return Mathf.Clamp01(_holdTimer / _currentTarget.HoldDuration);
            }
        }

        /// <summary>Coupe totalement l'interaction (mort, cachette, cinematique).</summary>
        public bool InteractionLocked { get; set; }

        // ------------------------------------------------------------------

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponent<PlayerCharacter>();
            }

            if (input == null)
            {
                input = GetComponent<InputReader>();
            }

            if (rayOrigin == null && player != null && player.Camera != null)
            {
                rayOrigin = player.Camera.transform;
            }

            if (rayOrigin == null)
            {
                Camera fallback = GetComponentInChildren<Camera>(true);

                if (fallback != null)
                {
                    rayOrigin = fallback.transform;
                }
            }

            if (rayOrigin == null)
            {
                Debug.LogError("[PlayerInteractor] Aucune origine de raycast : assigne 'Ray Origin' ou ajoute une camera au joueur.", this);
            }
        }

        private void OnDisable()
        {
            CancelHold();
            SetTarget(null);
        }

        private void Update()
        {
            if (!IsActive())
            {
                if (_currentTarget != null)
                {
                    CancelHold();
                    SetTarget(null);
                }

                return;
            }

            UpdateDetection();
            UpdateInteractionInput();
        }

        private bool IsActive()
        {
            if (InteractionLocked || rayOrigin == null)
            {
                return false;
            }

            if (input == null || !input.InputEnabled)
            {
                return false;
            }

            if (player != null && !player.IsAlive)
            {
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Detection
        // ------------------------------------------------------------------

        private void UpdateDetection()
        {
            if (detectionInterval > 0f)
            {
                _detectionTimer -= Time.deltaTime;

                if (_detectionTimer > 0f)
                {
                    return;
                }

                _detectionTimer = detectionInterval;
            }

            IInteractable found = FindTarget();

            if (found != _currentTarget)
            {
                CancelHold();
                SetTarget(found);
                return;
            }

            if (found == null)
            {
                return;
            }

            if (!found.IsFocusable(player))
            {
                // La cible s'est invalidee (objet ramasse par un autre joueur, porte consommee).
                CancelHold();
                SetTarget(null);
                return;
            }

            // La cible n'a pas change, mais son etat peut avoir evolue sans que
            // le joueur bouge : slot d'inventaire different, porte deverrouillee
            // a distance, objectif accompli... On ne republie que si le texte
            // ou la disponibilite ont reellement change.
            string prompt = found.GetPrompt(player);
            string title = found.GetTitle(player);
            bool canInteract = found.CanInteract(player);

            if (prompt != _cachedPrompt || title != _cachedTitle || canInteract != _cachedCanInteract)
            {
                PublishTargetChanged();
            }
        }

        private IInteractable FindTarget()
        {
            Vector3 origin = rayOrigin.position;
            Vector3 direction = rayOrigin.forward;

            // Vue TPS : la camera est derriere le survivant, on vise depuis ses yeux.
            if (_viewMode == null) _viewMode = GetComponent<SurvivorViewMode>();
            if (_viewMode != null) origin += direction * _viewMode.AimRayOffset;

            if (drawDebugRay)
            {
                Debug.DrawRay(origin, direction * interactionDistance, Color.cyan);
            }

            // 1. Visee precise.
            RaycastHit hit;

            if (Physics.Raycast(origin, direction, out hit, interactionDistance, interactionMask, QueryTriggerInteraction.Collide))
            {
                IInteractable precise = ExtractInteractable(hit.collider);

                if (precise != null)
                {
                    return precise;
                }
            }

            if (assistRadius <= 0f)
            {
                return null;
            }

            // 2. Assistance a la visee : on garde le candidat valide le plus proche.
            int count = Physics.SphereCastNonAlloc(
                origin,
                assistRadius,
                direction,
                _hits,
                interactionDistance,
                interactionMask,
                QueryTriggerInteraction.Collide);

            IInteractable best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider collider = _hits[i].collider;

                if (collider == null)
                {
                    continue;
                }

                IInteractable candidate = ExtractInteractable(collider);

                if (candidate == null)
                {
                    continue;
                }

                // On verifie que rien n'obstrue reellement la vue vers le candidat.
                Vector3 toTarget = collider.bounds.center - origin;
                float distance = toTarget.magnitude;

                if (distance > interactionDistance + assistRadius)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Remonte la hierarchie pour trouver l'interactif : le collider peut etre
        /// pose sur un enfant (poignee de porte, sous-mesh).
        /// </summary>
        private IInteractable ExtractInteractable(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            // On ignore le joueur lui-meme.
            if (collider.transform == transform || collider.transform.IsChildOf(transform))
            {
                return null;
            }

            IInteractable interactable = collider.GetComponentInParent<IInteractable>();

            if (interactable == null || !interactable.IsFocusable(player))
            {
                return null;
            }

            return interactable;
        }

        private void SetTarget(IInteractable target)
        {
            if (_currentTarget == target)
            {
                return;
            }

            if (_currentTarget != null)
            {
                _currentTarget.OnFocusExit(player);
            }

            _currentTarget = target;

            if (_currentTarget != null)
            {
                _currentTarget.OnFocusEnter(player);
            }

            PublishTargetChanged();
        }

        private void PublishTargetChanged()
        {
            string prompt = string.Empty;
            string title = string.Empty;
            bool canInteract = false;
            float hold = 0f;

            if (_currentTarget != null)
            {
                prompt = _currentTarget.GetPrompt(player);
                title = _currentTarget.GetTitle(player);
                canInteract = _currentTarget.CanInteract(player);
                hold = _currentTarget.HoldDuration;
            }

            _cachedPrompt = prompt;
            _cachedTitle = title;
            _cachedCanInteract = canInteract;

            EventBus.Publish(new InteractionTargetChangedEvent(player, _currentTarget, title, prompt, canInteract, hold));
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------

        private void UpdateInteractionInput()
        {
            if (_currentTarget == null)
            {
                if (_isHolding)
                {
                    CancelHold();
                }

                return;
            }

            bool instant = _currentTarget.HoldDuration <= 0f;

            if (instant)
            {
                if (input.InteractPressed)
                {
                    _currentTarget.Interact(player);

                    // Le prompt peut avoir change (porte ouverte -> "Fermer").
                    PublishTargetChanged();
                }

                return;
            }

            // Interaction a maintien.
            if (input.InteractHeld && !_holdConsumed)
            {
                if (!_currentTarget.CanInteract(player))
                {
                    CancelHold();
                    return;
                }

                if (!_isHolding)
                {
                    _isHolding = true;
                    _holdTimer = 0f;
                }

                _holdTimer += Time.deltaTime;

                float progress = Mathf.Clamp01(_holdTimer / _currentTarget.HoldDuration);

                _currentTarget.OnHoldProgress(player, progress);
                EventBus.Publish(new InteractionHoldProgressEvent(player, progress, true));

                if (progress >= 1f)
                {
                    _isHolding = false;
                    _holdConsumed = true;
                    _holdTimer = 0f;

                    EventBus.Publish(new InteractionHoldProgressEvent(player, 0f, false));

                    _currentTarget.Interact(player);
                    PublishTargetChanged();
                }

                return;
            }

            if (!input.InteractHeld)
            {
                if (_holdConsumed)
                {
                    _holdConsumed = false;
                }

                if (_isHolding)
                {
                    CancelHold();
                }
            }
        }

        private void CancelHold()
        {
            _holdConsumed = false;

            if (!_isHolding)
            {
                return;
            }

            _isHolding = false;
            _holdTimer = 0f;

            if (_currentTarget != null)
            {
                _currentTarget.OnHoldCancelled(player);
            }

            EventBus.Publish(new InteractionHoldProgressEvent(player, 0f, false));
        }

        /// <summary>
        /// Force la reevaluation du prompt : a appeler quand l'etat d'un objet
        /// change alors que le joueur le vise deja (deverrouillage a distance,
        /// objet obtenu par un coequipier...).
        /// </summary>
        public void RefreshPrompt()
        {
            if (_currentTarget != null)
            {
                PublishTargetChanged();
            }
        }
    }
}
