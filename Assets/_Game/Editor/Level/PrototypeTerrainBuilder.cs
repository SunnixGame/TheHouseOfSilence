using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HouseOfSilence.EditorTools.Level
{
    /// <summary>
    /// Genere le terrain de la foret : relief, plateau pour la maison, chemin,
    /// clairiere, textures, arbres, herbe et buissons.
    ///
    /// Tout est deterministe (graine fixe) : relancer l'outil redonne la meme foret.
    /// </summary>
    public static class PrototypeTerrainBuilder
    {
        // --- Dimensions -------------------------------------------------------
        public const float TerrainSize = 300f;
        public const float TerrainHeight = 60f;

        /// <summary>Altitude du plateau ou se dresse la maison.</summary>
        public const float PlateauHeight = 13f;
        public const float PlateauRadius = 34f;

        /// <summary>Centre du terrain = centre de la maison.</summary>
        public static readonly Vector2 Center = new Vector2(TerrainSize * 0.5f, TerrainSize * 0.5f);

        /// <summary>Clairiere secondaire, au nord-est.</summary>
        private static readonly Vector2 ClearingCenter = new Vector2(218f, 205f);
        private const float ClearingRadius = 22f;

        /// <summary>Chemin en S depuis le bord sud jusqu'a la maison.</summary>
        private static readonly Vector2[] PathPoints =
        {
            new Vector2(140f, 0f),
            new Vector2(146f, 30f),
            new Vector2(160f, 60f),
            new Vector2(152f, 90f),
            new Vector2(150f, 118f)
        };

        private const float PathHalfWidth = 2.6f;
        private const int Seed = 1337;

        private const string TerrainDataPath = "Assets/_Game/Scenes/Prototype_House_Terrain.asset";
        private const string RttLayers = "Assets/ObjectiveEnvironment_Assets/Realistic Terrain Textures Lite/Terrain Layers/";
        private const string JpPrefabs = "Assets/JP Environmental Asset Pack/Prefabs/";

        // ------------------------------------------------------------------

        /// <summary>Construit le terrain complet et le renvoie.</summary>
        public static Terrain Build(Transform parent)
        {
            Random.InitState(Seed);

            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);

            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, TerrainDataPath);
            }

            data.heightmapResolution = 513;
            data.alphamapResolution = 512;
            data.baseMapResolution = 512;
            data.SetDetailResolution(128, 16);
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);

            BuildHeights(data);
            BuildLayers(data);
            BuildTrees(data);
            BuildDetails(data);

            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terrain_Forest";
            terrainObject.transform.SetParent(parent, false);
            terrainObject.transform.position = Vector3.zero;

            Terrain terrain = terrainObject.GetComponent<Terrain>();

            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;

            if (pipeline != null && pipeline.defaultTerrainMaterial != null)
            {
                terrain.materialTemplate = pipeline.defaultTerrainMaterial;
            }

            // La nuit et le brouillard cachent le lointain : inutile de dessiner loin.
            // Billboard >= tree distance : les arbres hors portee disparaissent dans
            // le brouillard au lieu de devenir des panneaux noirs (billboards non supportes).
            terrain.treeDistance = 110f;
            terrain.treeBillboardDistance = 110f;
            terrain.treeCrossFadeLength = 10f;
            terrain.treeMaximumFullLODCount = 60;
            terrain.detailObjectDistance = 55f;
            terrain.detailObjectDensity = 0.6f;
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 150f;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = ShadowCastingMode.TwoSided;

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            return terrain;
        }

        /// <summary>Altitude du terrain (en metres monde) a une position XZ.</summary>
        public static float SampleHeight(Terrain terrain, float x, float z)
        {
            return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        }

        // ------------------------------------------------------------------
        // Relief
        // ------------------------------------------------------------------

        private static void BuildHeights(TerrainData data)
        {
            int res = data.heightmapResolution;
            float[,] heights = new float[res, res];

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    // Coordonnees monde (attention : heights[y, x] = (z, x)).
                    float wx = (float)x / (res - 1) * TerrainSize;
                    float wz = (float)y / (res - 1) * TerrainSize;

                    float h = ForestHeight(wx, wz);
                    heights[y, x] = Mathf.Clamp01(h / TerrainHeight);
                }
            }

            data.SetHeights(0, 0, heights);
        }

        /// <summary>Hauteur monde de la foret, avant plateau et chemin.</summary>
        private static float RawHeight(float wx, float wz)
        {
            // Trois octaves de bruit : grandes ondulations, bosses, micro-relief.
            float n = 0f;
            n += Mathf.PerlinNoise(wx * 0.008f + 17.3f, wz * 0.008f + 42.1f) * 7f;
            n += Mathf.PerlinNoise(wx * 0.025f + 5.7f, wz * 0.025f + 9.9f) * 2.2f;
            n += Mathf.PerlinNoise(wx * 0.09f + 31f, wz * 0.09f + 3f) * 0.5f;

            // Legere montee vers le centre : la maison domine.
            float toCenter = Vector2.Distance(new Vector2(wx, wz), Center);
            n += Mathf.Clamp01(1f - toCenter / 150f) * 4f;

            return 2f + n;
        }

        private static float ForestHeight(float wx, float wz)
        {
            float h = RawHeight(wx, wz);

            // Plateau de la maison : parfaitement plat, fondu progressif sur 14 m.
            float toCenter = Vector2.Distance(new Vector2(wx, wz), Center);
            float plateau = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PlateauRadius, PlateauRadius + 14f, toCenter));
            h = Mathf.Lerp(h, PlateauHeight, plateau);

            // Clairiere : legerement aplanie.
            float toClearing = Vector2.Distance(new Vector2(wx, wz), ClearingCenter);
            float clearing = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClearingRadius * 0.5f, ClearingRadius, toClearing));
            float clearingHeight = RawHeight(ClearingCenter.x, ClearingCenter.y);
            h = Mathf.Lerp(h, clearingHeight, clearing * 0.7f);

            // Chemin : on lisse le relief sous le chemin pour qu'il soit praticable.
            float toPath = DistanceToPath(wx, wz);
            float path = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PathHalfWidth, PathHalfWidth + 5f, toPath));

            if (path > 0f)
            {
                float smoothed = SmoothedPathHeight(wx, wz);
                h = Mathf.Lerp(h, smoothed, path * 0.85f);
            }

            return h;
        }

        /// <summary>Hauteur moyenne le long du chemin : evite les marches.</summary>
        private static float SmoothedPathHeight(float wx, float wz)
        {
            float sum = 0f;
            int count = 0;

            for (int i = -2; i <= 2; i++)
            {
                for (int j = -2; j <= 2; j++)
                {
                    float sx = wx + i * 4f;
                    float sz = wz + j * 4f;

                    float raw = RawHeight(sx, sz);
                    float toCenter = Vector2.Distance(new Vector2(sx, sz), Center);
                    float plateau = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PlateauRadius, PlateauRadius + 14f, toCenter));

                    sum += Mathf.Lerp(raw, PlateauHeight, plateau);
                    count++;
                }
            }

            return sum / count;
        }

        /// <summary>Distance a la polyligne du chemin.</summary>
        public static float DistanceToPath(float wx, float wz)
        {
            Vector2 p = new Vector2(wx, wz);
            float best = float.MaxValue;

            for (int i = 0; i < PathPoints.Length - 1; i++)
            {
                Vector2 a = PathPoints[i];
                Vector2 b = PathPoints[i + 1];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(p, a + ab * t);

                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        /// <summary>Point de depart du joueur : sur le chemin, a 30 m au sud de la maison.</summary>
        public static Vector2 PlayerSpawnXZ { get { return new Vector2(152f, 92f); } }

        // ------------------------------------------------------------------
        // Textures
        // ------------------------------------------------------------------

        private const string LocalLayersFolder = "Assets/_Game/Scenes/TerrainLayers";

        /// <summary>
        /// Copie une couche du pack dans le projet et la matifie : les masques du
        /// pack donnent un sol tres brillant qui, sous une lune rasante, devient
        /// blanc comme de la neige. Le pack lui-meme n'est jamais modifie.
        /// </summary>
        private static TerrainLayer LocalLayer(string sourceName, string localName)
        {
            if (!AssetDatabase.IsValidFolder(LocalLayersFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game/Scenes", "TerrainLayers");
            }

            string localPath = LocalLayersFolder + "/" + localName + ".terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(localPath);

            if (layer == null)
            {
                if (!AssetDatabase.CopyAsset(RttLayers + sourceName + ".terrainlayer", localPath))
                {
                    return AssetDatabase.LoadAssetAtPath<TerrainLayer>(RttLayers + sourceName + ".terrainlayer");
                }

                layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(localPath);
            }

            if (layer != null)
            {
                layer.metallic = 0f;
                layer.smoothness = 0.08f;
                layer.maskMapRemapMin = Vector4.zero;
                layer.maskMapRemapMax = new Vector4(0f, 1f, 1f, 0.18f);
                EditorUtility.SetDirty(layer);
            }

            return layer;
        }

        private static void BuildLayers(TerrainData data)
        {
            TerrainLayer forestFloor = LocalLayer("Ground001", "TL_ForestFloor");
            TerrainLayer leaves = LocalLayer("Ground002", "TL_DeadLeaves");
            TerrainLayer grass = LocalLayer("Ground006", "TL_Grass");
            TerrainLayer dirtPath = LocalLayer("Ground005", "TL_DirtPath");

            List<TerrainLayer> layers = new List<TerrainLayer>();
            if (forestFloor != null) layers.Add(forestFloor);
            if (leaves != null) layers.Add(leaves);
            if (grass != null) layers.Add(grass);
            if (dirtPath != null) layers.Add(dirtPath);

            if (layers.Count == 0)
            {
                Debug.LogWarning("[Level] Aucun TerrainLayer trouve dans " + RttLayers);
                return;
            }

            data.terrainLayers = layers.ToArray();

            int res = data.alphamapResolution;
            float[,,] maps = new float[res, res, layers.Count];

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float wx = (float)x / (res - 1) * TerrainSize;
                    float wz = (float)y / (res - 1) * TerrainSize;

                    float toCenter = Vector2.Distance(new Vector2(wx, wz), Center);
                    float toClearing = Vector2.Distance(new Vector2(wx, wz), ClearingCenter);
                    float toPath = DistanceToPath(wx, wz);

                    // Sol de foret sombre + feuilles mortes, melanges au bruit.
                    float leafNoise = Mathf.PerlinNoise(wx * 0.05f + 3f, wz * 0.05f + 8f);
                    float wFloor = 1f - leafNoise * 0.75f;
                    float wLeaves = leafNoise * 0.75f;

                    // Herbe verte sur le plateau et dans la clairiere.
                    float openness = Mathf.Max(
                        1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PlateauRadius - 6f, PlateauRadius + 8f, toCenter)),
                        1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClearingRadius * 0.6f, ClearingRadius, toClearing)));

                    float grassNoise = Mathf.PerlinNoise(wx * 0.08f + 11f, wz * 0.08f + 2f);
                    float wGrass = openness * (0.55f + grassNoise * 0.45f);

                    // Chemin de terre craquelee.
                    float wPath = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PathHalfWidth * 0.6f, PathHalfWidth + 1.5f, toPath));
                    wPath *= 0.85f + Mathf.PerlinNoise(wx * 0.3f, wz * 0.3f) * 0.15f;

                    float[] w = { wFloor * (1f - wGrass) * (1f - wPath), wLeaves * (1f - wGrass) * (1f - wPath), wGrass * (1f - wPath), wPath };

                    float sum = 0f;
                    for (int i = 0; i < layers.Count; i++) sum += w[i];
                    if (sum <= 0.0001f) { w[0] = 1f; sum = 1f; }

                    for (int i = 0; i < layers.Count; i++)
                    {
                        maps[y, x, i] = w[i] / sum;
                    }
                }
            }

            data.SetAlphamaps(0, 0, maps);
        }

        // ------------------------------------------------------------------
        // Arbres
        // ------------------------------------------------------------------

        private static void BuildTrees(TerrainData data)
        {
            List<TreePrototype> prototypes = new List<TreePrototype>();

            for (int i = 1; i <= 4; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JpPrefabs + "Tree " + i + ".prefab");

                if (prefab == null)
                {
                    continue;
                }

                // Le terrain ignore les MeshCollider des arbres : sans variante a
                // capsule, le joueur traverserait les troncs.
                GameObject collisionVariant = GetOrCreateTreeVariant(prefab, "Tree " + i);

                TreePrototype prototype = new TreePrototype();
                prototype.prefab = collisionVariant != null ? collisionVariant : prefab;
                prototype.bendFactor = 0f;
                prototypes.Add(prototype);
            }

            if (prototypes.Count == 0)
            {
                Debug.LogWarning("[Level] Aucun prefab d'arbre trouve dans " + JpPrefabs);
                return;
            }

            data.treePrototypes = prototypes.ToArray();

            List<TreeInstance> instances = new List<TreeInstance>(800);

            // Grille de 8.5 m avec gigue : dense mais sans arbres imbriques.
            const float spacing = 8.5f;
            int cells = Mathf.FloorToInt(TerrainSize / spacing);

            for (int cy = 0; cy < cells; cy++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    float wx = (cx + 0.5f + Random.Range(-0.45f, 0.45f)) * spacing;
                    float wz = (cy + 0.5f + Random.Range(-0.45f, 0.45f)) * spacing;

                    if (wx < 4f || wz < 4f || wx > TerrainSize - 4f || wz > TerrainSize - 4f)
                    {
                        continue;
                    }

                    float toCenter = Vector2.Distance(new Vector2(wx, wz), Center);
                    float toClearing = Vector2.Distance(new Vector2(wx, wz), ClearingCenter);
                    float toPath = DistanceToPath(wx, wz);

                    // Pas d'arbre sur le plateau, dans la clairiere ni sur le chemin.
                    if (toCenter < PlateauRadius + 3f || toClearing < ClearingRadius * 0.75f || toPath < 4.5f)
                    {
                        continue;
                    }

                    // La foret s'eclaircit un peu pres du plateau et de la clairiere.
                    float density = 0.92f;
                    density *= Mathf.Clamp01(Mathf.InverseLerp(PlateauRadius + 3f, PlateauRadius + 25f, toCenter)) * 0.6f + 0.4f;
                    density *= Mathf.PerlinNoise(wx * 0.02f + 77f, wz * 0.02f + 13f) * 0.5f + 0.6f;

                    if (Random.value > density)
                    {
                        continue;
                    }

                    float scale = Random.Range(0.75f, 1.2f);

                    TreeInstance tree = new TreeInstance();
                    tree.position = new Vector3(wx / TerrainSize, 0f, wz / TerrainSize);
                    tree.prototypeIndex = Random.Range(0, prototypes.Count);
                    tree.widthScale = scale;
                    tree.heightScale = scale;
                    tree.rotation = Random.Range(0f, Mathf.PI * 2f);
                    tree.color = Color.white;
                    tree.lightmapColor = Color.white;

                    instances.Add(tree);
                }
            }

            data.SetTreeInstances(instances.ToArray(), true);
            Debug.Log("[Level] Arbres places : " + instances.Count);
        }

        private const string TreeVariantsFolder = "Assets/_Game/Prefabs/Trees";

        /// <summary>
        /// Variante de prefab d'arbre avec un CapsuleCollider sur le tronc a la
        /// place du MeshCollider du pack (seul type de collider supporte par le
        /// terrain). Le prefab d'origine n'est pas modifie.
        /// </summary>
        private static GameObject GetOrCreateTreeVariant(GameObject source, string baseName)
        {
            if (!AssetDatabase.IsValidFolder(TreeVariantsFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Game/Prefabs"))
                {
                    AssetDatabase.CreateFolder("Assets/_Game", "Prefabs");
                }

                AssetDatabase.CreateFolder("Assets/_Game/Prefabs", "Trees");
            }

            string path = TreeVariantsFolder + "/" + baseName + " (Collision).prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
            {
                return existing;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;

            if (instance == null)
            {
                return null;
            }

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i]);
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            float height = 15f;

            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;

                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                height = Mathf.Max(4f, bounds.size.y);
            }

            CapsuleCollider capsule = instance.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
            capsule.height = height;
            capsule.radius = 0.45f;
            capsule.direction = 1;

            GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);

            return variant;
        }

        // ------------------------------------------------------------------
        // Herbe et buissons
        // ------------------------------------------------------------------

        private static void BuildDetails(TerrainData data)
        {
            string[] names = { "Grass Patch Small", "Grass Patch Large", "Foliage 1", "Foliage 3" };
            List<DetailPrototype> prototypes = new List<DetailPrototype>();
            List<int> kinds = new List<int>();

            for (int i = 0; i < names.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JpPrefabs + names[i] + ".prefab");

                if (prefab == null)
                {
                    continue;
                }

                DetailPrototype prototype = new DetailPrototype();
                prototype.prototype = prefab;
                prototype.usePrototypeMesh = true;
                prototype.renderMode = DetailRenderMode.VertexLit;
                prototype.useInstancing = true;
                prototype.minWidth = 0.7f;
                prototype.maxWidth = 1.15f;
                prototype.minHeight = 0.7f;
                prototype.maxHeight = 1.15f;
                prototype.noiseSpread = 0.3f;
                prototype.healthyColor = Color.white;
                prototype.dryColor = new Color(0.85f, 0.85f, 0.8f);

                prototypes.Add(prototype);
                kinds.Add(i < 2 ? 0 : 1); // 0 = herbe, 1 = buisson
            }

            if (prototypes.Count == 0)
            {
                return;
            }

            data.detailPrototypes = prototypes.ToArray();
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

            int res = data.detailResolution;

            for (int layer = 0; layer < prototypes.Count; layer++)
            {
                int[,] map = new int[res, res];
                bool isGrass = kinds[layer] == 0;

                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float wx = (float)x / (res - 1) * TerrainSize;
                        float wz = (float)y / (res - 1) * TerrainSize;

                        float toCenter = Vector2.Distance(new Vector2(wx, wz), Center);
                        float toPath = DistanceToPath(wx, wz);

                        // Rien sous la maison ni sur le chemin.
                        if (toCenter < 14f || toPath < PathHalfWidth + 0.5f)
                        {
                            continue;
                        }

                        float noise = Mathf.PerlinNoise(wx * 0.06f + layer * 9f, wz * 0.06f + layer * 4f);

                        if (isGrass)
                        {
                            // Herbe presque partout, en touffes.
                            if (noise > 0.35f && Random.value < 0.55f)
                            {
                                map[y, x] = 1;
                            }
                        }
                        else
                        {
                            // Buissons plus rares, groupes.
                            if (noise > 0.62f && Random.value < 0.12f)
                            {
                                map[y, x] = 1;
                            }
                        }
                    }
                }

                data.SetDetailLayer(0, 0, layer, map);
            }
        }
    }
}
