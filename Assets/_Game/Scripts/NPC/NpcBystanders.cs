using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace HouseOfSilence.NPC
{
    /// <summary>
    /// Personnages du pack places sous cet objet.
    ///
    /// Jouables (par defaut) : au lancement, chacun devient un survivant complet. Le
    /// joueur de la scene est copie (camera, lampe, deplacement, mort, ragdoll,
    /// jumpscare, effet VHS...) et le personnage remplace son corps. Le demon les traite
    /// donc exactement comme le survivant d'origine, et PlayableCharacterSwitcher peut
    /// en prendre le controle.
    ///
    /// Figurants (convertToSurvivors decoche) : poses au sol, tournes vers le joueur,
    /// Idle et collider, comme avant.
    /// </summary>
    [DefaultExecutionOrder(-200)] // avant RandomSpawner (-100) : les copies ont aussi leur clairiere
    [DisallowMultipleComponent]
    public class NpcBystanders : MonoBehaviour
    {
        [Tooltip("Chaque personnage devient un survivant jouable (copie du joueur de la scene).")]
        [SerializeField] private bool convertToSurvivors = true;

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
            PlayerCharacter player = FindAnyObjectByType<PlayerCharacter>();

            if (convertToSurvivors && player != null)
            {
                ConvertAll(player);
                return;
            }

            foreach (Transform npc in transform)
            {
                if (!npc.gameObject.activeSelf) continue;

                if (snapToGround) SnapToGround(npc);

                if (facePlayer && player != null)
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

        // ------------------------------------------------------------------
        // PNJ jouables
        // ------------------------------------------------------------------

        private void ConvertAll(PlayerCharacter template)
        {
            Transform templateBody = template.transform.Find("SurvivorBody");
            Animator templateAnimator = templateBody != null ? templateBody.GetComponent<Animator>() : null;
            RuntimeAnimatorController controller = templateAnimator != null && templateAnimator.runtimeAnimatorController != null
                ? templateAnimator.runtimeAnimatorController
                : idleController;

            // Copie inactive : les Awake() ne tournent qu'une fois le nouveau corps en place.
            GameObject holder = new GameObject("_NpcSurvivorFactory");
            holder.SetActive(false);

            Transform[] npcs = new Transform[transform.childCount];
            for (int i = 0; i < npcs.Length; i++) npcs[i] = transform.GetChild(i);

            int id = 1;
            foreach (Transform npc in npcs)
            {
                if (!npc.gameObject.activeSelf) continue;

                Quaternion facing = Quaternion.Euler(0f, npc.eulerAngles.y, 0f);
                GameObject clone = Instantiate(template.gameObject, npc.position, facing, holder.transform);
                clone.name = npc.name;

                Transform oldBody = clone.transform.Find("SurvivorBody");
                if (oldBody != null) DestroyImmediate(oldBody.gameObject);

                npc.SetParent(clone.transform, false);
                npc.localPosition = Vector3.zero;
                npc.localRotation = Quaternion.identity;
                npc.name = "SurvivorBody";
                PrepareBody(npc.gameObject, controller);

                // Pas deux cameras / ecoutes actives : PlayableCharacterSwitcher choisit la bonne.
                Camera view = clone.GetComponentInChildren<Camera>(true);
                if (view != null)
                {
                    view.enabled = false;
                    AudioListener listener = view.GetComponent<AudioListener>();
                    if (listener != null) listener.enabled = false;
                }

                clone.GetComponent<PlayerCharacter>().SetIdentity(id++, DisplayName(clone.name), false);
                clone.transform.SetParent(null, true); // active : Awake() avec le nouveau corps
            }

            Destroy(holder);
        }

        /// <summary>"NPC_Homme_1" -> "Homme 1".</summary>
        private static string DisplayName(string objectName)
        {
            string n = objectName.StartsWith("NPC_") ? objectName.Substring(4) : objectName;
            return n.Replace('_', ' ');
        }

        /// <summary>Animations du survivant, un seul niveau de detail (ragdoll, contour du demon), pas d'ombre (lampe).</summary>
        private static void PrepareBody(GameObject body, RuntimeAnimatorController controller)
        {
            LODGroup lods = body.GetComponent<LODGroup>();
            if (lods != null)
            {
                LOD[] levels = lods.GetLODs();
                for (int i = 1; i < levels.Length; i++)
                {
                    foreach (Renderer r in levels[i].renderers)
                    {
                        if (r != null) DestroyImmediate(r.gameObject);
                    }
                }

                DestroyImmediate(lods);
            }

            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                // La lampe torche est tenue contre le corps : pas d'ombre portee.
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }

            foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);

            Animator animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
        }

        // ------------------------------------------------------------------
        // Figurants
        // ------------------------------------------------------------------

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
