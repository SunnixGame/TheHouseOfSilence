using UnityEngine;

namespace HouseOfSilence.Horror
{
    /// <summary>Ce que fait concretement l'evenement.</summary>
    public enum HorrorEventKind
    {
        /// <summary>Une porte ouverte claque.</summary>
        DoorSlam = 0,

        /// <summary>Une porte fermee (non verrouillee) s'ouvre toute seule.</summary>
        DoorOpen = 1,

        /// <summary>Une ou plusieurs lampes proches scintillent.</summary>
        LightFlicker = 2,

        /// <summary>Une lampe proche eclate.</summary>
        LightBreak = 3,

        /// <summary>Coupure generale pendant Duration secondes, puis retour.</summary>
        PowerCut = 4,

        /// <summary>Un objet physique (HorrorProp) tombe ou est pousse.</summary>
        ObjectFall = 5,

        /// <summary>Son seul : pas, murmure, cri, telephone, radio... a une position choisie.</summary>
        Sound = 6,

        /// <summary>Apparition breve : Visual Prefab a une position, pendant Duration.</summary>
        Apparition = 7
    }

    /// <summary>Ou placer un evenement sonore ou visuel.</summary>
    public enum HorrorPositionMode
    {
        /// <summary>Derriere le joueur, a Min/Max Distance.</summary>
        BehindPlayer = 0,

        /// <summary>Direction aleatoire autour du joueur, a Min/Max Distance.</summary>
        AroundPlayer = 1,

        /// <summary>Devant le joueur, au bout du couloir, a Min/Max Distance.</summary>
        InFrontOfPlayer = 2,

        /// <summary>Le HorrorSpot le plus proche portant Spot Tag.</summary>
        NearestSpot = 3,

        /// <summary>Un HorrorSpot au hasard portant Spot Tag.</summary>
        RandomSpot = 4
    }

    /// <summary>Etat de la creature exige par l'evenement (alimente en Phase 11).</summary>
    public enum MonsterRequirement
    {
        Any = 0,
        MonsterDormant = 1,
        MonsterActive = 2
    }

    /// <summary>
    /// Definition d'un evenement horrifique. Donnee pure, sans reference de scene :
    /// c'est le HorrorEventManager qui trouve les portes, lampes, objets et
    /// emplacements au moment de l'execution.
    ///
    /// Creation : clic droit > Create > House of Silence > Horror Event Data
    /// Ranger dans Assets/_Game/ScriptableObjects/Horror/.
    /// </summary>
    [CreateAssetMenu(fileName = "HE_New", menuName = "House of Silence/Horror Event Data", order = 2)]
    public class HorrorEventData : ScriptableObject
    {
        [Header("Identite")]
        [SerializeField] private string eventId = "event_new";
        [SerializeField] private string displayName = "Nouvel evenement";
        [SerializeField] private HorrorEventKind kind = HorrorEventKind.Sound;

        [TextArea(2, 4)]
        [SerializeField] private string designerNotes = "";

        [Header("Conditions")]
        [Tooltip("Peur minimale du joueur cible (0-100).")]
        [SerializeField, Range(0f, 100f)] private float minFear = 0f;

        [Tooltip("Peur maximale : au dela, l'evenement ne se declenche plus (evite d'enfoncer un joueur deja en panique).")]
        [SerializeField, Range(0f, 100f)] private float maxFear = 100f;

        [Tooltip("Temps de jeu minimal, en secondes, avant que l'evenement soit possible.")]
        [SerializeField, Min(0f)] private float minGameTime = 60f;

        [Tooltip("Index minimal de l'objectif courant (0 = des le premier).")]
        [SerializeField, Min(0)] private int minObjectiveIndex = 0;

        [Tooltip("Index maximal de l'objectif courant (-1 = sans limite).")]
        [SerializeField] private int maxObjectiveIndex = -1;

        [SerializeField] private MonsterRequirement monsterRequirement = MonsterRequirement.Any;

        [Tooltip("Exige que le courant general soit retabli (coupure, lampes).")]
        [SerializeField] private bool requiresPower = false;

        [Header("Frequence")]
        [Tooltip("Poids relatif dans le tirage aleatoire.")]
        [SerializeField, Min(0.01f)] private float weight = 1f;

        [Tooltip("Delai minimal entre deux occurrences de CET evenement, en secondes.")]
        [SerializeField, Min(0f)] private float cooldown = 60f;

        [Tooltip("Nombre maximal d'occurrences par partie. 0 = illimite.")]
        [SerializeField, Min(0)] private int maxOccurrences = 0;

        [Header("Effet")]
        [Tooltip("Peur ajoutee aux joueurs a portee.")]
        [SerializeField, Min(0f)] private float fearImpact = 5f;

        [Tooltip("Rayon dans lequel les joueurs subissent la peur.")]
        [SerializeField, Min(0f)] private float fearRadius = 15f;

        [Tooltip("Rayon du bruit emis (entendu par la creature). 0 = silencieux.")]
        [SerializeField, Min(0f)] private float noiseRadius = 8f;

        [Tooltip("Duree, en secondes : coupure de courant, apparition, scintillement.")]
        [SerializeField, Min(0f)] private float duration = 2f;

        [Header("Position")]
        [SerializeField] private HorrorPositionMode positionMode = HorrorPositionMode.AroundPlayer;

        [Tooltip("Distance minimale au joueur pour choisir une cible ou une position.")]
        [SerializeField, Min(0f)] private float minDistance = 3f;

        [Tooltip("Distance maximale au joueur.")]
        [SerializeField, Min(0.5f)] private float maxDistance = 15f;

        [Tooltip("Etiquette de HorrorSpot pour les modes NearestSpot / RandomSpot. Vide = n'importe lequel.")]
        [SerializeField] private string spotTag = "";

        [Header("Medias (facultatifs)")]
        [SerializeField] private AudioClip sound;
        [SerializeField, Range(0f, 1f)] private float soundVolume = 0.9f;
        [SerializeField] private GameObject visualPrefab;

        // ------------------------------------------------------------------

        public string EventId { get { return eventId; } }
        public string DisplayName { get { return displayName; } }
        public HorrorEventKind Kind { get { return kind; } }

        /// <summary>Notes de conception, affichees dans l'overlay de debug.</summary>
        public string DesignerNotes { get { return designerNotes; } }
        public float MinFear { get { return minFear; } }
        public float MaxFear { get { return maxFear; } }
        public float MinGameTime { get { return minGameTime; } }
        public int MinObjectiveIndex { get { return minObjectiveIndex; } }
        public int MaxObjectiveIndex { get { return maxObjectiveIndex; } }
        public MonsterRequirement MonsterRequirement { get { return monsterRequirement; } }
        public bool RequiresPower { get { return requiresPower; } }
        public float Weight { get { return weight; } }
        public float Cooldown { get { return cooldown; } }
        public int MaxOccurrences { get { return maxOccurrences; } }
        public float FearImpact { get { return fearImpact; } }
        public float FearRadius { get { return fearRadius; } }
        public float NoiseRadius { get { return noiseRadius; } }
        public float Duration { get { return duration; } }
        public HorrorPositionMode PositionMode { get { return positionMode; } }
        public float MinDistance { get { return minDistance; } }
        public float MaxDistance { get { return Mathf.Max(minDistance + 0.1f, maxDistance); } }
        public string SpotTag { get { return spotTag; } }
        public AudioClip Sound { get { return sound; } }
        public float SoundVolume { get { return soundVolume; } }
        public GameObject VisualPrefab { get { return visualPrefab; } }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(eventId))
            {
                eventId = name.ToLowerInvariant();
            }

            if (maxFear < minFear)
            {
                maxFear = minFear;
            }
        }
#endif
    }

    /// <summary>
    /// Publie a chaque evenement declenche. L'audio (Phase 14), les
    /// hallucinations et tout systeme custom peuvent s'y abonner.
    /// </summary>
    public readonly struct HorrorEventTriggeredEvent
    {
        public readonly HorrorEventData Data;
        public readonly Vector3 Position;

        public HorrorEventTriggeredEvent(HorrorEventData data, Vector3 position)
        {
            Data = data;
            Position = position;
        }
    }
}
