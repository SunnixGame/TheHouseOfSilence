using System.Collections.Generic;
using UnityEngine;

namespace HouseOfSilence.Interaction
{
    /// <summary>
    /// Surbrillance discrete d'un objet vise, sans creer d'instance de materiau
    /// (on utilise un MaterialPropertyBlock : zero allocation, compatible GPU instancing).
    ///
    /// Volontairement sobre : dans un jeu d'horreur, un contour fluo casse
    /// l'ambiance. Un simple eclaircissement suffit a dire "c'est utilisable".
    ///
    /// Composant optionnel : InteractableBase fonctionne sans lui.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractableHighlight : MonoBehaviour
    {
        private struct RendererSlot
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public string ColorProperty;
            public Color OriginalColor;
        }

        [Header("Apparence")]
        [SerializeField] private Color highlightColor = new Color(1f, 0.92f, 0.72f, 1f);

        [Tooltip("0 = aucun effet, 1 = remplace totalement la couleur d'origine.")]
        [SerializeField, Range(0f, 1f)] private float blend = 0.45f;

        [Tooltip("Vitesse du fondu. 0 = instantane.")]
        [SerializeField, Min(0f)] private float fadeSpeed = 12f;

        [Header("Cible")]
        [Tooltip("Laisser vide pour utiliser tous les Renderer enfants.")]
        [SerializeField] private Renderer[] targetRenderers;

        private readonly List<RendererSlot> _slots = new List<RendererSlot>(4);
        private MaterialPropertyBlock _block;
        private float _current;
        private float _target;
        private bool _initialized;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _block = new MaterialPropertyBlock();

            Renderer[] renderers = targetRenderers != null && targetRenderers.Length > 0
                ? targetRenderers
                : GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;

                for (int m = 0; m < materials.Length; m++)
                {
                    Material material = materials[m];

                    if (material == null)
                    {
                        continue;
                    }

                    string property = null;

                    if (material.HasProperty(BaseColorId))
                    {
                        property = "_BaseColor";
                    }
                    else if (material.HasProperty(LegacyColorId))
                    {
                        property = "_Color";
                    }

                    if (property == null)
                    {
                        continue;
                    }

                    RendererSlot slot = new RendererSlot();
                    slot.Renderer = renderer;
                    slot.MaterialIndex = m;
                    slot.ColorProperty = property;
                    slot.OriginalColor = material.GetColor(property);

                    _slots.Add(slot);
                }
            }

            _initialized = true;
        }

        /// <summary>Active ou coupe la surbrillance.</summary>
        public void SetHighlighted(bool highlighted)
        {
            Initialize();
            _target = highlighted ? 1f : 0f;

            if (fadeSpeed <= 0f)
            {
                _current = _target;
                Apply();
            }
        }

        private void Update()
        {
            if (fadeSpeed <= 0f || Mathf.Approximately(_current, _target))
            {
                return;
            }

            _current = Mathf.MoveTowards(_current, _target, fadeSpeed * Time.unscaledDeltaTime);
            Apply();
        }

        private void Apply()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                RendererSlot slot = _slots[i];

                if (slot.Renderer == null)
                {
                    continue;
                }

                Color color = Color.Lerp(slot.OriginalColor, highlightColor, blend * _current);

                slot.Renderer.GetPropertyBlock(_block, slot.MaterialIndex);
                _block.SetColor(slot.ColorProperty, color);
                slot.Renderer.SetPropertyBlock(_block, slot.MaterialIndex);
            }
        }

        private void OnDisable()
        {
            _current = 0f;
            _target = 0f;

            if (_initialized)
            {
                Apply();
            }
        }
    }
}
