using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Core.Debugging;
using HouseOfSilence.Inventory;
using HouseOfSilence.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Objectives
{
    /// <summary>
    /// Deroule la chaine d'objectifs de la partie.
    ///
    /// La sequence se configure ENTIEREMENT dans l'Inspector : on glisse des
    /// assets ObjectiveData dans la liste, dans l'ordre voulu. Ajouter, retirer
    /// ou reordonner un objectif ne demande aucune modification de code.
    ///
    /// Le manager ne connait ni les portes, ni les zones, ni les objets : il
    /// publie des evenements, et ce sont les autres systemes qui reagissent.
    ///
    /// Autorite : en multijoueur (Phase 18), seul le host fera avancer la
    /// chaine ; les clients recevront les evenements repliques.
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectiveManager : MonoSingleton<ObjectiveManager>
    {
        [Header("Sequence")]
        [Tooltip("Objectifs dans l'ordre. Glisser les assets ObjectiveData ici.")]
        [SerializeField] private List<ObjectiveData> objectives = new List<ObjectiveData>();

        [Tooltip("Active le premier objectif des le demarrage de la partie.")]
        [SerializeField] private bool autoStartOnGameStart = true;

        [Header("Debug")]
        [Tooltip("Touche de debug pour terminer l'objectif courant.")]
        [SerializeField] private Key completeCurrentKey = Key.F3;

        [SerializeField] private bool registerDebugCommand = true;
        [SerializeField] private bool logToConsole = true;

        // ------------------------------------------------------------------

        private readonly Dictionary<string, ObjectiveState> _states = new Dictionary<string, ObjectiveState>(16);
        private int _currentIndex = -1;
        private float _hintTimer = -1f;
        private bool _hintShown;
        private bool _sequenceStarted;

        /// <summary>Le manager vit dans la scene de jeu.</summary>
        protected override bool IsPersistent { get { return false; } }

        /// <summary>Objectif actuellement actif, ou null.</summary>
        public ObjectiveData CurrentObjective
        {
            get
            {
                if (_currentIndex < 0 || _currentIndex >= objectives.Count)
                {
                    return null;
                }

                return objectives[_currentIndex];
            }
        }

        /// <summary>Index de l'objectif courant (0 = le premier).</summary>
        public int CurrentIndex { get { return _currentIndex; } }

        /// <summary>Nombre total d'objectifs de la chaine.</summary>
        public int TotalCount { get { return objectives.Count; } }

        /// <summary>Nombre d'objectifs termines.</summary>
        public int CompletedCount
        {
            get
            {
                int count = 0;

                foreach (KeyValuePair<string, ObjectiveState> pair in _states)
                {
                    if (pair.Value == ObjectiveState.Completed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Vrai quand toute la chaine est terminee.</summary>
        public bool IsSequenceComplete { get; private set; }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            RebuildStates();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<GameStartedEvent>(OnGameStarted);
            EventBus.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);

            if (registerDebugCommand)
            {
                DebugManager.Register(completeCurrentKey, "Terminer l'objectif courant", CompleteCurrent);
                DebugManager.RegisterInfo("Objectifs", BuildDebugInfo);
            }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<GameStartedEvent>(OnGameStarted);
            EventBus.Unsubscribe<ItemPickedUpEvent>(OnItemPickedUp);

            if (registerDebugCommand)
            {
                DebugManager.Unregister(completeCurrentKey);
                DebugManager.UnregisterInfo("Objectifs");
            }
        }

        private void Start()
        {
            // Si la partie a demarre avant que ce composant ne s'active
            // (lancement direct dans la scene de jeu), on rattrape ici.
            if (!autoStartOnGameStart || _sequenceStarted)
            {
                return;
            }

            if (GameManager.HasInstance && GameManager.Instance != null && GameManager.Instance.IsPlaying)
            {
                StartSequence();
            }
        }

        private void Update()
        {
            UpdateHint();
        }

        private void OnGameStarted(GameStartedEvent evt)
        {
            if (autoStartOnGameStart)
            {
                StartSequence();
            }
        }

        // ------------------------------------------------------------------
        // Deroulement
        // ------------------------------------------------------------------

        /// <summary>(Re)demarre la chaine depuis le premier objectif.</summary>
        public void StartSequence()
        {
            RebuildStates();

            _sequenceStarted = true;
            IsSequenceComplete = false;
            _currentIndex = -1;

            if (objectives.Count == 0)
            {
                Debug.LogWarning("[Objectifs] Aucun objectif dans la liste du ObjectiveManager.", this);
                return;
            }

            ActivateIndex(0);
        }

        private void RebuildStates()
        {
            _states.Clear();

            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveData objective = objectives[i];

                if (objective == null)
                {
                    Debug.LogWarning("[Objectifs] Entree vide a l'index " + i + " de la liste.", this);
                    continue;
                }

                if (_states.ContainsKey(objective.ObjectiveId))
                {
                    Debug.LogWarning("[Objectifs] Identifiant en double : '" + objective.ObjectiveId + "'. Chaque objectif doit avoir un Objective Id unique.", objective);
                    continue;
                }

                _states.Add(objective.ObjectiveId, ObjectiveState.Locked);
            }
        }

        private void ActivateIndex(int index)
        {
            if (index < 0 || index >= objectives.Count)
            {
                return;
            }

            ObjectiveData objective = objectives[index];

            if (objective == null)
            {
                // On saute proprement une entree vide plutot que de bloquer la partie.
                ActivateIndex(index + 1);
                return;
            }

            _currentIndex = index;

            // Objectif deja accompli en avance (trigger avec Require Objective
            // Active decoche) : on le traverse sans le redemander au joueur.
            ObjectiveState existing;

            if (_states.TryGetValue(objective.ObjectiveId, out existing) && existing == ObjectiveState.Completed)
            {
                if (logToConsole)
                {
                    Debug.Log("[Objectifs] " + (index + 1) + "/" + objectives.Count + " - " + objective.Title + " (deja accompli, passage direct)");
                }

                EventBus.Publish(new ObjectiveActivatedEvent(objective, index, objectives.Count));
                AdvanceToNext();
                return;
            }

            _states[objective.ObjectiveId] = ObjectiveState.Active;

            _hintShown = false;
            _hintTimer = objective.HasHint ? objective.HintDelay : -1f;

            if (logToConsole)
            {
                Debug.Log("[Objectifs] " + (index + 1) + "/" + objectives.Count + " - " + objective.Title);
            }

            EventBus.Publish(new ObjectiveActivatedEvent(objective, index, objectives.Count));

            // Un objectif deja rempli au moment ou il s'active (le joueur a
            // ramasse l'objet en avance) se termine immediatement.
            if (IsAlreadySatisfied(objective))
            {
                CompleteObjective(objective.ObjectiveId);
            }
        }

        /// <summary>Termine l'objectif portant cet identifiant.</summary>
        public bool CompleteObjective(string objectiveId)
        {
            if (string.IsNullOrEmpty(objectiveId))
            {
                return false;
            }

            ObjectiveState state;

            if (!_states.TryGetValue(objectiveId, out state))
            {
                Debug.LogWarning("[Objectifs] Identifiant inconnu : '" + objectiveId + "'. Verifie l'orthographe et la presence de l'asset dans la liste.", this);
                return false;
            }

            if (state == ObjectiveState.Completed)
            {
                return false;
            }

            ObjectiveData objective = FindById(objectiveId);
            _states[objectiveId] = ObjectiveState.Completed;

            if (logToConsole)
            {
                Debug.Log("[Objectifs] TERMINE : " + (objective != null ? objective.Title : objectiveId));
            }

            EventBus.Publish(new ObjectiveCompletedEvent(objective, objectiveId));

            // On avance seulement si c'etait bien l'objectif courant.
            if (objective != null && objective == CurrentObjective)
            {
                AdvanceToNext();
            }

            return true;
        }

        /// <summary>Termine l'objectif actuellement actif (touche de debug F3).</summary>
        public void CompleteCurrent()
        {
            ObjectiveData current = CurrentObjective;

            if (current == null)
            {
                Debug.Log("[Objectifs] Aucun objectif actif.");
                return;
            }

            CompleteObjective(current.ObjectiveId);
        }

        /// <summary>Marque un objectif comme rate.</summary>
        public bool FailObjective(string objectiveId)
        {
            ObjectiveState state;

            if (string.IsNullOrEmpty(objectiveId) || !_states.TryGetValue(objectiveId, out state))
            {
                return false;
            }

            if (state == ObjectiveState.Completed || state == ObjectiveState.Failed)
            {
                return false;
            }

            ObjectiveData objective = FindById(objectiveId);
            _states[objectiveId] = ObjectiveState.Failed;

            Debug.Log("[Objectifs] ECHOUE : " + (objective != null ? objective.Title : objectiveId));
            EventBus.Publish(new ObjectiveFailedEvent(objective, objectiveId));

            // Un objectif optionnel rate ne doit jamais bloquer la partie.
            if (objective != null && objective.Optional && objective == CurrentObjective)
            {
                AdvanceToNext();
            }

            return true;
        }

        private void AdvanceToNext()
        {
            int next = _currentIndex + 1;

            if (next < objectives.Count)
            {
                ActivateIndex(next);
                return;
            }

            _currentIndex = objectives.Count;
            _hintTimer = -1f;
            IsSequenceComplete = true;

            if (logToConsole)
            {
                Debug.Log("[Objectifs] Tous les objectifs sont termines.");
            }

            EventBus.Publish(new AllObjectivesCompletedEvent(CompletedCount));
        }

        // ------------------------------------------------------------------
        // Lecture
        // ------------------------------------------------------------------

        /// <summary>Etat d'un objectif. Locked si l'identifiant est inconnu.</summary>
        public ObjectiveState GetState(string objectiveId)
        {
            ObjectiveState state;
            return _states.TryGetValue(objectiveId, out state) ? state : ObjectiveState.Locked;
        }

        /// <summary>Vrai si cet objectif est termine.</summary>
        public bool IsCompleted(string objectiveId)
        {
            return GetState(objectiveId) == ObjectiveState.Completed;
        }

        /// <summary>Retrouve un asset d'objectif par son identifiant.</summary>
        public ObjectiveData FindById(string objectiveId)
        {
            for (int i = 0; i < objectives.Count; i++)
            {
                if (objectives[i] != null && objectives[i].ObjectiveId == objectiveId)
                {
                    return objectives[i];
                }
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Achevement automatique
        // ------------------------------------------------------------------

        private void OnItemPickedUp(ItemPickedUpEvent evt)
        {
            ObjectiveData current = CurrentObjective;

            if (current == null || current.CompletionMode != ObjectiveCompletionMode.CollectItem)
            {
                return;
            }

            if (current.RequiredItem == null || evt.Item != current.RequiredItem)
            {
                return;
            }

            if (evt.Player == null)
            {
                return;
            }

            PlayerInventory inventory = evt.Player.GetComponent<PlayerInventory>();

            if (inventory == null || inventory.CountOf(current.RequiredItem) < current.RequiredCount)
            {
                return;
            }

            CompleteObjective(current.ObjectiveId);
        }

        private bool IsAlreadySatisfied(ObjectiveData objective)
        {
            if (objective == null || objective.CompletionMode != ObjectiveCompletionMode.CollectItem || objective.RequiredItem == null)
            {
                return false;
            }

            Player.PlayerManager players = Player.PlayerManager.HasInstance ? Player.PlayerManager.Instance : null;

            if (players == null || players.LocalPlayer == null)
            {
                return false;
            }

            PlayerInventory inventory = players.LocalPlayer.GetComponent<PlayerInventory>();

            if (inventory == null)
            {
                return false;
            }

            return inventory.CountOf(objective.RequiredItem) >= objective.RequiredCount;
        }

        // ------------------------------------------------------------------
        // Indices
        // ------------------------------------------------------------------

        private void UpdateHint()
        {
            if (_hintTimer < 0f || _hintShown)
            {
                return;
            }

            _hintTimer -= Time.deltaTime;

            if (_hintTimer > 0f)
            {
                return;
            }

            ObjectiveData current = CurrentObjective;

            if (current == null || !current.HasHint)
            {
                _hintTimer = -1f;
                return;
            }

            _hintShown = true;
            _hintTimer = -1f;

            EventBus.Publish(new ObjectiveHintEvent(current, current.Hint));
        }

        // ------------------------------------------------------------------

        private string BuildDebugInfo()
        {
            ObjectiveData current = CurrentObjective;

            if (IsSequenceComplete)
            {
                return "  Chaine terminee (" + CompletedCount + "/" + objectives.Count + ")";
            }

            if (current == null)
            {
                return "  Aucun objectif actif";
            }

            return "  " + (_currentIndex + 1) + "/" + objectives.Count + " : " + current.Title
                + "\n  id : " + current.ObjectiveId
                + "   mode : " + current.CompletionMode;
        }
    }
}
