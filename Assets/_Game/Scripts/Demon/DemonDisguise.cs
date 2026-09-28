using System.Collections;
using System.Collections.Generic;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Deguisement du demon : il prend l'apparence d'un survivant (copie de son corps,
    /// animee par la marche / course du demon), yeux eteints, pendant 'duration'
    /// secondes. Ensuite il reprend sa forme avec une transformation : convulsions et
    /// scintillement entre les deux corps, eclair rouge, le faux corps s'effondre et la
    /// poupee surgit (grossit avec un leger rebond), secousse de camera.
    ///
    /// Le deguisement tombe aussitot si le demon tue (DemonKill) ou n'est plus joue.
    /// Lance par DemonPowers apres une teleportation ; ajoute automatiquement.
    /// </summary>
    [DisallowMultipleComponent]
    public class DemonDisguise : MonoBehaviour
    {
        [Header("Retour a la forme du demon")]
        [SerializeField, Min(0.2f)] private float revertDuration = 1.3f;
        [SerializeField] private AudioClip revertSound;
        [SerializeField, Range(0f, 1f)] private float revertVolume = 1f;
        [SerializeField] private Color flashColor = new Color(1f, 0.08f, 0.04f);
        [SerializeField, Min(0f)] private float flashIntensity = 12f;
        [SerializeField] private bool showTimer = true;

        private DemonController _controller;
        private DemonKill _kill;
        private DemonEyes _eyes;
        private AudioSource _audio;
        private Transform _model;
        private readonly List<Renderer> _demonRenderers = new List<Renderer>();

        private GameObject _disguise;
        private Animator _disguiseAnimator;
        private readonly List<Renderer> _disguiseRenderers = new List<Renderer>();
        private int _speedHash;
        private string _disguiseName;
        private float _until;
        private Coroutine _revert;
        private Vector3 _modelScale = Vector3.one;
        private GUIStyle _style;

        /// <summary>Le demon porte une apparence de survivant (retour en cours compris).</summary>
        public bool IsActive { get { return _disguise != null; } }

        private void Awake()
        {
            _controller = GetComponent<DemonController>();
            _kill = GetComponent<DemonKill>();
            _eyes = GetComponent<DemonEyes>();
            _audio = GetComponent<AudioSource>();
            _speedHash = Animator.StringToHash("Speed");

            Animator animator = _controller != null ? GetComponentInChildren<Animator>(true) : null;
            _model = animator != null ? animator.transform : transform.Find("Model");
            if (_model != null) _modelScale = _model.localScale;
        }

        /// <summary>Prend l'apparence de 'source' pendant 'duration' secondes.</summary>
        public bool Begin(PlayerCharacter source, float duration, AudioClip fallbackSound)
        {
            Transform body = source != null ? source.transform.Find("SurvivorBody") : null;
            if (body == null || _model == null) return false;

            if (revertSound == null) revertSound = fallbackSound;
            EndImmediate();

            _disguise = Instantiate(body.gameObject, transform);
            _disguise.name = "Disguise_" + source.DisplayName;
            _disguise.transform.localPosition = Vector3.zero;
            _disguise.transform.localRotation = Quaternion.identity;
            _disguise.transform.localScale = body.localScale;
            Clean(_disguise);
            _disguise.SetActive(true);

            _disguiseAnimator = _disguise.GetComponent<Animator>();
            if (_disguiseAnimator != null)
            {
                _disguiseAnimator.enabled = true;
                _disguiseAnimator.applyRootMotion = false;
                _disguiseAnimator.Rebind();
            }

            _disguiseRenderers.Clear();
            _disguiseRenderers.AddRange(_disguise.GetComponentsInChildren<Renderer>(true));

            _demonRenderers.Clear();
            foreach (Renderer r in _model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.enabled) _demonRenderers.Add(r);
            }

            ShowDemon(false);
            _disguiseName = source.DisplayName;
            _until = Time.time + duration;
            return true;
        }

        /// <summary>Copie du corps : ni physique de ragdoll, ni contour de vision.</summary>
        private static void Clean(GameObject copy)
        {
            foreach (Joint j in copy.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);

            List<GameObject> reveal = new List<GameObject>();
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "RevealMask" || t.name == "RevealOutline") reveal.Add(t.gameObject);
            }

            foreach (GameObject g in reveal) DestroyImmediate(g);
        }

        private void Update()
        {
            if (!IsActive) return;

            // Tue ou change de personnage : le masque tombe d'un coup.
            if ((_kill != null && _kill.IsExecuting) || (_controller != null && !_controller.IsControlled))
            {
                EndImmediate();
                return;
            }

            if (_revert == null && Time.time >= _until)
            {
                _revert = StartCoroutine(Revert());
            }

            // Le faux corps marche et court comme le demon (0 arret, 1 marche, 2 course).
            if (_disguiseAnimator != null && _controller != null && _disguiseAnimator.isActiveAndEnabled)
            {
                float speed = _controller.CurrentSpeed;
                float walk = Mathf.Max(0.1f, _controller.WalkSpeed);
                float run = Mathf.Max(walk + 0.1f, _controller.RunSpeed);
                float normalized = speed <= walk ? speed / walk : Mathf.Min(2f, 1f + (speed - walk) / (run - walk));
                _disguiseAnimator.SetFloat(_speedHash, normalized, 0.12f, Time.deltaTime);
            }
        }

        private IEnumerator Revert()
        {
            if (_audio != null && revertSound != null) _audio.PlayOneShot(revertSound, revertVolume);

            GameObject lightObject = new GameObject("DisguiseFlash");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = Vector3.up * 1.1f;
            Light flash = lightObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = flashColor;
            flash.range = 7f;
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;

            Transform fake = _disguise.transform;
            Vector3 fakeScale = fake.localScale;
            float start = Time.time;
            bool shook = false;

            while (Time.time - start < revertDuration)
            {
                float t = (Time.time - start) / revertDuration;

                if (t < 0.45f)
                {
                    // Convulsions : le faux corps tremble, les deux formes se superposent par eclairs.
                    float k = t / 0.45f;
                    fake.localPosition = Random.insideUnitSphere * 0.035f * k;
                    fake.localRotation = Quaternion.Euler(0f, Random.Range(-10f, 10f) * k, Random.Range(-4f, 4f) * k);
                    bool glimpse = Random.value < 0.08f + 0.45f * k;
                    ShowDemon(glimpse);
                    ShowFake(!glimpse || Random.value < 0.5f);
                    flash.intensity = flashIntensity * 0.25f * k * Random.value;
                }
                else
                {
                    if (!shook && _controller != null)
                    {
                        shook = true;
                        _controller.Shake(0.09f, 0.6f);
                        _controller.KickFov(10f);
                    }

                    // Rupture : le faux corps s'ecrase, la poupee surgit avec un rebond.
                    float k = Mathf.Clamp01((t - 0.45f) / 0.55f);
                    float collapse = Mathf.Clamp01(k / 0.4f);
                    fake.localPosition = Vector3.zero;
                    fake.localRotation = Quaternion.identity;
                    fake.localScale = new Vector3(fakeScale.x * (1f + 0.4f * collapse), fakeScale.y * (1f - collapse), fakeScale.z * (1f + 0.4f * collapse));
                    ShowFake(collapse < 0.98f);
                    ShowDemon(true);

                    float grow = 1f - Mathf.Pow(1f - k, 3f);
                    float overshoot = Mathf.Sin(k * Mathf.PI) * 0.15f;
                    _model.localScale = _modelScale * (0.25f + 0.75f * grow + overshoot);
                    flash.intensity = flashIntensity * Mathf.Exp(-k * 4f);
                }

                yield return null;
            }

            Destroy(lightObject);
            _revert = null;
            EndImmediate();
        }

        /// <summary>Rend immediatement l'apparence du demon.</summary>
        public void EndImmediate()
        {
            if (_revert != null)
            {
                StopCoroutine(_revert);
                _revert = null;
                Transform flash = transform.Find("DisguiseFlash");
                if (flash != null) Destroy(flash.gameObject);
            }

            if (_disguise != null) Destroy(_disguise);
            _disguise = null;
            _disguiseAnimator = null;
            _disguiseRenderers.Clear();

            if (_model != null) _model.localScale = _modelScale;
            ShowDemon(true);
            _demonRenderers.Clear();
        }

        private void ShowDemon(bool visible)
        {
            foreach (Renderer r in _demonRenderers)
            {
                if (r != null) r.enabled = visible;
            }

            if (_eyes != null) _eyes.Hidden = !visible;
        }

        private void ShowFake(bool visible)
        {
            foreach (Renderer r in _disguiseRenderers)
            {
                if (r != null) r.enabled = visible;
            }
        }

        private void OnDisable()
        {
            EndImmediate();
        }

        private void OnGUI()
        {
            if (!showTimer || !IsActive || _revert != null || _controller == null || !_controller.IsControlled || SurvivorJumpscare.AnyPlaying)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
                _style.normal.textColor = new Color(1f, 0.75f, 0.6f, 0.95f);
            }

            int left = Mathf.CeilToInt(_until - Time.time);
            GUI.Label(new Rect(0f, Screen.height * 0.2f, Screen.width, 30f), "Apparence : " + _disguiseName + "   " + left + " s", _style);
        }
    }
}
