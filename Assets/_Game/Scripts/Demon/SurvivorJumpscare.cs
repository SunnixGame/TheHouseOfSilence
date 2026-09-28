using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Jumpscare de mort, vu par les yeux du survivant (camera FPS) :
    ///  1. la vue se braque sur le demon qui bondit au visage (cri, flash, zoom) ;
    ///  2. etranglement : son visage emplit l'ecran, la vue tremble de plus en plus,
    ///     voile rouge qui pulse, battements de coeur qui ralentissent ;
    ///  3. la vue bascule et tombe au sol, fondu au noir, "VOUS ETES MORT".
    /// Pour les tests (on joue le demon), la camera du survivant prend la main le temps
    /// de la sequence puis la rend. Tout se regle dans l'Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class SurvivorJumpscare : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera view;
        [Tooltip("Corps du survivant : masque pendant la sequence (la camera est dedans).")]
        [SerializeField] private GameObject body;
        [Tooltip("Source 2D pour le cri et le coeur (sur la camera).")]
        [SerializeField] private AudioSource audioSource;

        [Header("Lumiere")]
        [Tooltip("Lumiere froide collee a la camera : eclaire le visage du demon la nuit.")]
        [SerializeField] private Light faceLight;
        [SerializeField, Min(0f)] private float faceLightIntensity = 2.5f;
        [Tooltip("Scintillement de la lumiere pendant l'etranglement (0 = fixe).")]
        [SerializeField, Range(0f, 1f)] private float faceLightFlicker = 0.6f;

        [Header("Timing (s)")]
        [Tooltip("Le demon arrive au visage.")]
        [SerializeField, Min(0f)] private float impactTime = 0.45f;
        [Tooltip("Fin de l'etranglement : la vue tombe.")]
        [SerializeField, Min(0.1f)] private float strangleEnd = 3.3f;
        [SerializeField, Min(0.1f)] private float fallDuration = 0.9f;
        [SerializeField, Min(0f)] private float blackHold = 1.6f;

        [Header("Camera")]
        [Tooltip("Vitesse a laquelle la vue se braque sur le visage du demon.")]
        [SerializeField, Min(0.1f)] private float lookSpeed = 14f;
        [Tooltip("Champ de vision pendant l'etranglement (zoom sur le visage).")]
        [SerializeField, Range(20f, 90f)] private float strangleFov = 48f;
        [SerializeField] private Vector2 shakeRange = new Vector2(0.004f, 0.022f);
        [SerializeField, Min(0f)] private float rollShake = 6f;

        [Header("Sons")]
        [SerializeField] private AudioClip scream;
        [SerializeField, Range(0f, 1f)] private float screamVolume = 1f;
        [SerializeField] private AudioClip heartbeat;
        [SerializeField, Range(0f, 1f)] private float heartbeatVolume = 0.9f;
        [SerializeField] private Vector2 heartbeatPitch = new Vector2(1.5f, 0.55f);

        [Header("Ecran")]
        [SerializeField] private Color vignetteColor = new Color(0.55f, 0f, 0f, 1f);
        [SerializeField] private string deathText = "VOUS ETES MORT";

        private Texture2D _vignette;
        private float _vignetteAlpha;
        private float _flash;
        private float _black;
        private float _textAlpha;
        private GUIStyle _textStyle;
        private bool _playing;

        public bool IsPlaying { get { return _playing; } }

        /// <summary>Un jumpscare est en cours : le HUD et la vision nocturne du demon s'effacent.</summary>
        public static bool AnyPlaying { get; private set; }

        private void Awake()
        {
            if (view == null) view = GetComponentInChildren<Camera>(true);
        }

        /// <summary>Lance la sequence : 'face' = point a regarder (tete du demon).</summary>
        public void Play(Transform face, Camera cameraToRestore)
        {
            if (_playing || view == null || face == null) return;
            StartCoroutine(Sequence(face, cameraToRestore));
        }

        private IEnumerator Sequence(Transform face, Camera other)
        {
            _playing = true;
            AnyPlaying = true;

            // La camera du survivant prend la main (ecoute audio comprise).
            bool otherWasEnabled = other != null && other.enabled;
            AudioListener otherListener = other != null ? other.GetComponent<AudioListener>() : null;
            AudioListener ownListener = view.GetComponent<AudioListener>();
            bool viewWasEnabled = view.enabled;
            bool ownListenerWasEnabled = ownListener != null && ownListener.enabled;

            if (other != null) other.enabled = false;
            if (otherListener != null) otherListener.enabled = false;
            view.enabled = true;
            if (ownListener != null) ownListener.enabled = true;

            // Corps masque (la camera est a l'interieur), ombre gardee.
            Renderer[] bodyRenderers = body != null ? body.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            ShadowCastingMode[] shadowModes = new ShadowCastingMode[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                shadowModes[i] = bodyRenderers[i].shadowCastingMode;
                bodyRenderers[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            Transform cam = view.transform;
            Vector3 startLocalPos = cam.localPosition;
            Quaternion startLocalRot = cam.localRotation;
            float startFov = view.fieldOfView;

            if (audioSource != null && scream != null) audioSource.PlayOneShot(scream, screamVolume);
            if (faceLight != null) faceLight.enabled = true;

            float start = Time.time;
            float total = strangleEnd + fallDuration + blackHold;
            bool heartStarted = false;
            Quaternion look = cam.rotation;

            while (Time.time - start < total)
            {
                float t = Time.time - start;

                // Regard braque sur le visage du demon (sursaut au debut).
                Vector3 target = face.position;
                Quaternion wanted = Quaternion.LookRotation(target - cam.position, Vector3.up);
                look = Quaternion.Slerp(look, wanted, 1f - Mathf.Exp(-lookSpeed * Time.deltaTime));

                // Tremblement : fort a l'impact, croissant pendant l'etranglement.
                float strangle = Mathf.Clamp01((t - impactTime) / Mathf.Max(0.01f, strangleEnd - impactTime));
                float amplitude = t < impactTime ? shakeRange.x : Mathf.Lerp(shakeRange.x, shakeRange.y, strangle);
                float impactKick = Mathf.Exp(-Mathf.Max(0f, t - impactTime) * 6f) * (t >= impactTime ? 1f : 0f);
                amplitude += impactKick * 0.03f;
                Vector3 jitter = new Vector3(Mathf.PerlinNoise(t * 23f, 0.1f) - 0.5f, Mathf.PerlinNoise(0.7f, t * 21f) - 0.5f, 0f) * 2f * amplitude;
                float roll = (Mathf.PerlinNoise(t * 9f, 3.3f) - 0.5f) * 2f * rollShake * strangle;

                Quaternion rotation = look * Quaternion.Euler(jitter.y * 400f, jitter.x * 400f, roll);
                Vector3 localPos = startLocalPos;

                // Chute : la vue bascule sur le cote et descend au sol.
                float fall = Mathf.Clamp01((t - strangleEnd) / fallDuration);
                if (fall > 0f)
                {
                    float f = fall * fall;
                    rotation = rotation * Quaternion.Euler(-35f * f, 0f, 70f * f);
                    localPos = startLocalPos + Vector3.down * 1.35f * f;
                }

                cam.localPosition = localPos;
                cam.rotation = rotation;

                // Zoom sur le visage a l'impact.
                float fovTarget = t < impactTime ? startFov : strangleFov;
                view.fieldOfView = Mathf.Lerp(view.fieldOfView, fovTarget, 1f - Mathf.Exp(-8f * Time.deltaTime));

                // Lumiere sur le visage : forte a l'impact, scintille, s'eteint a la chute.
                if (faceLight != null)
                {
                    float flicker = 1f - faceLightFlicker * strangle * Mathf.PerlinNoise(t * 17f, 5.5f);
                    faceLight.intensity = faceLightIntensity * (1f + 1.5f * impactKick) * flicker * (1f - fall);
                }

                // Coeur qui ralentit pendant l'etranglement.
                if (!heartStarted && t >= impactTime && audioSource != null && heartbeat != null)
                {
                    heartStarted = true;
                    audioSource.clip = heartbeat;
                    audioSource.loop = true;
                    audioSource.volume = heartbeatVolume;
                    audioSource.Play();
                }

                if (heartStarted && audioSource != null)
                {
                    audioSource.pitch = Mathf.Lerp(heartbeatPitch.x, heartbeatPitch.y, strangle);
                    audioSource.volume = heartbeatVolume * (1f - fall);
                }

                // Ecran : flash a l'impact, voile rouge qui pulse, noir a la chute.
                _flash = t >= impactTime ? Mathf.Exp(-(t - impactTime) * 7f) : 0f;
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Lerp(8f, 3f, strangle));
                _vignetteAlpha = t >= impactTime ? Mathf.Lerp(0.35f, 0.95f, strangle) * (0.75f + 0.25f * pulse) : 0f;
                _black = Mathf.Max(strangle * 0.35f, fall);
                _textAlpha = Mathf.Clamp01((t - strangleEnd - fallDuration * 0.6f) / 0.6f);

                yield return null;
            }

            // Retour a la normale.
            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.loop = false;
                audioSource.pitch = 1f;
                audioSource.clip = null;
            }

            if (faceLight != null) faceLight.enabled = false;
            cam.localPosition = startLocalPos;
            cam.localRotation = startLocalRot;
            view.fieldOfView = startFov;
            for (int i = 0; i < bodyRenderers.Length; i++) if (bodyRenderers[i] != null) bodyRenderers[i].shadowCastingMode = shadowModes[i];

            view.enabled = viewWasEnabled;
            if (ownListener != null) ownListener.enabled = ownListenerWasEnabled;
            if (other != null) other.enabled = otherWasEnabled;
            if (otherListener != null) otherListener.enabled = otherWasEnabled;

            _vignetteAlpha = _flash = _black = _textAlpha = 0f;
            _playing = false;
            AnyPlaying = false;
        }

        private void OnGUI()
        {
            if (!_playing)
            {
                return;
            }

            if (_vignette == null) _vignette = BuildVignette();

            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            Color previous = GUI.color;

            if (_black > 0f)
            {
                GUI.color = new Color(0f, 0f, 0f, _black);
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
            }

            if (_vignetteAlpha > 0f)
            {
                GUI.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, _vignetteAlpha);
                GUI.DrawTexture(screen, _vignette);
            }

            if (_flash > 0.01f)
            {
                GUI.color = new Color(1f, 0.9f, 0.9f, _flash * 0.8f);
                GUI.DrawTexture(screen, Texture2D.whiteTexture);
            }

            if (_textAlpha > 0f)
            {
                if (_textStyle == null)
                {
                    _textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                }

                GUI.color = Color.white;
                _textStyle.fontSize = Mathf.RoundToInt(56f * Screen.height / 1080f);
                _textStyle.normal.textColor = new Color(0.65f, 0.02f, 0.02f, _textAlpha);
                GUI.Label(screen, deathText, _textStyle);
            }

            GUI.color = previous;
        }

        /// <summary>Degrade radial : transparent au centre, opaque sur les bords.</summary>
        private static Texture2D BuildVignette()
        {
            const int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.95f, d));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply();
            return tex;
        }

        private void OnDestroy()
        {
            if (_playing) AnyPlaying = false;
            if (_vignette != null) Destroy(_vignette);
        }
    }
}
