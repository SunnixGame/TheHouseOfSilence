using System.Collections.Generic;
using HouseOfSilence.Core;
using UnityEngine;

namespace HouseOfSilence.Horror
{
    /// <summary>
    /// Objet physique que les evenements peuvent faire tomber ou pousser :
    /// livre, boite de conserve, cadre, chaise...
    ///
    /// A poser sur un objet avec un Rigidbody (non cinematique).
    /// Menu : Tools > House of Silence > Create Test Props.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class HorrorProp : MonoBehaviour
    {
        private static readonly List<HorrorProp> s_All = new List<HorrorProp>(32);

        [Tooltip("Force de la poussee, en impulsion.")]
        [SerializeField, Min(0f)] private float impulse = 2.5f;

        [Tooltip("Rayon du bruit a la chute.")]
        [SerializeField, Min(0f)] private float noiseRadius = 7f;

        [Tooltip("Delai minimal entre deux utilisations de cet objet.")]
        [SerializeField, Min(0f)] private float cooldown = 30f;

        private Rigidbody _body;
        private float _lastNudgeTime = -999f;

        /// <summary>Tous les props actifs de la scene.</summary>
        public static IReadOnlyList<HorrorProp> All { get { return s_All; } }

        /// <summary>Vrai si l'objet peut etre pousse maintenant.</summary>
        public bool IsAvailable { get { return Time.time - _lastNudgeTime >= cooldown; } }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            if (!s_All.Contains(this))
            {
                s_All.Add(this);
            }
        }

        private void OnDisable()
        {
            s_All.Remove(this);
        }

        /// <summary>Pousse l'objet : il tombe de son etagere, glisse, bascule.</summary>
        public void Nudge(float strengthMultiplier = 1f)
        {
            if (_body == null)
            {
                return;
            }

            _lastNudgeTime = Time.time;

            _body.WakeUp();

            // Poussee horizontale aleatoire, legerement vers le haut pour decoller d'une etagere.
            Vector3 direction = Random.insideUnitSphere;
            direction.y = Mathf.Abs(direction.y) * 0.4f + 0.2f;
            direction.Normalize();

            _body.AddForce(direction * impulse * strengthMultiplier, ForceMode.Impulse);
            _body.AddTorque(Random.insideUnitSphere * impulse * 0.5f, ForceMode.Impulse);

            Noise.Emit(transform.position, noiseRadius, NoiseType.Object, gameObject);
        }
    }
}
