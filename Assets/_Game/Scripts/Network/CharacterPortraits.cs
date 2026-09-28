using System.Collections.Generic;
using HouseOfSilence.Demon;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HouseOfSilence.Network
{
    /// <summary>
    /// Portraits des personnages pour l'ecran de selection : une copie du modele (corps
    /// du survivant, poupee du demon) est posee sur un plateau cache tres loin sous la
    /// scene, en pose Idle, eclairee, et rendue une fois dans une texture (haut du corps).
    /// Meme principe que InventoryIconRenderer : aucune couche a configurer, le plateau
    /// est hors de portee des autres cameras.
    ///
    /// Les portraits sont gardes par nom de personnage : ils survivent aux rechargements.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterPortraits : MonoBehaviour
    {
        [SerializeField] private Vector2Int size = new Vector2Int(192, 240);
        [SerializeField] private Vector3 rigOrigin = new Vector3(0f, -4500f, 0f);
        [SerializeField] private Color background = new Color(0.07f, 0.03f, 0.03f, 1f);
        [SerializeField, Range(10f, 60f)] private float fieldOfView = 26f;
        [Tooltip("Part du haut du corps cadree (0.55 = tete et buste).")]
        [SerializeField, Range(0.3f, 1f)] private float framing = 0.55f;

        private readonly Dictionary<string, RenderTexture> _portraits = new Dictionary<string, RenderTexture>();
        private Transform _rig;
        private Camera _camera;

        /// <summary>Portrait du personnage (cree au premier appel), ou null s'il n'a pas de modele.</summary>
        public Texture Get(Component character)
        {
            if (character == null) return null;

            string key = Key(character);
            RenderTexture texture;
            if (_portraits.TryGetValue(key, out texture) && texture != null) return texture;

            texture = Render(character);
            _portraits[key] = texture;
            return texture;
        }

        public static string Key(Component character)
        {
            PlayerCharacter survivor = character as PlayerCharacter;
            return survivor != null ? survivor.DisplayName : "Demon";
        }

        private void OnDestroy()
        {
            foreach (RenderTexture t in _portraits.Values)
            {
                if (t != null) t.Release();
            }

            _portraits.Clear();
            if (_rig != null) Destroy(_rig.gameObject);
        }

        // ------------------------------------------------------------------

        private void BuildRig()
        {
            if (_rig != null) return;

            GameObject rig = new GameObject("[CharacterPortraitRig]");
            rig.transform.position = rigOrigin;
            rig.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(rig);
            _rig = rig.transform;

            GameObject cameraObject = new GameObject("PortraitCamera");
            cameraObject.transform.SetParent(_rig, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false; // rendu manuel
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = background;
            _camera.fieldOfView = fieldOfView;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 20f;
            _camera.allowMSAA = false;

            UniversalAdditionalCameraData data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            AddLight("KeyLight", new Vector3(1.2f, 2.2f, 2f), new Color(1f, 0.92f, 0.82f), 4f);
            AddLight("FillLight", new Vector3(-1.5f, 1.2f, 1.8f), new Color(0.55f, 0.65f, 0.95f), 1.6f);
            AddLight("RimLight", new Vector3(0f, 2.2f, -1.5f), new Color(1f, 0.3f, 0.2f), 2.5f);
        }

        private void AddLight(string name, Vector3 localPosition, Color color, float intensity)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_rig, false);
            go.transform.localPosition = localPosition;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 7f;
            light.shadows = LightShadows.None;
        }

        private RenderTexture Render(Component character)
        {
            GameObject model = VisualRoot(character);
            if (model == null) return null;

            BuildRig();

            // Copie en pose Idle, face a la camera, sans scripts ni physique.
            GameObject copy = Instantiate(model, _rig.position, Quaternion.identity);
            copy.transform.localScale = model.transform.lossyScale;
            copy.SetActive(true);
            Strip(copy);

            Animator animator = copy.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.enabled = true;
                animator.Rebind();
                animator.Update(0f);
            }

            // Cadrage : haut du corps.
            Bounds bounds = new Bounds(copy.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                if (any) bounds.Encapsulate(r.bounds);
                else bounds = r.bounds;
                any = true;
            }

            float height = Mathf.Max(0.5f, bounds.size.y);
            float frameHeight = height * framing;
            Vector3 focus = new Vector3(bounds.center.x, bounds.max.y - frameHeight * 0.5f, bounds.center.z);
            float distance = frameHeight * 0.5f / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;

            _camera.transform.position = focus + Vector3.forward * distance; // le modele regarde +Z
            _camera.transform.LookAt(focus, Vector3.up);

            RenderTexture texture = new RenderTexture(size.x, size.y, 16, RenderTextureFormat.ARGB32);
            texture.name = "Portrait_" + Key(character);
            _camera.aspect = (float)size.x / size.y;
            _camera.targetTexture = texture;
            _camera.Render();
            _camera.targetTexture = null;

            Destroy(copy);
            return texture;
        }

        /// <summary>Le modele visible : corps du survivant, ou modele de la poupee.</summary>
        private static GameObject VisualRoot(Component character)
        {
            if (character is DemonController)
            {
                Animator animator = character.GetComponentInChildren<Animator>(true);
                return animator != null ? animator.gameObject : null;
            }

            Transform body = character.transform.Find("SurvivorBody");
            return body != null ? body.gameObject : null;
        }

        private static void Strip(GameObject copy)
        {
            foreach (Joint j in copy.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (MonoBehaviour m in copy.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(m);
            foreach (AudioSource a in copy.GetComponentsInChildren<AudioSource>(true)) Destroy(a);
            foreach (Light l in copy.GetComponentsInChildren<Light>(true)) Destroy(l);
        }
    }
}
