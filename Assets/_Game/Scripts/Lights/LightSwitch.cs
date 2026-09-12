using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Interaction;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Lights
{
    /// <summary>
    /// Interrupteur mural. Allume ou eteint un groupe de lampes.
    ///
    /// Cibles : la liste Targets, ou, si elle est vide, toutes les lampes dans
    /// Auto Radius autour de l'interrupteur (pratique : un interrupteur par piece
    /// sans rien cabler).
    ///
    /// Sans courant, l'interrupteur clique dans le vide : le prompt le dit.
    /// </summary>
    public class LightSwitch : InteractableBase
    {
        [Header("Lampes")]
        [Tooltip("Lampes commandees. Vide = toutes celles dans Auto Radius.")]
        [SerializeField] private List<LightController> targets = new List<LightController>();

        [SerializeField, Min(1f)] private float autoRadius = 6f;

        [Header("Textes")]
        [SerializeField] private string turnOnPrompt = "Allumer";
        [SerializeField] private string turnOffPrompt = "Eteindre";
        [SerializeField] private string noPowerPrompt = "Pas de courant";

        [Header("Bruit")]
        [SerializeField, Min(0f)] private float clickNoiseRadius = 4f;

        private readonly List<LightController> _resolved = new List<LightController>(8);
        private float _lastResolveTime = -10f;

        protected override void Awake()
        {
            base.Awake();

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = "Interrupteur";
            }
        }

        private List<LightController> ResolveTargets()
        {
            if (targets != null && targets.Count > 0)
            {
                return targets;
            }

            // Recherche par rayon, rafraichie au plus toutes les secondes.
            if (Time.time - _lastResolveTime > 1f)
            {
                _lastResolveTime = Time.time;

                LightManager manager = LightManager.HasInstance ? LightManager.Instance : null;

                if (manager != null)
                {
                    manager.GetLightsWithin(transform.position, autoRadius, _resolved);
                }
                else
                {
                    _resolved.Clear();
                }
            }

            return _resolved;
        }

        private bool AnyLit(List<LightController> lights)
        {
            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] != null && lights[i].IsLit)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPower()
        {
            if (!LightManager.HasInstance)
            {
                return true;
            }

            LightManager manager = LightManager.Instance;
            return manager == null || manager.IsPowered;
        }

        public override bool CanInteract(PlayerCharacter player)
        {
            return base.CanInteract(player) && HasPower();
        }

        public override string GetPrompt(PlayerCharacter player)
        {
            if (!Interactable)
            {
                return base.GetPrompt(player);
            }

            if (!HasPower())
            {
                return noPowerPrompt;
            }

            return AnyLit(ResolveTargets()) ? turnOffPrompt : turnOnPrompt;
        }

        protected override void OnInteracted(PlayerCharacter player)
        {
            List<LightController> lights = ResolveTargets();
            bool turnOff = AnyLit(lights);

            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] == null)
                {
                    continue;
                }

                if (turnOff)
                {
                    lights[i].TurnOff();
                }
                else
                {
                    lights[i].TurnOn();
                }
            }

            Noise.Emit(transform.position, clickNoiseRadius, NoiseType.Interaction, gameObject);
        }

        protected override void OnInteractionRefused(PlayerCharacter player)
        {
            // Clic dans le vide : la maison n'a pas de courant.
            Noise.Emit(transform.position, clickNoiseRadius * 0.5f, NoiseType.Interaction, gameObject);
        }
    }
}
