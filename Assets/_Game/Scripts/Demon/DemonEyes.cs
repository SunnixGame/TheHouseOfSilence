using UnityEngine;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Yeux rouges du demon, visibles de loin la nuit : deux halos (shader EyeGlow)
    /// poses sur les yeux + une petite lueur rouge sur le visage. Les yeux clignent
    /// de temps en temps. Tout se regle dans l'Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class DemonEyes : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int MinSizeId = Shader.PropertyToID("_MinAngularSize");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        private static readonly int FadeEndId = Shader.PropertyToID("_FadeEnd");
        private static readonly int SideId = Shader.PropertyToID("_Side");
        private static readonly int MinSeparationId = Shader.PropertyToID("_MinSeparation");

        [Header("Halos")]
        [SerializeField] private bool glowEnabled = true;
        [SerializeField] private Renderer[] glows = new Renderer[0];
        [SerializeField, ColorUsage(true, true)] private Color color = new Color(4f, 0.08f, 0.04f, 1f);
        [SerializeField, Range(0f, 4f)] private float intensity = 1f;
        [Tooltip("Diametre du halo vu de pres (m) : a peu pres la taille de l'oeil.")]
        [SerializeField, Min(0f)] private float size = 0.022f;
        [Tooltip("Taille minimale par metre de distance : garde les yeux visibles de loin (0.008 = ~5 px a 1080p).")]
        [SerializeField, Min(0f)] private float minAngularSize = 0.008f;
        [Tooltip("Ecart minimal entre les deux yeux par metre de distance : de loin on voit toujours deux points.")]
        [SerializeField, Min(0f)] private float minSeparation = 0.009f;
        [Tooltip("Distance ou les halos commencent / finissent de s'estomper (m).")]
        [SerializeField] private Vector2 fadeDistance = new Vector2(90f, 160f);

        [Header("Lueur sur le visage (optionnelle, aucune par defaut)")]
        [Tooltip("Lumiere qui teinte le visage autour des yeux. Vide = la couleur reste sur les yeux.")]
        [SerializeField] private Light faceLight;
        [SerializeField, Min(0f)] private float faceLightIntensity = 0.3f;

        [Header("Clignement")]
        [SerializeField] private bool blink = true;
        [Tooltip("Intervalle aleatoire entre deux clignements (s).")]
        [SerializeField] private Vector2 blinkInterval = new Vector2(3f, 9f);
        [SerializeField, Min(0.01f)] private float blinkDuration = 0.14f;

        [Header("Pulsation")]
        [SerializeField, Range(0f, 1f)] private float pulse = 0.15f;
        [SerializeField, Min(0f)] private float pulseSpeed = 1.3f;

        private MaterialPropertyBlock _block;
        private float _nextBlink;
        private float _blinkStart = -1f;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);
        }

        private void LateUpdate()
        {
            float open = 1f;

            if (blink)
            {
                if (_blinkStart < 0f && Time.time >= _nextBlink)
                {
                    _blinkStart = Time.time;
                }

                if (_blinkStart >= 0f)
                {
                    float t = (Time.time - _blinkStart) / blinkDuration;
                    open = Mathf.Abs(t * 2f - 1f);   // 1 -> 0 -> 1

                    if (t >= 1f)
                    {
                        _blinkStart = -1f;
                        open = 1f;
                        _nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);
                    }
                }
            }

            float value = glowEnabled ? intensity * open * (1f + pulse * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f)) : 0f;

            _block.Clear();
            _block.SetColor(ColorId, color);
            _block.SetFloat(SizeId, size);
            _block.SetFloat(MinSizeId, minAngularSize);
            _block.SetFloat(IntensityId, value);
            _block.SetFloat(FadeStartId, fadeDistance.x);
            _block.SetFloat(FadeEndId, Mathf.Max(fadeDistance.x + 1f, fadeDistance.y));

            _block.SetFloat(MinSeparationId, minSeparation);

            foreach (Renderer r in glows)
            {
                if (r == null) continue;
                r.enabled = glowEnabled;
                // Oeil gauche (EyeGlow_L) vers -X de la tete, droit vers +X.
                _block.SetFloat(SideId, r.name.EndsWith("_L") ? -1f : r.name.EndsWith("_R") ? 1f : 0f);
                r.SetPropertyBlock(_block);
            }

            if (faceLight != null)
            {
                faceLight.enabled = glowEnabled;
                faceLight.intensity = faceLightIntensity * open;
            }
        }
    }
}
