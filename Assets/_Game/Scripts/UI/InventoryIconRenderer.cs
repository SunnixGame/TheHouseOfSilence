using System.Collections.Generic;
using HouseOfSilence.Items;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HouseOfSilence.UI
{
    /// <summary>
    /// Rend le mesh des objets d'inventaire dans de petites textures, pour les
    /// afficher directement dans les cases du HUD (l'objet tourne lentement).
    ///
    /// Fonctionnement : un "plateau" invisible tres loin sous la scene, une
    /// camera orthographique qui ne sert qu'a ca, et une instance du World
    /// Prefab de chaque objet (debarrassee de sa physique et de ses scripts).
    /// Chaque frame, seuls les objets demandes par le HUD sont rendus : six
    /// petits rendus de 128 px au maximum, cout negligeable.
    ///
    /// Aucun layer a configurer : le plateau est hors de portee de toutes les
    /// autres cameras (far clip du joueur : 220 m, plateau a -4000 m).
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryIconRenderer : MonoBehaviour
    {
        private class Entry
        {
            public ItemData Item;
            public GameObject Instance;
            public RenderTexture Texture;
            public Bounds LocalBounds;

            /// <summary>Centre visuel dans le repere du prefab : l'objet tourne autour de lui.</summary>
            public Vector3 PivotOffset;
            public float Yaw;
            public bool Requested;
        }

        [Header("Rendu")]
        [SerializeField, Range(32, 512)] private int resolution = 128;
        [SerializeField, Range(0f, 180f)] private float rotationSpeed = 35f;
        [SerializeField] private Color background = new Color(0.04f, 0.04f, 0.05f, 1f);

        [Tooltip("Emplacement du plateau de rendu, loin de tout.")]
        [SerializeField] private Vector3 rigOrigin = new Vector3(0f, -4000f, 0f);

        [Header("Eclairage du plateau")]
        [SerializeField] private Color keyLightColor = new Color(1f, 0.93f, 0.8f);
        [SerializeField, Range(0f, 8f)] private float keyLightIntensity = 3.6f;

        private readonly Dictionary<ItemData, Entry> _entries = new Dictionary<ItemData, Entry>(8);
        private readonly List<Entry> _toRender = new List<Entry>(8);

        private Transform _rig;
        private Camera _camera;
        private bool _built;

        // ------------------------------------------------------------------

        private void Awake()
        {
            BuildRig();
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<ItemData, Entry> pair in _entries)
            {
                ReleaseEntry(pair.Value);
            }

            _entries.Clear();

            if (_rig != null)
            {
                Destroy(_rig.gameObject);
            }
        }

        private void BuildRig()
        {
            if (_built)
            {
                return;
            }

            GameObject rigObject = new GameObject("[InventoryPreviewRig]");
            rigObject.transform.position = rigOrigin;
            rigObject.hideFlags = HideFlags.DontSave;
            _rig = rigObject.transform;

            GameObject cameraObject = new GameObject("PreviewCamera");
            cameraObject.transform.SetParent(_rig, false);

            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false; // rendu manuel uniquement
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = background;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 20f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;

            UniversalAdditionalCameraData data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;

            // Vue en trois quarts, legerement plongeante : lisible pour tout objet.
            cameraObject.transform.localPosition = new Vector3(2.2f, 1.6f, -3.2f);
            cameraObject.transform.LookAt(_rig.position, Vector3.up);

            GameObject lightObject = new GameObject("KeyLight");
            lightObject.transform.SetParent(_rig, false);
            lightObject.transform.localPosition = new Vector3(1.5f, 2.5f, -2f);
            lightObject.transform.LookAt(_rig.position, Vector3.up);

            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Point;
            keyLight.color = keyLightColor;
            keyLight.intensity = keyLightIntensity;
            keyLight.range = 8f;
            keyLight.shadows = LightShadows.None;

            // Lumiere d'appoint froide de l'autre cote : les faces dans l'ombre restent lisibles.
            GameObject fillObject = new GameObject("FillLight");
            fillObject.transform.SetParent(_rig, false);
            fillObject.transform.localPosition = new Vector3(-2f, 0.5f, -1.5f);

            Light fillLight = fillObject.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.color = new Color(0.6f, 0.7f, 0.9f);
            fillLight.intensity = keyLightIntensity * 0.35f;
            fillLight.range = 8f;
            fillLight.shadows = LightShadows.None;

            _built = true;
        }

        // ------------------------------------------------------------------
        // API
        // ------------------------------------------------------------------

        /// <summary>
        /// Texture de previsualisation d'un objet. A appeler chaque frame ou l'on
        /// veut l'afficher : l'objet est alors rendu au LateUpdate suivant.
        /// Renvoie null si l'objet n'a pas de World Prefab.
        /// </summary>
        public Texture GetPreview(ItemData item)
        {
            if (item == null || item.WorldPrefab == null)
            {
                return null;
            }

            Entry entry;

            if (!_entries.TryGetValue(item, out entry))
            {
                entry = CreateEntry(item);

                if (entry == null)
                {
                    return null;
                }

                _entries.Add(item, entry);
            }

            entry.Requested = true;
            return entry.Texture;
        }

        private Entry CreateEntry(ItemData item)
        {
            BuildRig();

            GameObject instance = Instantiate(item.WorldPrefab, _rig);
            instance.name = "Preview_" + item.ItemName;
            instance.hideFlags = HideFlags.DontSave;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // On ne garde que le visuel : ni physique, ni interaction, ni scripts.
            Rigidbody[] bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++) Destroy(bodies[i]);

            MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++) Destroy(behaviours[i]);

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Destroy(colliders[i]);

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                Destroy(instance);
                return null;
            }

            // Bornes locales (l'instance est a l'identite) pour cadrer la camera.
            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            renderers[0].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // L'instance est a l'identite sur le plateau : le centre des bornes,
            // exprime par rapport au plateau, est le centre visuel du prefab.
            Vector3 pivotOffset = bounds.center - _rig.position;
            bounds.center = Vector3.zero;

            RenderTexture texture = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32);
            texture.name = "InventoryPreview_" + item.ItemName;
            texture.hideFlags = HideFlags.DontSave;
            texture.Create();

            Entry entry = new Entry();
            entry.Item = item;
            entry.Instance = instance;
            entry.Texture = texture;
            entry.LocalBounds = bounds;
            entry.PivotOffset = pivotOffset;
            entry.Yaw = Random.Range(0f, 360f);

            instance.SetActive(false);
            return entry;
        }

        private void ReleaseEntry(Entry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.Instance != null)
            {
                Destroy(entry.Instance);
            }

            if (entry.Texture != null)
            {
                entry.Texture.Release();
                Destroy(entry.Texture);
            }
        }

        // ------------------------------------------------------------------
        // Rendu
        // ------------------------------------------------------------------

        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            _toRender.Clear();

            foreach (KeyValuePair<ItemData, Entry> pair in _entries)
            {
                Entry entry = pair.Value;

                if (entry.Requested)
                {
                    _toRender.Add(entry);
                }

                entry.Requested = false;
            }

            float dt = Time.unscaledDeltaTime;

            for (int i = 0; i < _toRender.Count; i++)
            {
                Entry entry = _toRender[i];

                if (entry.Instance == null || entry.Texture == null)
                {
                    continue;
                }

                entry.Yaw += rotationSpeed * dt;

                // Rotation autour du centre visuel : le pivot du prefab est
                // compense pour que l'objet reste au milieu de la case.
                Quaternion rotation = Quaternion.Euler(0f, entry.Yaw, 0f);
                entry.Instance.transform.localRotation = rotation;
                entry.Instance.transform.localPosition = -(rotation * entry.PivotOffset);

                float radius = entry.LocalBounds.extents.magnitude;
                _camera.orthographicSize = Mathf.Max(0.02f, radius * 1.15f);

                entry.Instance.SetActive(true);

                _camera.targetTexture = entry.Texture;
                _camera.Render();
                _camera.targetTexture = null;

                entry.Instance.SetActive(false);
            }
        }
    }
}
