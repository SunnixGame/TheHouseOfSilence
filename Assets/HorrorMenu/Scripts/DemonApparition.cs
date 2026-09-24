using UnityEngine;

namespace HouseOfSilence.HorrorMenu
{
    /// <summary>
    /// Le demon est invisible a l'ouverture du menu et apparait au premier
    /// eclair ; ses pleurs demarrent alors en boucle et ne s'arretent plus
    /// tant que le menu est charge.
    /// </summary>
    [DisallowMultipleComponent]
    public class DemonApparition : MonoBehaviour
    {
        [SerializeField] private LightningStorm lightning;

        [Tooltip("Pleurs joues en boucle a partir de l'apparition.")]
        [SerializeField] private AudioSource crying;

        private Renderer[] _renderers;
        private Light[] _lights;
        private bool _revealed;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _lights = GetComponentsInChildren<Light>(true);
            SetVisible(false);

            if (crying != null)
            {
                crying.playOnAwake = false;
                crying.loop = true;
                crying.Stop();
            }
        }

        private void OnEnable()
        {
            if (lightning != null)
            {
                lightning.Struck += Reveal;
            }
        }

        private void OnDisable()
        {
            if (lightning != null)
            {
                lightning.Struck -= Reveal;
            }
        }

        public void Reveal()
        {
            if (_revealed)
            {
                return;
            }

            _revealed = true;
            SetVisible(true);

            if (crying != null)
            {
                crying.Play();
            }

            // Une seule apparition : plus besoin d'ecouter les eclairs.
            if (lightning != null)
            {
                lightning.Struck -= Reveal;
            }
        }

        private void SetVisible(bool visible)
        {
            foreach (Renderer r in _renderers)
            {
                if (r != null)
                {
                    r.enabled = visible;
                }
            }

            foreach (Light l in _lights)
            {
                if (l != null)
                {
                    l.enabled = visible;
                }
            }
        }
    }
}
