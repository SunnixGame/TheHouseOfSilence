using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.NPC
{
    /// <summary>
    /// Figurants immobiles (enfants de cet objet) : au lancement, chacun est pose sur le
    /// sol, tourne vers le joueur, recoit l'Animator Controller d'Idle et un collider
    /// pour qu'on ne le traverse pas. Chaque figurant demarre son Idle a un moment et a
    /// une vitesse differents, pour qu'ils ne bougent pas tous en meme temps.
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcBystanders : MonoBehaviour
    {
        [Tooltip("Controleur donne aux figurants dont l'Animator n'en a pas.")]
        [SerializeField] private RuntimeAnimatorController idleController;

        [Header("Placement")]
        [SerializeField] private bool snapToGround = true;
        [SerializeField] private bool facePlayer = true;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Animation")]
        [Tooltip("Vitesse de lecture tiree au hasard pour chaque figurant.")]
        [SerializeField] private Vector2 speedRange = new Vector2(0.85f, 1.1f);

        [Header("Collision")]
        [SerializeField] private bool addCollider = true;
        [SerializeField, Min(0.5f)] private float colliderHeight = 1.75f;
        [SerializeField, Min(0.1f)] private float colliderRadius = 0.3f;

        private void Start()
        {
            PlayerCharacter player = facePlayer ? FindAnyObjectByType<PlayerCharacter>() : null;

            foreach (Transform npc in transform)
            {
                if (!npc.gameObject.activeSelf) continue;

                if (snapToGround) SnapToGround(npc);

                if (player != null)
                {
                    Vector3 toPlayer = player.transform.position - npc.position;
                    toPlayer.y = 0f;
                    if (toPlayer.sqrMagnitude > 0.01f) npc.rotation = Quaternion.LookRotation(toPlayer);
                }

                SetupAnimator(npc.GetComponentInChildren<Animator>());

                if (addCollider && npc.GetComponent<Collider>() == null)
                {
                    CapsuleCollider capsule = npc.gameObject.AddComponent<CapsuleCollider>();
                    capsule.height = colliderHeight;
                    capsule.radius = colliderRadius;
                    capsule.center = Vector3.up * (colliderHeight * 0.5f);
                }
            }
        }

        private void SnapToGround(Transform npc)
        {
            Vector3 origin = npc.position + Vector3.up * 5f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 30f, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;

            foreach (RaycastHit hit in hits)
            {
                // Ni le figurant lui-meme, ni le joueur.
                if (hit.collider.transform.IsChildOf(npc) || hit.collider.GetComponentInParent<PlayerCharacter>() != null) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    npc.position = hit.point;
                }
            }
        }

        private void SetupAnimator(Animator animator)
        {
            if (animator == null) return;

            if (animator.runtimeAnimatorController == null) animator.runtimeAnimatorController = idleController;
            if (animator.runtimeAnimatorController == null) return;

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            animator.speed = Random.Range(speedRange.x, speedRange.y);
            animator.Play(0, 0, Random.value); // etat par defaut, a un moment au hasard du cycle
        }
    }
}
