using System.Collections.Generic;
using HouseOfSilence.Core;
using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Ragdoll du survivant : a sa mort, l'Animator du corps est coupe et le squelette
    /// tombe sous l'effet de la physique (elan de la course + poussee du tueur).
    ///
    /// Le ragdoll est construit par code a la premiere mort, a partir des os Humanoid
    /// (bassin, poitrine, tete, bras, jambes) : rien a regler dans les scenes. Hors mort,
    /// ses corps sont cinematiques et ses colliders coupes (aucun effet sur le jeu).
    ///
    /// Declencheurs : PlayerDiedEvent (PlayerHealth) et SurvivorDeath (tue par le demon).
    /// En vue FPS, la camera passe en TPS sur le corps tant que dure la mort.
    /// Ajoute automatiquement par PlayerCharacter si absent.
    /// </summary>
    [DisallowMultipleComponent]
    public class SurvivorRagdoll : MonoBehaviour
    {
        [Tooltip("Animator du corps. Vide = celui de l'enfant 'SurvivorBody'.")]
        [SerializeField] private Animator animator;

        [Header("Physique")]
        [SerializeField, Min(1f)] private float totalMass = 55f;
        [Tooltip("Part de la vitesse de deplacement transmise au corps a la mort.")]
        [SerializeField, Range(0f, 1.5f)] private float inheritVelocity = 1f;
        [Tooltip("Poussee au niveau du buste, en m/s, a l'oppose du tueur.")]
        [SerializeField, Min(0f)] private float killerPush = 2.5f;

        private sealed class Part
        {
            public Transform Bone;
            public Rigidbody Body;
            public Collider Collider;
        }

        private readonly List<Part> _parts = new List<Part>();
        private PlayerCharacter _owner;
        private SurvivorViewMode _viewMode;
        private Rigidbody _hips;
        private Rigidbody _chest;
        private bool _built;
        private Vector3 _lastPosition;
        private Vector3 _velocity;
        private Transform _bodyParent;
        private Vector3 _bodyLocalPosition;
        private Quaternion _bodyLocalRotation;

        public bool IsActive { get; private set; }

        private void Awake()
        {
            _owner = GetComponent<PlayerCharacter>();
            _lastPosition = transform.position;

            if (animator == null)
            {
                Transform body = transform.Find("SurvivorBody");
                if (body != null) animator = body.GetComponent<Animator>();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerDiedEvent>(OnPlayerDied);
            EventBus.Subscribe<PlayerHealthChangedEvent>(OnHealthChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
            EventBus.Unsubscribe<PlayerHealthChangedEvent>(OnHealthChanged);
        }

        private void Update()
        {
            // Vitesse reelle, memorisee avant que la mort n'arrete le PlayerMotor.
            float dt = Time.deltaTime;
            if (dt > 0f && !IsActive)
            {
                Vector3 v = (transform.position - _lastPosition) / dt;
                _velocity = v.sqrMagnitude < 15f * 15f ? v : Vector3.zero; // teleportation ignoree
            }

            _lastPosition = transform.position;
        }

        private void OnPlayerDied(PlayerDiedEvent evt)
        {
            if (evt.Player == _owner) Activate(null);
        }

        private void OnHealthChanged(PlayerHealthChangedEvent evt)
        {
            if (IsActive && evt.Player == _owner && evt.Current > 0f) Deactivate();
        }

        /// <summary>
        /// Lache le corps. 'killer' (facultatif) le pousse dans la direction opposee.
        /// Renvoie false s'il n'y a pas de corps Humanoid a faire tomber.
        /// </summary>
        public bool Activate(Transform killer)
        {
            if (IsActive) return true;
            if (animator == null) return false;

            // Le corps doit etre visible (en FPS il est masque) et pose avant d'etre fige.
            if (!animator.gameObject.activeSelf)
            {
                animator.gameObject.SetActive(true);
                animator.Update(0f); // pose immediate, sinon le squelette partirait de la pose de repos
            }
            if (!_built) Build();
            if (_parts.Count == 0) return false;

            IsActive = true;
            animator.enabled = false;

            // Le corps se detache du joueur : tourner la vue (ou recevoir une rotation du
            // reseau) ne doit plus faire pivoter le cadavre avec la racine.
            Transform body = animator.transform;
            _bodyParent = body.parent;
            _bodyLocalPosition = body.localPosition;
            _bodyLocalRotation = body.localRotation;
            body.SetParent(null, true);

            Vector3 push = Vector3.zero;
            if (killer != null)
            {
                Vector3 away = transform.position - killer.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f) push = away.normalized * killerPush;
            }

            Vector3 inherited = _velocity * inheritVelocity;

            foreach (Part part in _parts)
            {
                part.Body.isKinematic = false;
                part.Body.interpolation = RigidbodyInterpolation.Interpolate;
                part.Collider.enabled = true;
                part.Body.linearVelocity = inherited;
                part.Body.angularVelocity = Vector3.zero;
            }

            IgnoreSelfCollisions();

            if (_chest != null) _chest.linearVelocity += push;

            if (_viewMode == null) _viewMode = GetComponent<SurvivorViewMode>();
            if (_viewMode != null) _viewMode.SetDeathFocus(_hips != null ? _hips.transform : null);
            return true;
        }

        /// <summary>Rend le corps a l'Animator (reapparition, test du demon).</summary>
        public void Deactivate()
        {
            if (!IsActive) return;

            IsActive = false;

            foreach (Part part in _parts)
            {
                part.Body.isKinematic = true;
                part.Body.interpolation = RigidbodyInterpolation.None; // l'Animator reprend les os
                part.Collider.enabled = false;
            }

            if (animator != null)
            {
                // Le corps revient sous le joueur, a sa place d'origine.
                Transform body = animator.transform;
                if (_bodyParent != null) body.SetParent(_bodyParent, false);
                body.localPosition = _bodyLocalPosition;
                body.localRotation = _bodyLocalRotation;

                animator.enabled = true;
                animator.Rebind();
            }

            _lastPosition = transform.position;
            _velocity = Vector3.zero;

            if (_viewMode != null) _viewMode.SetDeathFocus(null);
        }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        private void Build()
        {
            _built = true;

            if (!animator.isHuman)
            {
                Debug.LogWarning("[SurvivorRagdoll] Le corps n'est pas Humanoid : pas de ragdoll.", this);
                return;
            }

            Transform hips = Bone(HumanBodyBones.Hips);
            Transform chest = Bone(HumanBodyBones.Chest);
            if (chest == null) chest = Bone(HumanBodyBones.Spine);
            Transform head = Bone(HumanBodyBones.Head);
            Transform neck = Bone(HumanBodyBones.Neck);

            if (hips == null || chest == null || head == null) return;

            // Axes du personnage (au moment de la mort) pour orienter les articulations.
            Vector3 right = transform.right;
            Vector3 forward = transform.forward;
            Vector3 up = transform.up;

            // Bassin et buste.
            _hips = AddSphere(hips, hips.position + up * 0.05f, 0.14f, 0.2f, null).Body;
            Part chestPart = AddCapsule(chest, neck != null ? neck : head, 0.15f, 1f, 0.22f, _hips);
            _chest = chestPart.Body;
            SetLimits(chestPart, right, forward, -20f, 20f, 15f, 15f);

            // Tete : sphere dans le prolongement du cou.
            Vector3 headDir = neck != null ? (head.position - neck.position).normalized : up;
            Part headPart = AddSphere(head, head.position + headDir * 0.09f, 0.11f, 0.08f, _chest);
            SetLimits(headPart, right, forward, -30f, 40f, 25f, 10f); // + = menton vers la poitrine

            foreach (bool left in new[] { true, false })
            {
                // Jambes : +X (droite) = la cuisse part en avant avec un angle negatif.
                Transform upperLeg = Bone(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                Transform lowerLeg = Bone(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                Transform foot = Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);

                if (upperLeg != null && lowerLeg != null && foot != null)
                {
                    Part thigh = AddCapsule(upperLeg, lowerLeg, 0.075f, 1f, 0.12f, _hips);
                    SetLimits(thigh, right, forward, -90f, 25f, 30f, 10f);
                    Part calf = AddCapsule(lowerLeg, foot, 0.055f, 1.1f, 0.07f, thigh.Body);
                    SetLimits(calf, right, forward, -2f, 110f, 0f, 0f);
                }

                // Bras : le coude plie vers l'avant (angle negatif autour de +X).
                Transform upperArm = Bone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                Transform lowerArm = Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                Transform hand = Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);

                if (upperArm != null && lowerArm != null && hand != null)
                {
                    Part arm = AddCapsule(upperArm, lowerArm, 0.045f, 1f, 0.03f, _chest);
                    SetLimits(arm, right, forward, -100f, 40f, 60f, 30f);
                    Part forearm = AddCapsule(lowerArm, hand, 0.04f, 1.25f, 0.025f, arm.Body);
                    SetLimits(forearm, right, forward, -120f, 2f, 0f, 0f);
                }
            }
        }

        /// <summary>
        /// Le CharacterController du joueur ne repousse pas son propre corps, et deux os
        /// relies ne se bloquent pas. A refaire a chaque activation : Unity oublie ces
        /// exclusions quand un collider est desactive.
        /// </summary>
        private void IgnoreSelfCollisions()
        {
            Collider[] own = GetComponents<Collider>();

            foreach (Part part in _parts)
            {
                foreach (Collider c in own) Physics.IgnoreCollision(part.Collider, c, true);

                foreach (Part other in _parts)
                {
                    if (other != part && IsNeighbour(part, other)) Physics.IgnoreCollision(part.Collider, other.Collider, true);
                }
            }
        }

        private static bool IsNeighbour(Part a, Part b)
        {
            CharacterJoint ja = a.Bone.GetComponent<CharacterJoint>();
            CharacterJoint jb = b.Bone.GetComponent<CharacterJoint>();
            return (ja != null && ja.connectedBody == b.Body) || (jb != null && jb.connectedBody == a.Body);
        }

        private Transform Bone(HumanBodyBones bone)
        {
            return animator.GetBoneTransform(bone);
        }

        private Part AddSphere(Transform bone, Vector3 worldCenter, float worldRadius, float massShare, Rigidbody parent)
        {
            SphereCollider sphere = bone.gameObject.AddComponent<SphereCollider>();
            sphere.center = bone.InverseTransformPoint(worldCenter);
            sphere.radius = worldRadius / Scale(bone);
            return AddBody(bone, sphere, massShare, parent);
        }

        /// <summary>Capsule de l'os vers son enfant (le long de l'axe local le plus aligne).</summary>
        private Part AddCapsule(Transform bone, Transform child, float worldRadius, float lengthScale, float massShare, Rigidbody parent)
        {
            Vector3 local = bone.InverseTransformPoint(child.position) * lengthScale;
            Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            int axis = abs.x >= abs.y && abs.x >= abs.z ? 0 : (abs.y >= abs.z ? 1 : 2);

            CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = axis;
            capsule.center = local * 0.5f;
            capsule.radius = worldRadius / Scale(bone);
            capsule.height = local.magnitude + capsule.radius * 2f;
            return AddBody(bone, capsule, massShare, parent);
        }

        private Part AddBody(Transform bone, Collider collider, float massShare, Rigidbody parent)
        {
            Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = totalMass * massShare;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = 12;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.5f;
            collider.enabled = false;

            if (parent != null)
            {
                CharacterJoint joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                joint.enableProjection = true;
            }

            Part part = new Part { Bone = bone, Body = body, Collider = collider };
            _parts.Add(part);
            return part;
        }

        /// <summary>Limites de l'articulation (degres) : torsion autour de 'twist', balancements.</summary>
        private static void SetLimits(Part part, Vector3 twistAxis, Vector3 swingAxis, float lowTwist, float highTwist, float swing1, float swing2)
        {
            CharacterJoint joint = part.Bone.GetComponent<CharacterJoint>();
            if (joint == null) return;

            joint.axis = part.Bone.InverseTransformDirection(twistAxis);
            joint.swingAxis = part.Bone.InverseTransformDirection(swingAxis);
            joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
            joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
            joint.swing1Limit = new SoftJointLimit { limit = swing1 };
            joint.swing2Limit = new SoftJointLimit { limit = swing2 };
        }

        private static float Scale(Transform bone)
        {
            Vector3 s = bone.lossyScale;
            return Mathf.Max(0.0001f, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f);
        }
    }
}
