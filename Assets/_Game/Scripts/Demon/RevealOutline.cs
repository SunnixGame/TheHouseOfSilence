using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HouseOfSilence.Demon
{
    /// <summary>
    /// Contour lumineux visible a travers les murs, pose temporairement sur une cible
    /// (pouvoir Vision du demon). Chaque Renderer de la cible est double par deux
    /// copies partageant son mesh (et ses os pour un SkinnedMeshRenderer) : une passe
    /// masque (RevealMask) et une passe contour (RevealOutline). Tout est detruit a la fin.
    /// </summary>
    public class RevealOutline : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int WidthId = Shader.PropertyToID("_Width");

        private readonly List<Renderer> _maskRenderers = new List<Renderer>();
        private readonly List<Renderer> _outlineRenderers = new List<Renderer>();
        private readonly List<GameObject> _created = new List<GameObject>();

        private MaterialPropertyBlock _block;
        private float _start;
        private float _end;
        private Color _color;
        private Color _fill;
        private float _width;
        private float _fade = 0.35f;

        public float Remaining { get { return Mathf.Max(0f, _end - Time.time); } }

        /// <summary>Affiche (ou prolonge) le contour sur la cible.</summary>
        public static RevealOutline Show(GameObject target, float duration, Color color, Color fill, float width, Material mask, Material outline)
        {
            RevealOutline reveal = target.GetComponent<RevealOutline>();

            if (reveal == null)
            {
                reveal = target.AddComponent<RevealOutline>();
                reveal.Build(mask, outline);
                reveal._start = Time.time;
            }

            reveal._end = Mathf.Max(reveal._end, Time.time + duration);
            reveal._color = color;
            reveal._fill = fill;
            reveal._width = width;
            return reveal;
        }

        private void Build(Material mask, Material outline)
        {
            _block = new MaterialPropertyBlock();

            foreach (Renderer source in GetComponentsInChildren<Renderer>())
            {
                if (!ShouldOutline(source)) continue;

                if (source is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null) continue;
                    _maskRenderers.Add(CopySkinned(skinned, mask, "RevealMask"));
                    _outlineRenderers.Add(CopySkinned(skinned, outline, "RevealOutline"));
                }
                else if (source is MeshRenderer meshRenderer)
                {
                    MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    _maskRenderers.Add(CopyMesh(meshRenderer, filter.sharedMesh, mask, "RevealMask"));
                    _outlineRenderers.Add(CopyMesh(meshRenderer, filter.sharedMesh, outline, "RevealOutline"));
                }
            }
        }

        /// <summary>
        /// Seulement le corps visible : pas les rendus desactives, ni ce qui est accroche a
        /// une camera (quad plein ecran de l'effet VHS...), ni nos propres effets.
        /// </summary>
        private static bool ShouldOutline(Renderer source)
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy) return false;
            if (source.GetComponentInParent<Camera>(true) != null) return false;

            Material material = source.sharedMaterial;
            if (material != null && material.shader != null && material.shader.name.StartsWith("HouseOfSilence/")) return false;

            return true;
        }

        private GameObject NewChild(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            _created.Add(go);
            return go;
        }

        private Renderer CopyMesh(MeshRenderer source, Mesh mesh, Material material, string name)
        {
            GameObject go = NewChild(source.transform, name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            Setup(r, material, mesh.subMeshCount);
            return r;
        }

        private Renderer CopySkinned(SkinnedMeshRenderer source, Material material, string name)
        {
            GameObject go = NewChild(source.transform, name);
            SkinnedMeshRenderer r = go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = source.sharedMesh;
            r.bones = source.bones;
            r.rootBone = source.rootBone;
            r.updateWhenOffscreen = true;
            Setup(r, material, source.sharedMesh.subMeshCount);
            return r;
        }

        private static void Setup(Renderer r, Material material, int subMeshCount)
        {
            Material[] mats = new Material[Mathf.Max(1, subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
        }

        private void LateUpdate()
        {
            float now = Time.time;

            if (now >= _end)
            {
                Destroy(this);
                return;
            }

            // Apparition / disparition en fondu, leger battement du contour.
            float alpha = Mathf.Clamp01((now - _start) / _fade) * Mathf.Clamp01((_end - now) / _fade);
            float pulse = 1f + 0.25f * Mathf.Sin(now * 8f);

            Color outline = _color;
            outline.a *= alpha;
            Color fill = _fill;
            fill.a *= alpha;

            _block.Clear();
            _block.SetColor(FillColorId, fill);
            foreach (Renderer r in _maskRenderers) if (r != null) r.SetPropertyBlock(_block);

            _block.Clear();
            _block.SetColor(ColorId, outline);
            _block.SetFloat(WidthId, _width * pulse);
            foreach (Renderer r in _outlineRenderers) if (r != null) r.SetPropertyBlock(_block);
        }

        private void OnDestroy()
        {
            foreach (GameObject go in _created)
            {
                if (go != null) Destroy(go);
            }
        }
    }
}
