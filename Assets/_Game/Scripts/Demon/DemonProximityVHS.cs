using System.Collections.Generic;
using UnityEngine;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Sur le survivant : quand un demon entre dans le perimetre, la vue se degrade en
    /// image VHS (shader VHSOverlay), d'autant plus fort qu'il est proche. Des qu'il en
    /// sort, l'image redevient normale en fondu. Tout se regle dans l'Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class DemonProximityVHS : MonoBehaviour
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [Header("Activation")]
        [SerializeField] private bool enableEffect = true;

        [Header("Perimetre (m)")]
        [Tooltip("Le demon declenche l'effet en entrant dans ce rayon.")]
        [SerializeField, Min(0.5f)] private float radius = 25f;
        [Tooltip("Marge de sortie : l'effet ne s'arrete qu'au-dela de rayon + marge (evite le clignotement en bordure).")]
        [SerializeField, Min(0f)] private float exitMargin = 3f;
        [Tooltip("A cette distance ou moins, l'effet est a son maximum.")]
        [SerializeField, Min(0f)] private float closeDistance = 5f;

        [Header("Intensite")]
        [Tooltip("Intensite en bordure du perimetre.")]
        [SerializeField, Range(0f, 1f)] private float edgeIntensity = 0.35f;
        [Tooltip("Intensite quand le demon est tout pres.")]
        [SerializeField, Range(0f, 1f)] private float maxIntensity = 1f;
        [Tooltip("Temps pour que l'effet apparaisse / disparaisse (s).")]
        [SerializeField, Min(0.01f)] private float fadeIn = 0.6f;
        [SerializeField, Min(0.01f)] private float fadeOut = 1.5f;
        [Tooltip("Intensite maximale pendant un jumpscare (le visage doit rester lisible).")]
        [SerializeField, Range(0f, 1f)] private float jumpscareIntensity = 0.4f;

        [Header("References")]
        [Tooltip("Quad plein ecran (materiau VHSOverlay) colle a la camera du survivant.")]
        [SerializeField] private Renderer overlay;
        [Tooltip("Camera du survivant : l'effet n'est rendu que quand elle est active.")]
        [SerializeField] private Camera view;

        private readonly List<DemonController> _demons = new List<DemonController>();
        private float _nextSearch;
        private float _intensity;
        private bool _inside;
        private MaterialPropertyBlock _block;

        /// <summary>Intensite courante de l'effet (0 = image normale).</summary>
        public float Intensity { get { return _intensity; } }

        /// <summary>Un demon est dans le perimetre.</summary>
        public bool DemonNearby { get { return _inside; } }

        /// <summary>Distance au demon le plus proche (infini s'il n'y en a pas).</summary>
        public float NearestDistance { get; private set; } = float.PositiveInfinity;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (view == null) view = GetComponentInChildren<Camera>(true);
            Apply();
        }

        private void Update()
        {
            if (Time.time >= _nextSearch)
            {
                _nextSearch = Time.time + 1f;
                _demons.Clear();
                _demons.AddRange(FindObjectsByType<DemonController>());
            }

            float nearest = float.PositiveInfinity;

            foreach (DemonController demon in _demons)
            {
                if (demon == null || !demon.isActiveAndEnabled) continue;

                // Demon deguise en survivant : rien ne le trahit.
                DemonDisguise disguise = demon.GetComponent<DemonDisguise>();
                if (disguise != null && disguise.IsActive) continue;
                nearest = Mathf.Min(nearest, Vector3.Distance(transform.position, demon.transform.position));
            }

            NearestDistance = nearest;

            float limit = _inside ? radius + exitMargin : radius;
            _inside = enableEffect && nearest <= limit;

            float target = 0f;

            if (_inside)
            {
                float k = Mathf.InverseLerp(radius, Mathf.Min(closeDistance, radius - 0.01f), nearest);
                target = Mathf.Lerp(edgeIntensity, maxIntensity, k);
            }

            // Pendant un jumpscare, l'image reste parasitee mais le visage du demon lisible.
            if (SurvivorJumpscare.AnyPlaying) target = Mathf.Min(target, jumpscareIntensity);

            float duration = target > _intensity ? fadeIn : fadeOut;
            _intensity = Mathf.MoveTowards(_intensity, target, Time.deltaTime / duration);
            Apply();
        }

        private void Apply()
        {
            if (overlay == null) return;

            bool visible = _intensity > 0.001f && (view == null || view.isActiveAndEnabled);
            overlay.enabled = visible;

            if (visible)
            {
                _block.Clear();
                _block.SetFloat(IntensityId, _intensity);
                overlay.SetPropertyBlock(_block);
            }
        }

        private void OnDisable()
        {
            _intensity = 0f;
            _inside = false;
            if (overlay != null) overlay.enabled = false;
        }
    }
}
