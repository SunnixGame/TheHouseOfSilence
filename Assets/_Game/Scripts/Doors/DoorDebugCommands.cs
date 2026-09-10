using HouseOfSilence.Core.Debugging;
using HouseOfSilence.Interaction;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Doors
{
    /// <summary>
    /// Permet de tester les portes sans attendre la creature (Phase 12).
    /// Les actions s'appliquent a la porte visee par le joueur.
    ///
    /// K : claquer la porte visee (evenement horrifique).
    /// L : frapper la porte visee ; sur une BreakableDoor elle finit par ceder,
    ///     sinon elle est forcee malgre son verrou.
    ///
    /// Les deux touches sont modifiables dans l'Inspector. Elles sont
    /// volontairement choisies parmi les lettres : l'Input System identifie les
    /// touches par leur POSITION PHYSIQUE, et K / L occupent la meme place sur
    /// AZERTY et QWERTY. Les touches de ponctuation comme ² ou ` ne sont pas
    /// fiables d'une disposition a l'autre.
    ///
    /// A poser sur le joueur. Purement optionnel.
    /// </summary>
    [DisallowMultipleComponent]
    public class DoorDebugCommands : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private PlayerCharacter player;

        [Header("Touches")]
        [SerializeField] private bool registerCommands = true;
        [SerializeField] private Key slamKey = Key.K;
        [SerializeField] private Key breakKey = Key.L;

        [Header("Reglages")]
        [Tooltip("Degats infliges par coup a une BreakableDoor.")]
        [SerializeField, Min(1f)] private float damagePerHit = 40f;

        [Tooltip("Portee du raycast de secours quand la porte n'est pas la cible d'interaction.")]
        [SerializeField, Min(1f)] private float fallbackRange = 4.5f;

        private void Awake()
        {
            if (interactor == null)
            {
                interactor = GetComponent<PlayerInteractor>();
            }

            if (player == null)
            {
                player = GetComponent<PlayerCharacter>();
            }
        }

        private void OnEnable()
        {
            if (!registerCommands)
            {
                return;
            }

            DebugManager.Register(slamKey, "Claquer la porte visee", SlamTargetDoor);
            DebugManager.Register(breakKey, "Frapper / forcer la porte visee", HitTargetDoor);
        }

        private void OnDisable()
        {
            if (!registerCommands)
            {
                return;
            }

            DebugManager.Unregister(slamKey);
            DebugManager.Unregister(breakKey);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Cherche la porte visee. On passe d'abord par la cible d'interaction ;
        /// si elle n'existe pas (porte deja cassee, hors portee d'interaction,
        /// collider vise sur le dormant), on retombe sur un raycast direct.
        /// </summary>
        private DoorBase GetTargetDoor()
        {
            if (interactor != null && interactor.CurrentTarget != null)
            {
                Transform target = interactor.CurrentTarget.Transform;

                if (target != null)
                {
                    DoorBase door = target.GetComponentInParent<DoorBase>();

                    if (door != null)
                    {
                        return door;
                    }
                }
            }

            Camera camera = player != null ? player.Camera : GetComponentInChildren<Camera>(true);

            if (camera == null)
            {
                return null;
            }

            RaycastHit hit;

            if (!Physics.Raycast(camera.transform.position, camera.transform.forward, out hit, fallbackRange, ~0, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            return hit.collider.GetComponentInParent<DoorBase>();
        }

        private void SlamTargetDoor()
        {
            DoorBase door = GetTargetDoor();

            if (door == null)
            {
                Debug.Log("[Debug] Aucune porte visee (approche-toi du battant et vise-le).");
                return;
            }

            door.Slam();
            Debug.Log("[Debug] '" + door.name + "' a claque.", door);
        }

        private void HitTargetDoor()
        {
            DoorBase door = GetTargetDoor();

            if (door == null)
            {
                Debug.Log("[Debug] Aucune porte visee (approche-toi du battant et vise-le).");
                return;
            }

            BreakableDoor breakable = door as BreakableDoor;

            if (breakable != null)
            {
                if (breakable.IsBroken)
                {
                    Debug.Log("[Debug] '" + door.name + "' a deja cede.", door);
                    return;
                }

                breakable.TakeDamage(damagePerHit);

                if (breakable.IsBroken)
                {
                    Debug.Log("[Debug] '" + door.name + "' a CEDE.", door);
                }
                else
                {
                    Debug.Log("[Debug] Coup porte sur '" + door.name + "'. Structure restante : "
                        + Mathf.RoundToInt(breakable.IntegrityNormalized * 100f) + " %", door);
                }

                return;
            }

            door.ForceOpen();
            Debug.Log("[Debug] '" + door.name + "' forcee (verrou ignore).", door);
        }
    }
}
