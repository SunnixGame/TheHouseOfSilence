using System;
using HouseOfSilence.Core;
using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Sons de pas du joueur. Le rythme vient de PlayerMotor (PlayerFootstepEvent) ;
    /// ce composant choisit le son selon la surface sous les pieds :
    ///  - sur un Terrain : la couche de texture dominante (TL_Grass -> Grass...) ;
    ///  - sur un objet : un mot-cle du nom de son materiau (M_Tile_Old -> Tile...).
    ///
    /// Marche / course / saut / atterrissage, sans repeter deux fois le meme clip,
    /// avec une legere variation de hauteur. Accroupi = plus discret.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerFootstepAudio : MonoBehaviour
    {
        [Serializable]
        public class Surface
        {
            public string name;
            public AudioClip[] walk;
            public AudioClip[] run;
            public AudioClip[] jump;
            public AudioClip[] land;
        }

        [Serializable]
        public class SurfaceRule
        {
            [Tooltip("Nom de TerrainLayer, ou mot contenu dans le nom du materiau.")]
            public string match;
            public string surface;
        }

        [Header("References")]
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private AudioSource source;

        [Header("Surfaces")]
        [SerializeField] private Surface[] surfaces = new Surface[0];
        [SerializeField] private SurfaceRule[] terrainLayerRules = new SurfaceRule[0];
        [SerializeField] private SurfaceRule[] materialRules = new SurfaceRule[0];
        [SerializeField] private string defaultSurface = "DirtyGround";

        [Header("Volume")]
        [SerializeField, Range(0f, 1f)] private float walkVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] private float runVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float crouchVolume = 0.25f;
        [SerializeField, Range(0f, 1f)] private float landVolume = 0.85f;
        [SerializeField] private Vector2 pitchRange = new Vector2(0.93f, 1.07f);

        [Header("Atterrissage")]
        [Tooltip("Temps en l'air minimum pour jouer un son d'atterrissage.")]
        [SerializeField, Min(0f)] private float minAirTime = 0.25f;

        [SerializeField] private LayerMask groundMask = ~0;

        private readonly RaycastHit[] _hits = new RaycastHit[6];
        private AudioClip _lastClip;
        private bool _wasGrounded = true;
        private float _airTime;

        /// <summary>Surface detectee sous les pieds en ce moment (debug).</summary>
        public string CurrentSurfaceName { get { return DetectSurfaceName(); } }

        /// <summary>Dernier clip de pas joue (debug).</summary>
        public AudioClip LastClip { get { return _lastClip; } }

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerCharacter>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerFootstepEvent>(OnFootstep);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerFootstepEvent>(OnFootstep);
        }

        private void Update()
        {
            if (motor == null)
            {
                return;
            }

            // PlayerMotor ne signale pas l'atterrissage : on le deduit du passage air -> sol.
            bool grounded = motor.IsGrounded;

            if (!grounded)
            {
                _airTime += Time.deltaTime;
            }
            else if (!_wasGrounded)
            {
                if (_airTime >= minAirTime)
                {
                    Surface surface = CurrentSurface();
                    Play(surface != null ? surface.land : null, landVolume);
                }

                _airTime = 0f;
            }

            _wasGrounded = grounded;
        }

        private void OnFootstep(PlayerFootstepEvent evt)
        {
            if (player != null && evt.Player != player)
            {
                return;
            }

            Surface surface = CurrentSurface();

            if (surface == null)
            {
                return;
            }

            // Le moteur publie aussi un "pas" a l'impulsion du saut (deja en l'air).
            if (motor != null && !motor.IsGrounded)
            {
                Play(surface.jump, walkVolume);
                return;
            }

            if (motor != null && motor.IsCrouching)
            {
                Play(surface.walk, crouchVolume);
            }
            else if (motor != null && motor.IsSprinting)
            {
                Play(surface.run != null && surface.run.Length > 0 ? surface.run : surface.walk, runVolume);
            }
            else
            {
                Play(surface.walk, walkVolume);
            }
        }

        // ------------------------------------------------------------------
        // Surface
        // ------------------------------------------------------------------

        private Surface CurrentSurface()
        {
            return FindSurface(DetectSurfaceName()) ?? FindSurface(defaultSurface);
        }

        private string DetectSurfaceName()
        {
            Vector3 origin = transform.position + Vector3.up * 0.3f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, 1.2f, groundMask, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            RaycastHit ground = default(RaycastHit);
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best)
                {
                    continue;
                }

                best = _hits[i].distance;
                ground = _hits[i];
                found = true;
            }

            if (!found)
            {
                return defaultSurface;
            }

            Terrain terrain = ground.collider.GetComponent<Terrain>();

            if (terrain != null)
            {
                return Match(terrainLayerRules, DominantLayer(terrain, ground.point));
            }

            Renderer renderer = ground.collider.GetComponent<Renderer>();

            if (renderer != null && renderer.sharedMaterial != null)
            {
                return Match(materialRules, renderer.sharedMaterial.name);
            }

            return defaultSurface;
        }

        private static string DominantLayer(Terrain terrain, Vector3 point)
        {
            TerrainData data = terrain.terrainData;
            TerrainLayer[] layers = data.terrainLayers;

            if (layers == null || layers.Length == 0)
            {
                return string.Empty;
            }

            Vector3 local = point - terrain.transform.position;
            int x = Mathf.Clamp(Mathf.RoundToInt(local.x / data.size.x * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(local.z / data.size.z * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);

            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);
            int bestLayer = 0;

            for (int i = 1; i < layers.Length; i++)
            {
                if (weights[0, 0, i] > weights[0, 0, bestLayer])
                {
                    bestLayer = i;
                }
            }

            return layers[bestLayer] != null ? layers[bestLayer].name : string.Empty;
        }

        private string Match(SurfaceRule[] rules, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                foreach (SurfaceRule rule in rules)
                {
                    if (!string.IsNullOrEmpty(rule.match) && name.IndexOf(rule.match, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return rule.surface;
                    }
                }
            }

            return defaultSurface;
        }

        private Surface FindSurface(string name)
        {
            foreach (Surface s in surfaces)
            {
                if (s != null && s.name == name)
                {
                    return s;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------

        private void Play(AudioClip[] clips, float volume)
        {
            if (source == null || clips == null || clips.Length == 0)
            {
                return;
            }

            // Jamais deux fois le meme clip d'affilee.
            AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];

            if (clips.Length > 1 && clip == _lastClip)
            {
                clip = clips[(Array.IndexOf(clips, clip) + 1 + UnityEngine.Random.Range(0, clips.Length - 1)) % clips.Length];
            }

            _lastClip = clip;
            source.pitch = UnityEngine.Random.Range(pitchRange.x, pitchRange.y);
            source.PlayOneShot(clip, volume);
        }
    }
}
