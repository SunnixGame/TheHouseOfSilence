using HouseOfSilence.Core;
using HouseOfSilence.Objectives;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.Events;

namespace HouseOfSilence.Doors
{
    /// <summary>
    /// Porte que rien dans l'inventaire n'ouvre : elle cede uniquement quand
    /// la partie l'autorise (objectif accompli, electricite retablie, rituel
    /// termine, sortie debloquee).
    ///
    /// Volontairement passive : elle expose RequiredObjectiveId et attend
    /// qu'on l'appelle. C'est l'ObjectiveManager (Phase 6) qui viendra
    /// declencher UnlockByObjective() ; en attendant, un SimpleInteractable ou
    /// n'importe quel UnityEvent peut le faire.
    ///
    /// C'est aussi la base de la porte de sortie finale (Phase 30).
    /// </summary>
    public class ObjectiveDoor : DoorBase
    {
        [Header("Condition")]
        [Tooltip("Identifiant de l'objectif qui deverrouille cette porte. Purement informatif tant que la Phase 6 n'est pas la.")]
        [SerializeField] private string requiredObjectiveId = "";

        [Tooltip("Ouvre la porte automatiquement au moment du deverrouillage.")]
        [SerializeField] private bool openOnUnlock = false;

        [Header("Textes")]
        [Tooltip("Message quand la porte refuse de s'ouvrir. Doit orienter le joueur, pas le bloquer.")]
        [SerializeField] private string blockedMessage = "Quelque chose la retient";

        [Header("Evenements")]
        [SerializeField] private UnityEvent onObjectiveUnlocked;

        /// <summary>Identifiant de l'objectif attendu.</summary>
        public string RequiredObjectiveId { get { return requiredObjectiveId; } }

        protected override void Awake()
        {
            base.Awake();

            // Une porte d'objectif demarre toujours verrouillee.
            if (!IsLocked)
            {
                Lock();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
        }

        /// <summary>
        /// La porte se deverrouille d'elle-meme quand SON objectif est termine.
        /// Aucun cablage n'est necessaire : il suffit que Required Objective Id
        /// corresponde a l'Objective Id de l'asset.
        /// </summary>
        private void OnObjectiveCompleted(ObjectiveCompletedEvent evt)
        {
            if (string.IsNullOrEmpty(requiredObjectiveId))
            {
                return;
            }

            if (evt.ObjectiveId != requiredObjectiveId)
            {
                return;
            }

            UnlockByObjective();
        }

        /// <summary>
        /// Deverrouille la porte. A appeler depuis un objectif, un evenement
        /// ou un UnityEvent dans l'Inspector.
        /// </summary>
        public void UnlockByObjective()
        {
            if (!IsLocked)
            {
                return;
            }

            Unlock();

            Debug.Log("[Porte] '" + name + "' deverrouillee par la progression du jeu.", this);

            if (onObjectiveUnlocked != null)
            {
                onObjectiveUnlocked.Invoke();
            }

            if (openOnUnlock)
            {
                Open(DoorActor.Script);
            }
        }

        /// <summary>Reverrouille la porte (piege, evenement horrifique).</summary>
        public void RelockByObjective()
        {
            Close(DoorActor.Script);
            Lock();
        }

        protected override string GetLockedPrompt(PlayerCharacter player)
        {
            return string.IsNullOrEmpty(blockedMessage) ? base.GetLockedPrompt(player) : blockedMessage;
        }
    }
}
