using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Level;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Les trois pouvoirs du demon (touches 1, 2, 3 de la rangee du haut) :
    ///   1 - Vision : pendant 5 s, voit les joueurs a travers le decor (contour lumineux),
    ///       leur distance a l'ecran et leur position exacte sur la carte 3D.
    ///   2 - Cri : hurlement entendu de tres loin (bruit "Monster" emis pour l'IA / les
    ///       joueurs) et course ultra rapide pendant 5 s.
    ///   3 - Teleportation : apparait a 10 m d'un joueur, a un endroit libre au hasard.
    /// Durees, temps de recharge, sons et couleurs se reglent dans l'Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class DemonPowers : MonoBehaviour
    {
        public enum TargetChoice { Random, Nearest }

        [Header("References")]
        [SerializeField] private DemonController controller;
        [Tooltip("Source 3D portant le cri (grande portee).")]
        [SerializeField] private AudioSource screamSource;
        [Tooltip("Source des effets (vision, teleportation).")]
        [SerializeField] private AudioSource sfxSource;

        [Header("1 - Vision")]
        [SerializeField] private bool visionEnabled = true;
        [SerializeField] private string visionBinding = "<Keyboard>/1";
        [SerializeField, Min(0.1f)] private float visionDuration = 5f;
        [SerializeField, Min(0f)] private float visionCooldown = 20f;
        [SerializeField, ColorUsage(true, true)] private Color outlineColor = new Color(3f, 0.3f, 0.12f, 1f);
        [SerializeField] private Color fillColor = new Color(1f, 0.1f, 0.05f, 0.18f);
        [Tooltip("Epaisseur du contour pres de la cible (m) ; il s'epaissit avec la distance.")]
        [SerializeField, Min(0f)] private float outlineWidth = 0.025f;
        [SerializeField] private bool showScreenMarkers = true;
        [SerializeField] private bool revealOnMap = true;
        [SerializeField] private Material revealMaskMaterial;
        [SerializeField] private Material revealOutlineMaterial;
        [SerializeField] private AudioClip visionSound;

        [Header("2 - Cri")]
        [SerializeField] private bool screamEnabled = true;
        [SerializeField] private string screamBinding = "<Keyboard>/2";
        [Tooltip("Duree de la course ultra rapide (s).")]
        [SerializeField, Min(0.1f)] private float screamDuration = 5f;
        [SerializeField, Min(0f)] private float screamCooldown = 25f;
        [SerializeField, Min(1f)] private float screamSpeedMultiplier = 2.2f;
        [SerializeField] private AudioClip screamClip;
        [SerializeField, Range(0f, 1f)] private float screamVolume = 1f;
        [Tooltip("Distance a laquelle le cri s'entend encore (m).")]
        [SerializeField, Min(1f)] private float screamAudibleDistance = 300f;
        [Tooltip("Rayon du bruit emis pour l'IA et les autres systemes (Noise.Emit).")]
        [SerializeField, Min(0f)] private float screamNoiseRadius = 250f;
        [SerializeField, Min(0f)] private float screamCameraShake = 0.12f;
        [SerializeField, Min(0f)] private float screamFovBoost = 12f;

        [Header("3 - Teleportation")]
        [SerializeField] private bool teleportEnabled = true;
        [SerializeField] private string teleportBinding = "<Keyboard>/3";
        [SerializeField, Min(0f)] private float teleportCooldown = 30f;
        [Tooltip("Distance au joueur vise (m).")]
        [SerializeField, Min(1f)] private float teleportDistance = 10f;
        [SerializeField] private TargetChoice teleportTarget = TargetChoice.Random;
        [Tooltip("Tourne le demon vers le joueur a l'arrivee.")]
        [SerializeField] private bool faceTargetOnArrival = true;
        [SerializeField, Range(4, 64)] private int teleportAttempts = 32;
        [SerializeField] private AudioClip teleportDepartSound;
        [SerializeField] private AudioClip teleportArriveSound;
        [SerializeField, Min(0f)] private float teleportFlash = 0.45f;

        [Header("HUD")]
        [SerializeField] private bool showHud = true;

        private class Power
        {
            public string key;
            public string name;
            public float cooldown;
            public float duration;
            public float readyAt;
            public float activeUntil;
            public InputAction action;
            public bool pending;
        }

        private Power _vision;
        private Power _scream;
        private Power _teleport;
        private readonly List<PlayerCharacter> _targets = new List<PlayerCharacter>();
        private float _flashUntil;
        private string _message;
        private float _messageUntil;
        private ForestMap3D _map;

        private GUIStyle _keyStyle;
        private GUIStyle _nameStyle;
        private GUIStyle _stateStyle;
        private GUIStyle _markerStyle;
        private GUIStyle _messageStyle;

        public bool VisionActive { get { return _vision != null && Time.time < _vision.activeUntil; } }
        public bool ScreamActive { get { return _scream != null && Time.time < _scream.activeUntil; } }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<DemonController>();

            if (revealMaskMaterial == null) revealMaskMaterial = new Material(Shader.Find("HouseOfSilence/RevealMask"));
            if (revealOutlineMaterial == null) revealOutlineMaterial = new Material(Shader.Find("HouseOfSilence/RevealOutline"));
        }

        private void OnEnable()
        {
            _vision = CreatePower("1", "Vision", visionBinding, visionCooldown, visionDuration);
            _scream = CreatePower("2", "Cri", screamBinding, screamCooldown, screamDuration);
            _teleport = CreatePower("3", "Teleport", teleportBinding, teleportCooldown, 0f);
        }

        private void OnDisable()
        {
            foreach (Power p in new[] { _vision, _scream, _teleport })
            {
                if (p != null && p.action != null)
                {
                    p.action.Disable();
                    p.action.Dispose();
                    p.action = null;
                }
            }

            if (controller != null) controller.SpeedMultiplier = 1f;
        }

        private Power CreatePower(string key, string name, string binding, float cooldown, float duration)
        {
            Power p = new Power { key = key, name = name, cooldown = cooldown, duration = duration };
            p.action = new InputAction("Demon" + name, InputActionType.Button, binding);
            p.action.performed += _ => p.pending = true;
            p.action.Enable();
            return p;
        }

        private void Update()
        {
            // Les reglages restent modifiables en jeu depuis l'Inspector.
            _vision.cooldown = visionCooldown; _vision.duration = visionDuration;
            _scream.cooldown = screamCooldown; _scream.duration = screamDuration;
            _teleport.cooldown = teleportCooldown;

            bool allowed = controller != null && controller.InputAllowed;

            if (Consume(_vision, allowed) && visionEnabled) TryActivate(_vision, ActivateVision);
            if (Consume(_scream, allowed) && screamEnabled) TryActivate(_scream, ActivateScream);
            if (Consume(_teleport, allowed) && teleportEnabled) TryActivate(_teleport, ActivateTeleport);

            // Course ultra rapide tant que le cri dure.
            if (controller != null)
            {
                controller.SpeedMultiplier = ScreamActive ? screamSpeedMultiplier : 1f;
                if (ScreamActive) controller.KickFov(screamFovBoost * 0.6f);
            }
        }

        private static bool Consume(Power p, bool allowed)
        {
            bool pressed = p.pending;
            p.pending = false;
            return pressed && allowed;
        }

        private void TryActivate(Power p, System.Func<bool> activate)
        {
            if (Time.time < p.readyAt)
            {
                ShowMessage(p.name + " : encore " + Mathf.CeilToInt(p.readyAt - Time.time) + " s");
                return;
            }

            if (activate())
            {
                p.activeUntil = Time.time + p.duration;
                p.readyAt = Time.time + p.cooldown;
            }
        }

        private List<PlayerCharacter> FindTargets()
        {
            _targets.Clear();

            foreach (PlayerCharacter p in FindObjectsByType<PlayerCharacter>())
            {
                if (p != null && p.isActiveAndEnabled && !p.transform.IsChildOf(transform))
                {
                    _targets.Add(p);
                }
            }

            return _targets;
        }

        // ------------------------------------------------------------------
        // 1 - Vision
        // ------------------------------------------------------------------

        private bool ActivateVision()
        {
            List<PlayerCharacter> targets = FindTargets();

            if (targets.Count == 0)
            {
                ShowMessage("Aucun joueur a reveler");
                return false;
            }

            if (_map == null) _map = FindAnyObjectByType<ForestMap3D>();

            foreach (PlayerCharacter t in targets)
            {
                RevealOutline.Show(t.gameObject, visionDuration, outlineColor, fillColor, outlineWidth, revealMaskMaterial, revealOutlineMaterial);
                if (revealOnMap && _map != null) _map.Reveal(t.transform, visionDuration);
            }

            PlaySfx(visionSound, 0.8f);
            return true;
        }

        // ------------------------------------------------------------------
        // 2 - Cri
        // ------------------------------------------------------------------

        private bool ActivateScream()
        {
            if (screamSource != null && screamClip != null)
            {
                screamSource.maxDistance = screamAudibleDistance;
                screamSource.PlayOneShot(screamClip, screamVolume);
            }

            Noise.Emit(transform.position, screamNoiseRadius, NoiseType.Monster, gameObject);

            if (controller != null)
            {
                controller.Shake(screamCameraShake, 1.2f);
                controller.KickFov(screamFovBoost);
            }

            return true;
        }

        // ------------------------------------------------------------------
        // 3 - Teleportation
        // ------------------------------------------------------------------

        private bool ActivateTeleport()
        {
            List<PlayerCharacter> targets = FindTargets();

            if (targets.Count == 0)
            {
                ShowMessage("Aucun joueur vers qui se teleporter");
                return false;
            }

            PlayerCharacter target = teleportTarget == TargetChoice.Random ? targets[Random.Range(0, targets.Count)] : Nearest(targets);
            Vector3 point;

            if (!FindTeleportPoint(target.transform.position, out point))
            {
                ShowMessage("Pas de place libre autour du joueur");
                return false;
            }

            PlayAt(teleportDepartSound, transform.position);

            Vector3 toTarget = target.transform.position - point;
            toTarget.y = 0f;
            Quaternion facing = faceTargetOnArrival && toTarget.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toTarget) : transform.rotation;
            controller.TeleportTo(point, facing);

            PlayAt(teleportArriveSound, point);
            controller.Shake(0.06f, 0.4f);
            _flashUntil = Time.time + teleportFlash;
            return true;
        }

        private PlayerCharacter Nearest(List<PlayerCharacter> targets)
        {
            PlayerCharacter best = targets[0];
            float bestDistance = float.MaxValue;

            foreach (PlayerCharacter t in targets)
            {
                float d = (t.transform.position - transform.position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = t; }
            }

            return best;
        }

        /// <summary>
        /// Point au sol a 'teleportDistance' du joueur, direction au hasard, avec la place
        /// pour la capsule du demon (pas dans un arbre, un mur ou sous un toit).
        /// </summary>
        private bool FindTeleportPoint(Vector3 center, out Vector3 point)
        {
            CharacterController cc = GetComponent<CharacterController>();
            float radius = cc != null ? cc.radius : 0.3f;
            float height = cc != null ? cc.height : 1.1f;

            for (int i = 0; i < teleportAttempts; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * teleportDistance;

                // Sol le plus proche de la hauteur du joueur (pas le toit d'un batiment).
                RaycastHit[] hits = Physics.RaycastAll(candidate + Vector3.up * 40f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
                bool found = false;
                float bestGap = float.MaxValue;
                Vector3 ground = candidate;

                foreach (RaycastHit h in hits)
                {
                    if (h.normal.y < 0.6f || h.collider.transform.IsChildOf(transform)) continue;
                    float gap = Mathf.Abs(h.point.y - center.y);
                    if (gap < bestGap) { bestGap = gap; ground = h.point; found = true; }
                }

                if (!found || bestGap > 6f) continue;

                Vector3 bottom = ground + Vector3.up * (radius + 0.05f);
                Vector3 top = ground + Vector3.up * Mathf.Max(radius + 0.06f, height - radius);
                bool blocked = false;

                foreach (Collider c in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!c.transform.IsChildOf(transform)) { blocked = true; break; }
                }

                if (blocked) continue;

                point = ground + Vector3.up * 0.05f;
                return true;
            }

            point = center;
            return false;
        }

        // ------------------------------------------------------------------

        private void PlaySfx(AudioClip clip, float volume)
        {
            if (clip == null) return;
            if (sfxSource != null) sfxSource.PlayOneShot(clip, volume);
            else AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }

        private static void PlayAt(AudioClip clip, Vector3 position)
        {
            if (clip != null) AudioSource.PlayClipAtPoint(clip, position, 1f);
        }

        private void ShowMessage(string text)
        {
            _message = text;
            _messageUntil = Time.time + 2f;
        }

        // ------------------------------------------------------------------
        // HUD (IMGUI, comme le reste du prototype)
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (controller == null || !controller.IsControlled || !controller.InputAllowed || SurvivorJumpscare.AnyPlaying)
            {
                return;
            }

            EnsureStyles();
            float scale = Screen.height / 1080f;
            Color previous = GUI.color;

            if (_flashUntil > Time.time && teleportFlash > 0f)
            {
                float a = (_flashUntil - Time.time) / teleportFlash;
                GUI.color = new Color(0.25f, 0f, 0f, a * 0.85f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            }

            if (VisionActive && showScreenMarkers)
            {
                DrawTargetMarkers(scale);
            }

            if (showHud)
            {
                DrawPowers(scale);
            }

            if (!string.IsNullOrEmpty(_message) && Time.time < _messageUntil)
            {
                _messageStyle.fontSize = Mathf.RoundToInt(18f * scale);
                _messageStyle.normal.textColor = new Color(1f, 0.75f, 0.6f, Mathf.Clamp01(_messageUntil - Time.time));
                GUI.Label(new Rect(0f, Screen.height - 230f * scale, Screen.width, 30f * scale), _message, _messageStyle);
            }

            GUI.color = previous;
        }

        private void DrawPowers(float scale)
        {
            float w = 150f * scale, h = 64f * scale, gap = 12f * scale;
            float x = (Screen.width - (w * 3f + gap * 2f)) * 0.5f;
            float y = Screen.height - h - 36f * scale;

            _keyStyle.fontSize = Mathf.RoundToInt(22f * scale);
            _nameStyle.fontSize = Mathf.RoundToInt(15f * scale);
            _stateStyle.fontSize = Mathf.RoundToInt(13f * scale);

            Power[] powers = { _vision, _scream, _teleport };
            bool[] enabled = { visionEnabled, screamEnabled, teleportEnabled };

            for (int i = 0; i < 3; i++)
            {
                Power p = powers[i];
                Rect box = new Rect(x + i * (w + gap), y, w, h);
                bool active = Time.time < p.activeUntil;
                bool ready = Time.time >= p.readyAt;

                GUI.color = active ? new Color(0.55f, 0.05f, 0.02f, 0.75f) : new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);

                // Barre de recharge / de duree en bas du cadre.
                float fill = active ? (p.activeUntil - Time.time) / Mathf.Max(0.01f, p.duration)
                                    : ready ? 1f : 1f - (p.readyAt - Time.time) / Mathf.Max(0.01f, p.cooldown);
                GUI.color = active ? new Color(1f, 0.35f, 0.15f, 0.95f) : ready ? new Color(0.9f, 0.2f, 0.1f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.7f);
                GUI.DrawTexture(new Rect(box.x, box.yMax - 4f * scale, box.width * Mathf.Clamp01(fill), 4f * scale), Texture2D.whiteTexture);

                GUI.color = Color.white;
                float alpha = enabled[i] ? 1f : 0.35f;
                _keyStyle.normal.textColor = new Color(1f, 0.85f, 0.7f, alpha);
                GUI.Label(new Rect(box.x + 8f * scale, box.y, 30f * scale, box.height), p.key, _keyStyle);

                _nameStyle.normal.textColor = new Color(0.95f, 0.9f, 0.85f, alpha);
                GUI.Label(new Rect(box.x + 40f * scale, box.y + 8f * scale, box.width - 44f * scale, 22f * scale), p.name, _nameStyle);

                string state = !enabled[i] ? "desactive" : active ? "ACTIF  " + (p.activeUntil - Time.time).ToString("0.0") + " s" : ready ? "pret" : Mathf.CeilToInt(p.readyAt - Time.time) + " s";
                _stateStyle.normal.textColor = active ? new Color(1f, 0.6f, 0.4f, 1f) : ready ? new Color(0.7f, 1f, 0.7f, alpha) : new Color(0.7f, 0.7f, 0.7f, 1f);
                GUI.Label(new Rect(box.x + 40f * scale, box.y + 32f * scale, box.width - 44f * scale, 20f * scale), state, _stateStyle);
            }
        }

        /// <summary>Losange + distance sur chaque joueur ; colle au bord de l'ecran s'il est hors champ.</summary>
        private void DrawTargetMarkers(float scale)
        {
            Camera cam = controller.Camera;
            if (cam == null) return;

            _markerStyle.fontSize = Mathf.RoundToInt(15f * scale);
            float margin = 40f * scale;
            float size = 14f * scale;

            foreach (PlayerCharacter t in _targets)
            {
                if (t == null) continue;

                Vector3 world = t.transform.position + Vector3.up * 2.1f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool behind = sp.z < 0f;
                if (behind) sp = -sp;

                Vector2 p = new Vector2(sp.x, Screen.height - sp.y);
                bool offscreen = behind || p.x < margin || p.x > Screen.width - margin || p.y < margin || p.y > Screen.height - margin;

                if (offscreen)
                {
                    // Projete sur le bord, dans la direction du joueur.
                    Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
                    Vector2 dir = (p - center);
                    if (dir.sqrMagnitude < 0.01f) dir = Vector2.up;
                    float k = Mathf.Min((center.x - margin) / Mathf.Abs(dir.x + 1e-4f), (center.y - margin) / Mathf.Abs(dir.y + 1e-4f));
                    p = center + dir * k;
                }

                float pulse = 1f + 0.2f * Mathf.Sin(Time.time * 10f);
                GUI.color = new Color(1f, 0.2f, 0.1f, 0.95f);
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, p);
                GUI.DrawTexture(new Rect(p.x - size * 0.5f * pulse, p.y - size * 0.5f * pulse, size * pulse, size * pulse), Texture2D.whiteTexture);
                GUI.matrix = m;

                // Distance au-dessus du losange (sous lui, elle se perdrait dans la silhouette rouge).
                float distance = Vector3.Distance(transform.position, t.transform.position);
                string label = Mathf.RoundToInt(distance) + " m";
                Rect rect = new Rect(p.x - 80f * scale, p.y - size - 24f * scale, 160f * scale, 22f * scale);
                GUI.color = Color.white;
                _markerStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, _markerStyle);
                _markerStyle.normal.textColor = new Color(1f, 0.6f, 0.5f, 1f);
                GUI.Label(rect, label, _markerStyle);
            }
        }

        private void EnsureStyles()
        {
            if (_keyStyle != null) return;

            _keyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold };
            _stateStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft };
            _markerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            _messageStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
