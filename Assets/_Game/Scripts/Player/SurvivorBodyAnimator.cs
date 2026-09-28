using UnityEngine;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Anime le corps visible du survivant (vue TPS, ou vu par le demon) d'apres son
    /// deplacement : l'Animator recoit "Speed" normalise, 0 = arret, 1 = marche, 2 = course,
    /// ce qui melange Idle / Walk / Run quelles que soient les vitesses reglees sur PlayerMotor.
    ///
    /// Ajoute automatiquement par PlayerCharacter si absent. Sans parametre "Speed" dans
    /// l'Animator (controleur pas encore regenere), il ne fait rien.
    /// </summary>
    [DisallowMultipleComponent]
    public class SurvivorBodyAnimator : MonoBehaviour
    {
        [Tooltip("Animator du corps. Vide = celui de l'enfant 'SurvivorBody'.")]
        [SerializeField] private Animator animator;
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField, Min(0f)] private float damping = 0.12f;

        private PlayerMotor _motor;
        private FlyMode _fly;
        private int _speedHash;
        private bool _hasParameter;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _fly = GetComponent<FlyMode>();
            _speedHash = Animator.StringToHash(speedParameter);

            if (animator == null)
            {
                Transform body = transform.Find("SurvivorBody");
                if (body != null) animator = body.GetComponent<Animator>();
            }
        }

        private void Update()
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            {
                _hasParameter = false;
                return;
            }

            if (!_hasParameter)
            {
                _hasParameter = HasFloat(animator, _speedHash);
                if (!_hasParameter) return;
            }

            animator.SetFloat(_speedHash, NormalizedSpeed(), damping, Time.deltaTime);
        }

        /// <summary>0 a l'arret, 1 a la vitesse de marche, 2 a la vitesse de course.</summary>
        private float NormalizedSpeed()
        {
            // Survivant fige (on joue le demon) ou en vol libre : pas de pas.
            if (_motor == null || !_motor.enabled || (_fly != null && _fly.IsFlying))
            {
                return 0f;
            }

            float speed = _motor.CurrentSpeed;
            float walk = Mathf.Max(0.1f, _motor.WalkSpeed);
            float run = Mathf.Max(walk + 0.1f, _motor.RunSpeed);

            if (speed <= walk) return speed / walk;
            return Mathf.Min(2f, 1f + (speed - walk) / (run - walk));
        }

        private static bool HasFloat(Animator animator, int hash)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Float) return true;
            }

            return false;
        }
    }
}
