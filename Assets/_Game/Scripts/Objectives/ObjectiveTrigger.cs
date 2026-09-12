using HouseOfSilence.Core;
using HouseOfSilence.Interaction;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.Events;

namespace HouseOfSilence.Objectives
{
    /// <summary>Ce qui declenche le trigger.</summary>
    public enum ObjectiveTriggerMode
    {
        /// <summary>
        /// Deduit tout seul : un interactif sur l'objet (ou lie) => a l'interaction ;
        /// sinon un collider Is Trigger => a l'entree du joueur ; sinon manuel.
        /// </summary>
        Auto = 0,

        /// <summary>Le joueur entre dans le collider (Is Trigger requis).</summary>
        PlayerEntersZone = 1,

        /// <summary>Le joueur interagit avec l'objet interactif (aucun cablage a faire).</summary>
        InteractWithObject = 2,

        /// <summary>Uniquement via Trigger() appele par un UnityEvent ou un script.</summary>
        ManualOnly = 3
    }

    /// <summary>
    /// Termine un objectif quand une condition de scene est remplie.
    ///
    /// AUCUN CABLAGE MANUEL N'EST NECESSAIRE :
    /// - pose sur une porte, un SimpleInteractable, un objet a ramasser : il
    ///   ecoute lui-meme l'interaction ;
    /// - pose sur un objet vide avec un collider Is Trigger : c'est une zone ;
    /// - pour viser un interactif situe ailleurs, glisser-le dans
    ///   "Linked Interactable".
    ///
    /// Chaque refus est explique dans la console : jamais d'echec silencieux.
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectiveTrigger : MonoBehaviour
    {
        [Header("Objectif")]
        [Tooltip("Asset de l'objectif a terminer (recommande).")]
        [SerializeField] private ObjectiveData objective;

        [Tooltip("Ou bien son identifiant, si l'asset n'est pas reference.")]
        [SerializeField] private string objectiveId = "";

        [Header("Declenchement")]
        [SerializeField] private ObjectiveTriggerMode mode = ObjectiveTriggerMode.Auto;

        [Tooltip("Interactif a surveiller. Vide = celui present sur ce GameObject.")]
        [SerializeField] private InteractableBase linkedInteractable;

        [Header("Regles")]
        [Tooltip("Ne fonctionne qu'une seule fois.")]
        [SerializeField] private bool singleUse = true;

        [Tooltip("Ne se declenche que si l'objectif vise est actuellement actif. Decoche pour autoriser une completion en avance (elle sera prise en compte quand l'objectif arrivera).")]
        [SerializeField] private bool requireObjectiveActive = true;

        [Tooltip("Identifiant d'un objectif qui doit deja etre termine. Vide = aucune condition.")]
        [SerializeField] private string requiredCompletedObjectiveId = "";

        [Header("Evenements")]
        [SerializeField] private UnityEvent onObjectiveTriggered;

        private ObjectiveTriggerMode _resolvedMode;
        private InteractableBase _interactable;
        private bool _consumed;
        private bool _subscribed;

        /// <summary>Identifiant reellement utilise.</summary>
        public string TargetObjectiveId
        {
            get { return objective != null ? objective.ObjectiveId : objectiveId; }
        }

        /// <summary>Mode effectif apres resolution de Auto.</summary>
        public ObjectiveTriggerMode ResolvedMode { get { return _resolvedMode; } }

        // ------------------------------------------------------------------

        private void Awake()
        {
            ResolveMode();
        }

        private void OnEnable()
        {
            if (_resolvedMode == ObjectiveTriggerMode.InteractWithObject && !_subscribed)
            {
                EventBus.Subscribe<InteractionPerformedEvent>(OnInteractionPerformed);
                _subscribed = true;
            }
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                EventBus.Unsubscribe<InteractionPerformedEvent>(OnInteractionPerformed);
                _subscribed = false;
            }
        }

        private void ResolveMode()
        {
            _interactable = linkedInteractable != null ? linkedInteractable : GetComponent<InteractableBase>();

            Collider collider = GetComponent<Collider>();
            bool hasTriggerCollider = collider != null && collider.isTrigger;

            switch (mode)
            {
                case ObjectiveTriggerMode.PlayerEntersZone:
                    _resolvedMode = ObjectiveTriggerMode.PlayerEntersZone;

                    if (!hasTriggerCollider)
                    {
                        Debug.LogWarning("[Objectifs] '" + name + "' est en mode zone mais n'a pas de collider en Is Trigger : il ne se declenchera jamais.", this);
                    }

                    break;

                case ObjectiveTriggerMode.InteractWithObject:
                    _resolvedMode = ObjectiveTriggerMode.InteractWithObject;

                    if (_interactable == null)
                    {
                        Debug.LogWarning("[Objectifs] '" + name + "' est en mode interaction mais aucun interactif n'est present ni lie : il ne se declenchera jamais.", this);
                    }

                    break;

                case ObjectiveTriggerMode.ManualOnly:
                    _resolvedMode = ObjectiveTriggerMode.ManualOnly;
                    break;

                default:
                    if (_interactable != null)
                    {
                        _resolvedMode = ObjectiveTriggerMode.InteractWithObject;
                    }
                    else if (hasTriggerCollider)
                    {
                        _resolvedMode = ObjectiveTriggerMode.PlayerEntersZone;
                    }
                    else
                    {
                        _resolvedMode = ObjectiveTriggerMode.ManualOnly;
                        Debug.LogWarning("[Objectifs] '" + name + "' : ni interactif, ni collider Is Trigger. Seul un appel a Trigger() pourra le declencher.", this);
                    }

                    break;
            }
        }

        // ------------------------------------------------------------------
        // Sources de declenchement
        // ------------------------------------------------------------------

        private void OnTriggerEnter(Collider other)
        {
            if (_resolvedMode != ObjectiveTriggerMode.PlayerEntersZone || other == null)
            {
                return;
            }

            if (other.GetComponentInParent<PlayerCharacter>() == null)
            {
                return;
            }

            Trigger();
        }

        private void OnInteractionPerformed(InteractionPerformedEvent evt)
        {
            if (_interactable == null || !ReferenceEquals(evt.Target, _interactable))
            {
                return;
            }

            Trigger();
        }

        /// <summary>
        /// Termine l'objectif. Publique pour pouvoir etre appelee depuis un
        /// UnityEvent ou un script, quel que soit le mode.
        /// </summary>
        public void Trigger()
        {
            if (_consumed)
            {
                return;
            }

            string id = TargetObjectiveId;

            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning("[Objectifs] '" + name + "' : aucun objectif assigne (champ Objective vide).", this);
                return;
            }

            ObjectiveManager manager = ObjectiveManager.Instance;

            if (manager == null)
            {
                Debug.LogWarning("[Objectifs] '" + name + "' : aucun ObjectiveManager dans la scene.", this);
                return;
            }

            if (!string.IsNullOrEmpty(requiredCompletedObjectiveId) && !manager.IsCompleted(requiredCompletedObjectiveId))
            {
                Debug.Log("[Objectifs] '" + name + "' refuse : l'objectif prealable '" + requiredCompletedObjectiveId + "' n'est pas termine.", this);
                return;
            }

            ObjectiveState state = manager.GetState(id);

            if (state == ObjectiveState.Completed)
            {
                Debug.Log("[Objectifs] '" + name + "' : l'objectif '" + id + "' est deja termine.", this);

                if (singleUse)
                {
                    _consumed = true;
                }

                return;
            }

            if (requireObjectiveActive && state != ObjectiveState.Active)
            {
                ObjectiveData current = manager.CurrentObjective;
                string currentId = current != null ? current.ObjectiveId : "aucun";

                Debug.Log("[Objectifs] '" + name + "' refuse : '" + id + "' n'est pas l'objectif actif (actuel : '" + currentId
                    + "'). Decoche 'Require Objective Active' sur ce trigger pour autoriser une completion en avance.", this);
                return;
            }

            if (!manager.CompleteObjective(id))
            {
                return;
            }

            if (singleUse)
            {
                _consumed = true;
            }

            if (onObjectiveTriggered != null)
            {
                onObjectiveTriggered.Invoke();
            }
        }

        /// <summary>Reactive le trigger (nouvelle partie).</summary>
        public void ResetTrigger()
        {
            _consumed = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Collider collider = GetComponent<Collider>();

            if (collider == null || !collider.isTrigger)
            {
                return;
            }

            Gizmos.color = new Color(0.4f, 0.9f, 0.6f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;

            BoxCollider box = collider as BoxCollider;

            if (box != null)
            {
                Gizmos.DrawCube(box.center, box.size);
            }
        }
#endif
    }
}
