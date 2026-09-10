using HouseOfSilence.Core;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.Events;

namespace HouseOfSilence.Doors
{
    /// <summary>
    /// Porte qui peut ceder sous les coups.
    ///
    /// Prevue pour deux usages :
    /// - la creature defonce une porte derriere laquelle le joueur s'est refugie
    ///   (Phase 12 : elle appellera TakeDamage() ou BreakNow()) ;
    /// - le joueur force un passage au pied-de-biche (Phase 24).
    ///
    /// Une porte cassee ne se referme plus et n'est plus interactive :
    /// le passage reste ouvert definitivement.
    /// </summary>
    public class BreakableDoor : DoorBase
    {
        [Header("Resistance")]
        [Tooltip("Points de structure. Chaque coup en retire.")]
        [SerializeField, Min(1f)] private float maxIntegrity = 100f;

        [Tooltip("Angle vers lequel la porte part quand elle cede.")]
        [SerializeField, Range(60f, 170f)] private float brokenAngle = 130f;

        [Tooltip("Desactive les colliders du battant une fois la porte cassee.")]
        [SerializeField] private bool disableCollidersWhenBroken = true;

        [Header("Audio")]
        [SerializeField] private AudioClip hitClip;
        [SerializeField] private AudioClip breakClip;

        [Header("Evenements")]
        [SerializeField] private UnityEvent onDoorBroken;

        private float _integrity;

        /// <summary>Structure restante, de 0 a 1.</summary>
        public float IntegrityNormalized { get { return maxIntegrity > 0f ? Mathf.Clamp01(_integrity / maxIntegrity) : 0f; } }

        /// <summary>Vrai si la porte a deja cede.</summary>
        public bool IsBroken { get { return State == DoorState.Broken; } }

        protected override void Awake()
        {
            base.Awake();
            _integrity = maxIntegrity;
        }

        /// <summary>
        /// Inflige des degats a la porte. Elle cede quand la structure tombe a zero.
        /// </summary>
        public void TakeDamage(float amount, DoorActor actor = DoorActor.Creature)
        {
            if (IsBroken || amount <= 0f)
            {
                return;
            }

            _integrity = Mathf.Max(0f, _integrity - amount);

            PlayClip(hitClip);
            EmitNoise(slamNoiseRadius * 0.8f, NoiseType.Door);

            if (_integrity <= 0f)
            {
                BreakNow(actor);
                return;
            }

            // Chaque coup ebranle le battant, de plus en plus fort a mesure que
            // la porte s'affaiblit. Il revient ensuite tout seul contre le dormant.
            float amplitude = Mathf.Lerp(5f, 14f, 1f - IntegrityNormalized);
            float shake = Random.value < 0.5f ? -amplitude : amplitude;

            ShakeTo(shake);
        }

        /// <summary>Fait ceder la porte immediatement.</summary>
        public void BreakNow(DoorActor actor = DoorActor.Creature)
        {
            if (IsBroken)
            {
                return;
            }

            _integrity = 0f;

            SetAngleImmediate(brokenAngle);
            MarkBroken(actor);

            PlayClip(breakClip != null ? breakClip : slamClip, 1f);
            Noise.Emit(transform.position, slamNoiseRadius * 1.4f, NoiseType.Explosion, gameObject);

            if (disableCollidersWhenBroken && hinge != null)
            {
                Collider[] colliders = hinge.GetComponentsInChildren<Collider>(true);

                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                    {
                        colliders[i].enabled = false;
                    }
                }
            }

            if (onDoorBroken != null)
            {
                onDoorBroken.Invoke();
            }

            Debug.Log("[Porte] '" + name + "' a cede.", this);
        }

        /// <summary>Repare la porte (nouvelle partie, debug).</summary>
        public void Repair()
        {
            _integrity = maxIntegrity;

            if (hinge != null)
            {
                Collider[] colliders = hinge.GetComponentsInChildren<Collider>(true);

                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                    {
                        colliders[i].enabled = true;
                    }
                }
            }

            SetAngleImmediate(0f);
            SetState(DoorState.Closed, DoorActor.Script);
        }

        public override bool IsFocusable(PlayerCharacter player)
        {
            if (IsBroken)
            {
                return false;
            }

            return base.IsFocusable(player);
        }
    }
}
