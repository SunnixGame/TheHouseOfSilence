using HouseOfSilence.Items;
using UnityEngine;

namespace HouseOfSilence.Objectives
{
    /// <summary>Comment un objectif se termine.</summary>
    public enum ObjectiveCompletionMode
    {
        /// <summary>Termine explicitement : ObjectiveTrigger, UnityEvent, script.</summary>
        Manual = 0,

        /// <summary>Termine automatiquement quand le joueur possede l'objet demande.</summary>
        CollectItem = 1,

        /// <summary>Termine quand un joueur entre dans une zone (ObjectiveTrigger en mode zone).</summary>
        ReachZone = 2
    }

    /// <summary>
    /// Definition d'un objectif. Donnee pure : aucune logique, aucune reference
    /// de scene. Le meme asset peut servir dans plusieurs niveaux.
    ///
    /// Creation : clic droit > Create > House of Silence > Objective Data
    /// Ranger dans Assets/_Game/ScriptableObjects/Objectives/.
    /// </summary>
    [CreateAssetMenu(fileName = "Objective_New", menuName = "House of Silence/Objective Data", order = 1)]
    public class ObjectiveData : ScriptableObject
    {
        [Header("Identite")]
        [Tooltip("Identifiant technique, unique et stable. Utilise par les triggers, les portes et la sauvegarde.")]
        [SerializeField] private string objectiveId = "objective_new";

        [Tooltip("Texte affiche dans le HUD.")]
        [SerializeField] private string title = "Nouvel objectif";

        [TextArea(2, 4)]
        [Tooltip("Precision facultative affichee sous le titre.")]
        [SerializeField] private string description = "";

        [TextArea(2, 3)]
        [Tooltip("Indice affiche apres un certain temps (difficulte Easy / Normal).")]
        [SerializeField] private string hint = "";

        [Header("Achevement")]
        [SerializeField] private ObjectiveCompletionMode completionMode = ObjectiveCompletionMode.Manual;

        [Tooltip("Mode CollectItem : objet a posseder.")]
        [SerializeField] private ItemData requiredItem;

        [Tooltip("Mode CollectItem : quantite requise.")]
        [SerializeField, Min(1)] private int requiredCount = 1;

        [Header("Regles")]
        [Tooltip("Un objectif optionnel n'empeche pas la progression s'il echoue.")]
        [SerializeField] private bool optional = false;

        [Tooltip("Delai avant l'affichage de l'indice, en secondes. 0 = jamais.")]
        [SerializeField, Min(0f)] private float hintDelay = 90f;

        // ------------------------------------------------------------------

        public string ObjectiveId { get { return objectiveId; } }
        public string Title { get { return title; } }
        public string Description { get { return description; } }
        public string Hint { get { return hint; } }
        public ObjectiveCompletionMode CompletionMode { get { return completionMode; } }
        public ItemData RequiredItem { get { return requiredItem; } }
        public int RequiredCount { get { return Mathf.Max(1, requiredCount); } }
        public bool Optional { get { return optional; } }
        public float HintDelay { get { return hintDelay; } }
        public bool HasHint { get { return !string.IsNullOrEmpty(hint) && hintDelay > 0f; } }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(objectiveId))
            {
                objectiveId = name.ToLowerInvariant();
            }

            if (completionMode != ObjectiveCompletionMode.CollectItem)
            {
                requiredCount = 1;
            }
        }
#endif
    }
}
