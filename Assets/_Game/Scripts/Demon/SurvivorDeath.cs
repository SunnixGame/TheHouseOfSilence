using System;
using UnityEngine;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Etat mort / vivant du survivant. DemonKill l'appelle a la fin de la jauge :
    /// le survivant se tourne vers son tueur et son corps joue l'animation de mort
    /// (declencheur "Die"). Revive() le remet debout (declencheur "Revive") ; pour les
    /// tests, PlayableCharacterSwitcher le ressuscite quand on repasse au survivant (F2).
    /// </summary>
    [DisallowMultipleComponent]
    public class SurvivorDeath : MonoBehaviour
    {
        [SerializeField] private Animator bodyAnimator;
        [SerializeField] private string dieTrigger = "Die";
        [SerializeField] private string reviveTrigger = "Revive";

        private float _dieAt = -1f;

        public bool IsDead { get; private set; }

        /// <summary>Declenche a la mort (avant l'animation) : tueur en parametre.</summary>
        public event Action<Transform> Killed;

        /// <summary>Tue le survivant : il se tourne vers le tueur, puis s'effondre apres 'delay' secondes.</summary>
        public void Kill(Transform killer, float delay)
        {
            if (IsDead) return;

            IsDead = true;

            if (killer != null)
            {
                Vector3 toKiller = killer.position - transform.position;
                toKiller.y = 0f;
                if (toKiller.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(toKiller);
            }

            _dieAt = Time.time + Mathf.Max(0f, delay);
            if (Killed != null) Killed(killer);
        }

        public void Revive()
        {
            if (!IsDead) return;

            IsDead = false;
            _dieAt = -1f;

            if (bodyAnimator != null && bodyAnimator.isActiveAndEnabled)
            {
                bodyAnimator.ResetTrigger(dieTrigger);
                bodyAnimator.SetTrigger(reviveTrigger);
            }
        }

        private void Update()
        {
            if (_dieAt >= 0f && Time.time >= _dieAt)
            {
                _dieAt = -1f;

                if (bodyAnimator != null && bodyAnimator.isActiveAndEnabled)
                {
                    bodyAnimator.ResetTrigger(reviveTrigger);
                    bodyAnimator.SetTrigger(dieTrigger);
                }
            }
        }
    }
}
