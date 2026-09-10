using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.Events;

namespace HouseOfSilence.Objectives
{
    /// <summary>
    /// Termine un objectif quand une condition de scene est remplie.
    ///
    /// Trois usages, tous configurables sans code :
    /// - zone : un collider en Is Trigger, le joueur entre dedans ;
    /// - manuel : appeler Trigger() depuis un UnityEvent (le "On Interacted"
    ///   d'un SimpleInteractable, d'une porte, d'un generateur...) ;
    /// - conditionnel : n'agit que si un autre objectif est deja termine.
    ///
    /// A poser sur un GameObject vide avec un BoxCollider en Is Trigger,
    /// ou directement sur l'objet interactif concerne.
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectiveTrigger : MonoBehaviour
    {
        [Header("Objectif")]
        [Tooltip("Identifiant de l'objectif a terminer. Doit correspondre a l'Objective Id d'un asset.")]
        [SerializeField] private string objectiveId = "";

        [Tooltip("Referencer l'asset a la place de l'identifiant (prioritaire, et plus sur).")]
        [SerializeField] private ObjectiveData objective;

        [Header("Declenchement")]
        [Tooltip("Se declenche quand un joueur entre dans le collider (Is Trigger requis).")]
        [SerializeField] private bool triggerOnPlayerEnter = true;

        [Tooltip("Ne fonctionne qu'une seule fois.")]
        [SerializeField] private bool singleUse = true;

        [Tooltip("Ne se declenche que si l'objectif vise est actuellement actif.")]
        [SerializeField] private bool requireObjectiveActive = true;

        [Tooltip("Identifiant d'un objectif qui doit deja etre termine. Vide = aucune condition.")]
        [SerializeField] private string requiredCompletedObjectiveId = "";

        [Header("Evenements")]
        [SerializeField] private UnityEvent onObjectiveTriggered;

        private bool _consumed;

        /// <summary>Identifiant reellement utilise.</summary>
        public string TargetObjectiveId
        {
            get { return objective != null ? objective.ObjectiveId : objectiveId; }
        }

        private void Reset()
        {
            // Confort : un collider pose ici est presque toujours une zone.
            Collider collider = GetComponent<Collider>();

            if (collider != null)
            {
                collider.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!triggerOnPlayerEnter || other == null)
            {
                return;
            }

            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();

            if (player == null)
            {
                return;
            }

            Trigger();
        }

        /// <summary>
        /// Termine l'objectif. Publique pour etre cablee dans n'importe quel
        /// UnityEvent de l'Inspector.
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
                Debug.LogWarning("[Objectifs] ObjectiveTrigger sur '" + name + "' sans objectif assigne.", this);
                return;
            }

            ObjectiveManager manager = ObjectiveManager.Instance;

            if (manager == null)
            {
                Debug.LogWarning("[Objectifs] Aucun ObjectiveManager dans la scene.", this);
                return;
            }

            if (!string.IsNullOrEmpty(requiredCompletedObjectiveId) && !manager.IsCompleted(requiredCompletedObjectiveId))
            {
                return;
            }

            if (requireObjectiveActive && manager.GetState(id) != ObjectiveState.Active)
            {
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
