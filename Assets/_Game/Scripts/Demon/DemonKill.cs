using System.Collections;
using System.Collections.Generic;
using HouseOfSilence.Network;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Mise a mort : quand le demon reste a moins de 1 m d'un survivant vivant, une
    /// jauge "TUER" se charge en 3 s (elle retombe s'il s'eloigne). A 100 %, E lance
    /// l'execution : le demon se place face a la victime et bondit sur elle (animation
    /// "Kill"), la victime s'effondre (SurvivorDeath), cri + craquement + flash rouge.
    /// Tout se regle dans l'Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class DemonKill : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private bool enableKill = true;
        [Tooltip("#(E) = la touche qui affiche E (AZERTY comme QWERTY).")]
        [SerializeField] private string killBinding = "<Keyboard>/#(E)";

        [Header("Charge")]
        [Tooltip("Distance au survivant (m, a plat) pour charger la jauge.")]
        [SerializeField, Min(0.1f)] private float killRange = 1f;
        [Tooltip("Temps a passer a proximite pour remplir la jauge (s).")]
        [SerializeField, Min(0.1f)] private float chargeTime = 3f;
        [Tooltip("Vitesse a laquelle la jauge retombe hors de portee (x la vitesse de charge). 0 = remise a zero immediate.")]
        [SerializeField, Min(0f)] private float drainSpeed = 2f;

        [Header("Execution")]
        [Tooltip("Distance a laquelle le demon se place face a la victime (m).")]
        [SerializeField, Min(0.2f)] private float strikeDistance = 0.6f;
        [Tooltip("Duree pendant laquelle le demon est bloque (longueur de l'animation Kill).")]
        [SerializeField, Min(0.1f)] private float killDuration = 2f;
        [Tooltip("Moment ou la victime commence a s'effondrer (s apres le debut).")]
        [SerializeField, Min(0f)] private float victimDelay = 0.55f;
        [Tooltip("Moment de l'impact (craquement, secousse, flash).")]
        [SerializeField, Min(0f)] private float impactTime = 0.6f;

        [Header("Jumpscare (vue FPS de la victime)")]
        [Tooltip("Coche : la mise a mort se voit par les yeux de la victime (le demon saute au visage et l'etrangle).")]
        [SerializeField] private bool useJumpscare = true;
        [SerializeField] private string jumpscareTrigger = "Jumpscare";
        [Tooltip("Distance a laquelle le demon se place devant la victime pour le jumpscare (m).")]
        [SerializeField, Min(0.2f)] private float jumpscareDistance = 0.5f;
        [Tooltip("Duree totale du jumpscare (etranglement + chute + noir).")]
        [SerializeField, Min(0.5f)] private float jumpscareDuration = 5.8f;
        [Tooltip("Moment ou le corps de la victime s'effondre (quand la vue tombe).")]
        [SerializeField, Min(0f)] private float jumpscareDeathDelay = 3.3f;

        [Header("References")]
        [SerializeField] private DemonController controller;
        [SerializeField] private Animator animator;
        [SerializeField] private string killTrigger = "Kill";
        [SerializeField] private AudioSource sfxSource;

        [Header("Sons et effets")]
        [SerializeField] private AudioClip killScream;
        [SerializeField, Range(0f, 1f)] private float screamVolume = 1f;
        [SerializeField] private AudioClip impactSound;
        [SerializeField, Range(0f, 1f)] private float impactVolume = 1f;
        [SerializeField, Min(0f)] private float impactShake = 0.1f;
        [SerializeField, Min(0f)] private float flashDuration = 0.5f;

        [Header("HUD")]
        [SerializeField] private bool showHud = true;

        private InputAction _action;
        private bool _pending;
        private float _charge;
        private SurvivorDeath _target;
        private bool _executing;
        private float _flashUntil;
        private string _message;
        private float _messageUntil;
        private readonly List<SurvivorDeath> _survivors = new List<SurvivorDeath>();
        private float _nextSearch;

        private Transform _head;
        private GUIStyle _labelStyle;
        private GUIStyle _messageStyle;

        /// <summary>Charge de la jauge (0 a 1).</summary>
        public float Charge { get { return _charge; } }
        public bool IsExecuting { get { return _executing; } }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<DemonController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Tete de la poupee : la camera de la victime la fixe pendant le jumpscare.
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "head") _head = t;
            }
        }

        private void OnEnable()
        {
            _action = new InputAction("DemonKill", InputActionType.Button, killBinding);
            _action.performed += _ => _pending = true;
            _action.Enable();
        }

        private void OnDisable()
        {
            if (_action != null)
            {
                _action.Disable();
                _action.Dispose();
                _action = null;
            }

            _charge = 0f;
        }

        private void Update()
        {
            bool pressed = _pending;
            _pending = false;

            if (_executing || !enableKill || controller == null)
            {
                return;
            }

            bool allowed = controller.InputAllowed;
            DemonCry cry = GetComponent<DemonCry>();
            bool busy = cry != null && cry.IsCrying;

            SurvivorDeath nearest = allowed && !busy ? NearestVictim() : null;

            if (nearest != null)
            {
                if (nearest != _target) _charge = 0f;   // nouvelle cible : on recommence
                _target = nearest;
                _charge = Mathf.MoveTowards(_charge, 1f, Time.deltaTime / chargeTime);
            }
            else
            {
                _charge = drainSpeed <= 0f ? 0f : Mathf.MoveTowards(_charge, 0f, Time.deltaTime * drainSpeed / chargeTime);
                if (_charge <= 0f) _target = null;
            }

            if (pressed && allowed && _target != null && nearest == _target && _charge >= 1f)
            {
                StartCoroutine(Execute(_target));
            }
        }

        private SurvivorDeath NearestVictim()
        {
            if (Time.time >= _nextSearch)
            {
                _nextSearch = Time.time + 1f;
                _survivors.Clear();
                _survivors.AddRange(FindObjectsByType<SurvivorDeath>());
            }

            SurvivorDeath best = null;
            float bestDistance = killRange;

            foreach (SurvivorDeath s in _survivors)
            {
                if (s == null || s.IsDead || !s.isActiveAndEnabled) continue;

                Vector3 d = s.transform.position - transform.position;
                d.y = 0f;
                float distance = d.magnitude;

                if (distance <= bestDistance && Mathf.Abs(s.transform.position.y - transform.position.y) < 1.5f)
                {
                    best = s;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private IEnumerator Execute(SurvivorDeath victim)
        {
            SurvivorJumpscare jumpscare = useJumpscare ? victim.GetComponent<SurvivorJumpscare>() : null;

            if (jumpscare != null)
            {
                yield return Jumpscare(victim, jumpscare);
                yield break;
            }

            _executing = true;
            _charge = 0f;
            controller.MovementLocked = true;

            // Face a face : le demon se place devant la victime, qui se tourne vers lui.
            Vector3 toDemon = transform.position - victim.transform.position;
            toDemon.y = 0f;
            if (toDemon.sqrMagnitude < 0.0001f) toDemon = victim.transform.forward;
            toDemon.Normalize();

            Vector3 spot = victim.transform.position + toDemon * strikeDistance;
            spot.y = transform.position.y;
            controller.TeleportTo(spot, Quaternion.LookRotation(-toDemon));
            victim.Kill(transform, victimDelay);
            NetBridge.RaiseDemonKilled(this, victim, false);

            if (animator != null && animator.runtimeAnimatorController != null) animator.SetTrigger(killTrigger);
            if (sfxSource != null && killScream != null) sfxSource.PlayOneShot(killScream, screamVolume);

            yield return new WaitForSeconds(impactTime);

            if (sfxSource != null && impactSound != null) sfxSource.PlayOneShot(impactSound, impactVolume);
            controller.Shake(impactShake, 0.5f);
            _flashUntil = Time.time + flashDuration;

            yield return new WaitForSeconds(Mathf.Max(0f, killDuration - impactTime));

            controller.MovementLocked = false;
            _executing = false;
            _target = null;
            _message = "JOUEUR TUE";
            _messageUntil = Time.time + 3f;
        }

        /// <summary>
        /// Version jumpscare : le demon se colle a la victime, bondit a son visage et
        /// l'etrangle ; la sequence est vue par la camera FPS de la victime.
        /// </summary>
        private IEnumerator Jumpscare(SurvivorDeath victim, SurvivorJumpscare jumpscare)
        {
            _executing = true;
            _charge = 0f;
            controller.MovementLocked = true;

            Vector3 toDemon = transform.position - victim.transform.position;
            toDemon.y = 0f;
            if (toDemon.sqrMagnitude < 0.0001f) toDemon = victim.transform.forward;
            toDemon.Normalize();

            Vector3 spot = victim.transform.position + toDemon * jumpscareDistance;
            spot.y = transform.position.y;
            controller.TeleportTo(spot, Quaternion.LookRotation(-toDemon));
            victim.Kill(transform, jumpscareDeathDelay);

            if (animator != null && animator.runtimeAnimatorController != null) animator.SetTrigger(jumpscareTrigger);

            // En ligne, la victime est jouee ailleurs : c'est sur son ecran que passe le jumpscare.
            if (NetBridge.Online) NetBridge.RaiseDemonKilled(this, victim, true);
            else jumpscare.Play(_head != null ? _head : transform, controller.Camera);

            yield return new WaitForSeconds(jumpscareDuration);

            controller.MovementLocked = false;
            _executing = false;
            _target = null;
            _message = "JOUEUR TUE";
            _messageUntil = Time.time + 3f;
        }

        /// <summary>
        /// Mise a mort jouee par un autre joueur (en ligne) : animation et sons du demon,
        /// la victime s'effondre ; si c'est notre survivant, le jumpscare passe sur notre ecran.
        /// Le demon est deja place face a la victime (sa position arrive du reseau).
        /// </summary>
        public void PlayRemoteKill(SurvivorDeath victim, bool jumpscare, bool victimIsLocal)
        {
            if (victim == null) return;

            DemonDisguise disguise = GetComponent<DemonDisguise>();
            if (disguise != null) disguise.EndImmediate();

            StartCoroutine(RemoteKill(victim, jumpscare, victimIsLocal));
        }

        private IEnumerator RemoteKill(SurvivorDeath victim, bool jumpscare, bool victimIsLocal)
        {
            _executing = true;
            bool hasAnimator = animator != null && animator.runtimeAnimatorController != null;

            if (jumpscare)
            {
                victim.Kill(transform, jumpscareDeathDelay);
                if (hasAnimator) animator.SetTrigger(jumpscareTrigger);

                SurvivorJumpscare scare = victim.GetComponent<SurvivorJumpscare>();
                if (victimIsLocal && scare != null) scare.Play(_head != null ? _head : transform, null);

                yield return new WaitForSeconds(jumpscareDuration);
            }
            else
            {
                victim.Kill(transform, victimDelay);
                if (hasAnimator) animator.SetTrigger(killTrigger);
                if (sfxSource != null && killScream != null) sfxSource.PlayOneShot(killScream, screamVolume);

                yield return new WaitForSeconds(impactTime);

                if (sfxSource != null && impactSound != null) sfxSource.PlayOneShot(impactSound, impactVolume);

                yield return new WaitForSeconds(Mathf.Max(0f, killDuration - impactTime));
            }

            _executing = false;
        }

        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!showHud || controller == null || !controller.IsControlled || !controller.InputAllowed || SurvivorJumpscare.AnyPlaying)
            {
                return;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _messageStyle = new GUIStyle(_labelStyle);
            }

            float scale = Screen.height / 1080f;
            Color previous = GUI.color;

            if (_flashUntil > Time.time && flashDuration > 0f)
            {
                GUI.color = new Color(0.45f, 0f, 0f, 0.6f * (_flashUntil - Time.time) / flashDuration);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            }

            if (!_executing && _charge > 0f && _target != null)
            {
                float w = 360f * scale, h = 16f * scale;
                Rect bar = new Rect((Screen.width - w) * 0.5f, Screen.height - 150f * scale, w, h);
                bool ready = _charge >= 1f;
                float pulse = ready ? 0.75f + 0.25f * Mathf.Sin(Time.time * 10f) : 1f;

                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), Texture2D.whiteTexture);
                GUI.color = ready ? new Color(1f, 0.12f, 0.08f, pulse) : new Color(0.7f, 0.1f, 0.08f, 0.9f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * _charge, bar.height), Texture2D.whiteTexture);

                GUI.color = Color.white;
                _labelStyle.fontSize = Mathf.RoundToInt((ready ? 22f : 17f) * scale);
                _labelStyle.normal.textColor = ready ? new Color(1f, 0.3f, 0.2f, pulse) : new Color(1f, 0.8f, 0.75f, 0.9f);
                string text = ready ? "TUER  [E]" : "TUER   " + Mathf.FloorToInt(_charge * 100f) + " %";
                GUI.Label(new Rect(bar.x, bar.y - 32f * scale, bar.width, 28f * scale), text, _labelStyle);
            }

            if (!string.IsNullOrEmpty(_message) && Time.time < _messageUntil)
            {
                GUI.color = Color.white;
                _messageStyle.fontSize = Mathf.RoundToInt(30f * scale);
                _messageStyle.normal.textColor = new Color(0.8f, 0.05f, 0.03f, Mathf.Clamp01(_messageUntil - Time.time));
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 40f * scale), _message, _messageStyle);
            }

            GUI.color = previous;
        }
    }
}
