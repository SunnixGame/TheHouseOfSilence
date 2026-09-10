using HouseOfSilence.Core;
using HouseOfSilence.Interaction;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Doors
{
    /// <summary>
    /// Comportement commun a toutes les portes : rotation progressive sur
    /// charniere, verrouillage, bruit, audio et evenements.
    ///
    /// Hierarchie attendue (creee par Tools > House of Silence > Create Door) :
    ///
    ///     Door                (ce script, +9 axes de reglage)
    ///     +-- Frame           (dormant, immobile)
    ///     +-- Hinge           (pivot, c'est LUI qui tourne)
    ///         +-- Panel       (mesh + BoxCollider)
    ///
    /// Le collider etant sur un enfant, PlayerInteractor le remonte
    /// automatiquement jusqu'a ce composant.
    ///
    /// Une porte peut etre ouverte par le joueur, par un script, par un
    /// evenement horrifique ou par la creature : toutes ces sources passent
    /// par Open() / Close() / ForceOpen() / Slam().
    /// </summary>
    public abstract class DoorBase : InteractableBase
    {
        [Header("Charniere")]
        [Tooltip("Transform pivot. C'est lui qui tourne, pas la porte entiere.")]
        [SerializeField] protected Transform hinge;

        [Tooltip("Angle d'ouverture, en degres.")]
        [SerializeField, Range(30f, 170f)] protected float openAngle = 95f;

        [Tooltip("Vitesse d'ouverture, en degres par seconde.")]
        [SerializeField, Min(10f)] protected float openSpeed = 200f;

        [Tooltip("Vitesse de fermeture, en degres par seconde.")]
        [SerializeField, Min(10f)] protected float closeSpeed = 240f;

        [Tooltip("La porte s'ouvre du cote oppose a celui qui la pousse.")]
        [SerializeField] protected bool openAwayFromInstigator = true;

        [Header("Etat initial")]
        [SerializeField] protected bool startOpen = false;
        [SerializeField] protected bool startLocked = false;

        [Header("Regles")]
        [Tooltip("Decoche pour une porte qui ne peut plus etre refermee une fois ouverte.")]
        [SerializeField] protected bool canBeClosed = true;

        [Tooltip("Referme la porte toute seule apres un delai. 0 = jamais.")]
        [SerializeField, Min(0f)] protected float autoCloseDelay = 0f;

        [Header("Textes")]
        [SerializeField] protected string openPromptText = "Ouvrir";
        [SerializeField] protected string closePromptText = "Fermer";
        [SerializeField] protected string lockedPromptText = "Verrouillee";

        [Header("Bruit")]
        [Tooltip("Rayon audible par la creature quand la porte bouge normalement.")]
        [SerializeField, Min(0f)] protected float noiseRadius = 9f;

        [Tooltip("Rayon audible quand la porte claque ou cede.")]
        [SerializeField, Min(0f)] protected float slamNoiseRadius = 22f;

        [Header("Audio (facultatif, Phase 14)")]
        [SerializeField] protected AudioSource audioSource;
        [SerializeField] protected AudioClip openClip;
        [SerializeField] protected AudioClip closeClip;
        [SerializeField] protected AudioClip lockedClip;
        [SerializeField] protected AudioClip slamClip;

        // ------------------------------------------------------------------

        private Quaternion _hingeClosedRotation;
        private float _currentAngle;
        private float _targetAngle;
        private float _signedOpenAngle;
        private float _autoCloseTimer = -1f;
        private DoorState _state = DoorState.Closed;
        private bool _isLocked;

        /// <summary>Etat d'animation courant.</summary>
        public DoorState State { get { return _state; } }

        /// <summary>Vrai des que la porte n'est plus totalement fermee.</summary>
        public bool IsOpen { get { return _state == DoorState.Open || _state == DoorState.Opening || _state == DoorState.Broken; } }

        /// <summary>Vrai si la porte est completement fermee et immobile.</summary>
        public bool IsFullyClosed { get { return _state == DoorState.Closed; } }

        /// <summary>Vrai si la porte refuse de s'ouvrir sans condition supplementaire.</summary>
        public bool IsLocked { get { return _isLocked; } }

        /// <summary>Ouverture courante, de 0 (fermee) a 1 (grande ouverte).</summary>
        public float OpenAmount { get { return openAngle > 0f ? Mathf.Clamp01(Mathf.Abs(_currentAngle) / openAngle) : 0f; } }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();

            if (hinge == null)
            {
                Debug.LogError("[Porte] '" + name + "' n'a pas de charniere assignee. Utilise Tools > House of Silence > Create Door.", this);
                hinge = transform;
            }

            _hingeClosedRotation = hinge.localRotation;
            _isLocked = startLocked;
            _signedOpenAngle = openAngle;

            if (startOpen)
            {
                _currentAngle = _signedOpenAngle;
                _targetAngle = _signedOpenAngle;
                _state = DoorState.Open;
            }
            else
            {
                _currentAngle = 0f;
                _targetAngle = 0f;
                _state = DoorState.Closed;
            }

            ApplyHingeRotation();
        }

        protected virtual void Update()
        {
            UpdateRotation();
            UpdateAutoClose();
        }

        private void UpdateRotation()
        {
            if (Mathf.Approximately(_currentAngle, _targetAngle))
            {
                return;
            }

            bool opening = Mathf.Abs(_targetAngle) > Mathf.Abs(_currentAngle);
            float speed = opening ? openSpeed : closeSpeed;

            _currentAngle = Mathf.MoveTowards(_currentAngle, _targetAngle, speed * Time.deltaTime);

            ApplyHingeRotation();

            if (!Mathf.Approximately(_currentAngle, _targetAngle))
            {
                return;
            }

            // Fin d'animation.
            if (_state == DoorState.Opening)
            {
                SetState(DoorState.Open, DoorActor.Script);

                if (autoCloseDelay > 0f)
                {
                    _autoCloseTimer = autoCloseDelay;
                }
            }
            else if (_state == DoorState.Closing)
            {
                SetState(DoorState.Closed, DoorActor.Script);
            }
        }

        private void UpdateAutoClose()
        {
            if (_autoCloseTimer < 0f)
            {
                return;
            }

            _autoCloseTimer -= Time.deltaTime;

            if (_autoCloseTimer > 0f)
            {
                return;
            }

            _autoCloseTimer = -1f;
            Close(DoorActor.Script);
        }

        private void ApplyHingeRotation()
        {
            hinge.localRotation = _hingeClosedRotation * Quaternion.Euler(0f, _currentAngle, 0f);
        }

        // ------------------------------------------------------------------
        // API publique
        // ------------------------------------------------------------------

        /// <summary>Ouvre la porte. Sans effet si elle est verrouillee et que force est faux.</summary>
        public void Open(DoorActor actor = DoorActor.Script, Transform instigator = null)
        {
            Open(actor, instigator, false);
        }

        /// <summary>Ouvre la porte en ignorant le verrou (creature, evenement scripte).</summary>
        public void ForceOpen(DoorActor actor = DoorActor.Creature, Transform instigator = null)
        {
            Open(actor, instigator, true);
        }

        private void Open(DoorActor actor, Transform instigator, bool force)
        {
            if (_state == DoorState.Broken)
            {
                return;
            }

            if (_isLocked && !force)
            {
                return;
            }

            if (_state == DoorState.Open || _state == DoorState.Opening)
            {
                return;
            }

            // Sens d'ouverture : toujours a l'oppose de celui qui pousse,
            // pour que la porte ne balaye jamais le joueur.
            _signedOpenAngle = ComputeSignedOpenAngle(instigator);
            _targetAngle = _signedOpenAngle;
            _autoCloseTimer = -1f;

            SetState(DoorState.Opening, actor);

            PlayClip(openClip);
            EmitNoise(noiseRadius, NoiseType.Door);

            OnOpened(actor);
        }

        /// <summary>Referme la porte.</summary>
        public void Close(DoorActor actor = DoorActor.Script)
        {
            if (_state == DoorState.Broken || _state == DoorState.Closed || _state == DoorState.Closing)
            {
                return;
            }

            _targetAngle = 0f;
            _autoCloseTimer = -1f;

            SetState(DoorState.Closing, actor);

            PlayClip(closeClip);
            EmitNoise(noiseRadius * 0.8f, NoiseType.Door);

            OnClosed(actor);
        }

        /// <summary>Referme violemment la porte : bruit fort, vitesse doublee.</summary>
        public void Slam(DoorActor actor = DoorActor.HorrorEvent)
        {
            if (_state == DoorState.Broken)
            {
                return;
            }

            // Le claquement est instantane : pas d'animation progressive.
            _targetAngle = 0f;
            _currentAngle = 0f;
            _autoCloseTimer = -1f;

            ApplyHingeRotation();
            SetState(DoorState.Closed, actor);

            PlayClip(slamClip != null ? slamClip : closeClip, 1f);
            EmitNoise(slamNoiseRadius, NoiseType.Door);

            OnSlammed(actor);
        }

        /// <summary>Bascule ouvert / ferme.</summary>
        public void Toggle(DoorActor actor = DoorActor.Script, Transform instigator = null)
        {
            if (IsOpen)
            {
                if (canBeClosed)
                {
                    Close(actor);
                }

                return;
            }

            Open(actor, instigator);
        }

        /// <summary>Verrouille la porte.</summary>
        public void Lock()
        {
            if (_isLocked)
            {
                return;
            }

            _isLocked = true;
            EventBus.Publish(new DoorLockChangedEvent(this, true));
        }

        /// <summary>Deverrouille la porte (cle, objectif, evenement).</summary>
        public void Unlock()
        {
            if (!_isLocked)
            {
                return;
            }

            _isLocked = false;
            EventBus.Publish(new DoorLockChangedEvent(this, false));
            OnUnlocked();
        }

        // ------------------------------------------------------------------
        // Interaction
        // ------------------------------------------------------------------

        public override bool CanInteract(PlayerCharacter player)
        {
            if (!base.CanInteract(player))
            {
                return false;
            }

            if (_state == DoorState.Broken)
            {
                return false;
            }

            if (IsOpen && !canBeClosed)
            {
                return false;
            }

            if (_isLocked)
            {
                return CanUnlockWith(player);
            }

            return true;
        }

        public override string GetTitle(PlayerCharacter player)
        {
            // Une porte propose plusieurs actions selon son etat (Ouvrir, Fermer,
            // Deverrouiller, Verrouillee...). Sans titre, "Fermer" tout seul est
            // ambigu : on nomme donc toujours l'objet.
            string title = base.GetTitle(player);

            return string.IsNullOrEmpty(title) ? "Porte" : title;
        }

        public override string GetPrompt(PlayerCharacter player)
        {
            if (_state == DoorState.Broken)
            {
                return string.Empty;
            }

            if (_isLocked)
            {
                return CanUnlockWith(player) ? GetUnlockPrompt(player) : GetLockedPrompt(player);
            }

            if (IsOpen)
            {
                return canBeClosed ? closePromptText : string.Empty;
            }

            return openPromptText;
        }

        protected override void OnInteracted(PlayerCharacter player)
        {
            if (_isLocked)
            {
                if (!TryUnlockWith(player))
                {
                    return;
                }

                Unlock();
                Open(DoorActor.Player, player != null ? player.transform : null);
                return;
            }

            Toggle(DoorActor.Player, player != null ? player.transform : null);
        }

        protected override void OnInteractionRefused(PlayerCharacter player)
        {
            if (_isLocked)
            {
                // Poignee qui resiste : le joueur comprend que la porte existe
                // mais qu'il lui manque quelque chose.
                PlayClip(lockedClip);
                EmitNoise(noiseRadius * 0.5f, NoiseType.Door);
            }
        }

        // ------------------------------------------------------------------
        // Points d'extension
        // ------------------------------------------------------------------

        /// <summary>Vrai si ce joueur possede de quoi deverrouiller (sans consommer).</summary>
        protected virtual bool CanUnlockWith(PlayerCharacter player)
        {
            return false;
        }

        /// <summary>Tente le deverrouillage, en consommant ce qu'il faut. Appele uniquement si CanUnlockWith est vrai.</summary>
        protected virtual bool TryUnlockWith(PlayerCharacter player)
        {
            return false;
        }

        /// <summary>Texte quand la porte est verrouillee et que le joueur ne peut rien faire.</summary>
        protected virtual string GetLockedPrompt(PlayerCharacter player)
        {
            return lockedPromptText;
        }

        /// <summary>Texte quand le joueur peut deverrouiller.</summary>
        protected virtual string GetUnlockPrompt(PlayerCharacter player)
        {
            return "Deverrouiller";
        }

        protected virtual void OnOpened(DoorActor actor) { }
        protected virtual void OnClosed(DoorActor actor) { }
        protected virtual void OnSlammed(DoorActor actor) { }
        protected virtual void OnUnlocked() { }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        protected void SetState(DoorState next, DoorActor actor)
        {
            if (_state == next)
            {
                return;
            }

            DoorState previous = _state;
            _state = next;

            EventBus.Publish(new DoorStateChangedEvent(this, previous, next, actor));
        }

        /// <summary>
        /// Place instantanement la porte a un angle donne, sans animation.
        /// Utilise par les portes qui cedent ou par les evenements scriptes.
        /// </summary>
        protected void SetAngleImmediate(float signedAngle)
        {
            _currentAngle = signedAngle;
            _targetAngle = signedAngle;
            _autoCloseTimer = -1f;
            ApplyHingeRotation();
        }

        /// <summary>
        /// Deplace instantanement le battant puis le laisse revenir a sa position
        /// fermee : donne la secousse d'une porte qu'on frappe de l'autre cote.
        /// </summary>
        protected void ShakeTo(float signedAngle)
        {
            _currentAngle = signedAngle;
            _targetAngle = 0f;
            _autoCloseTimer = -1f;
            ApplyHingeRotation();
        }

        /// <summary>Marque la porte comme detruite : plus d'interaction, passage libre.</summary>
        protected void MarkBroken(DoorActor actor)
        {
            SetState(DoorState.Broken, actor);
            EventBus.Publish(new DoorBrokenEvent(this, transform.position, actor));
        }

        private float ComputeSignedOpenAngle(Transform instigator)
        {
            if (!openAwayFromInstigator || instigator == null)
            {
                return openAngle;
            }

            Vector3 toInstigator = instigator.position - transform.position;
            float side = Vector3.Dot(transform.forward, toInstigator);

            // Si l'instigateur est devant, la porte part en arriere, et inversement.
            return side >= 0f ? -openAngle : openAngle;
        }

        protected void PlayClip(AudioClip clip, float volume = 0.85f)
        {
            if (clip == null)
            {
                return;
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                // Pas de source dediee : lecture ponctuelle a la position de la porte.
                AudioSource.PlayClipAtPoint(clip, transform.position, volume);
                return;
            }

            audioSource.PlayOneShot(clip, volume);
        }

        protected void EmitNoise(float radius, NoiseType type)
        {
            Noise.Emit(transform.position, radius, type, gameObject);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Visualise le sens d'ouverture dans la scene.
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.8f);

            if (hinge != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(hinge.position, 0.08f);
            }
        }
#endif
    }
}
