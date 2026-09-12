using System.Collections.Generic;
using HouseOfSilence.Interaction;
using HouseOfSilence.Items;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Construit un prefab visuel distinct pour chaque objet (cle, fusible, pile,
    /// lampe torche, pied-de-biche, objet maudit) a partir de primitives et de
    /// materiaux colores, et l'assigne comme World Prefab de son ItemData.
    ///
    /// Ce sont des silhouettes reconnaissables, pas des modeles finaux : le jour
    /// ou de vrais meshes arrivent, il suffit de remplacer le World Prefab.
    ///
    /// Menu : Tools > House of Silence > Items > Create Item Prefabs
    /// </summary>
    public static class ItemPrefabBuilder
    {
        private const string ItemsFolder = "Assets/_Game/ScriptableObjects/Items";
        private const string PrefabsFolder = "Assets/_Game/Prefabs/Items";
        private const string MaterialsFolder = "Assets/_Game/Materials/Items";

        private static Material s_Brass, s_Rust, s_Ceramic, s_Steel, s_Plastic, s_Copper, s_Glass, s_Cursed, s_Wood;

        [MenuItem("Tools/House of Silence/Items/Create Item Prefabs", false, 63)]
        public static void CreateItemPrefabs()
        {
            EnsureFolder(PrefabsFolder);
            EnsureFolder(MaterialsFolder);
            LoadMaterials();

            int created = 0;

            created += Build("Item_Key_Office", BuildKey, s_Brass);
            created += Build("Item_Key_Basement", BuildKey, s_Rust);
            created += Build("Item_Fuse", BuildFuse, null);
            created += Build("Item_Battery", BuildBattery, null);
            created += Build("Item_Flashlight", BuildFlashlight, null);
            created += Build("Item_Crowbar", BuildCrowbar, null);
            created += Build("Item_CursedObject", BuildCursedObject, null);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Setup] Prefabs d'objets : " + created + " cree(s) dans " + PrefabsFolder + ", World Prefab assigne sur chaque ItemData.");
        }

        /// <summary>
        /// Remplace, dans la scene ouverte, les objets au sol qui utilisent encore le
        /// cube par defaut par le prefab visuel de leur ItemData.
        /// </summary>
        [MenuItem("Tools/House of Silence/Items/Replace Pickup Visuals In Scene", false, 64)]
        public static void ReplacePickupVisualsInScene()
        {
            ItemPickup[] pickups = Object.FindObjectsByType<ItemPickup>(FindObjectsInactive.Include);
            int replaced = 0;

            for (int i = 0; i < pickups.Length; i++)
            {
                ItemPickup pickup = pickups[i];

                if (pickup == null || pickup.Item == null || pickup.Item.WorldPrefab == null)
                {
                    continue;
                }

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(pickup.gameObject);

                if (source == pickup.Item.WorldPrefab)
                {
                    continue; // deja le bon prefab
                }

                Transform old = pickup.transform;
                GameObject instance = PrefabUtility.InstantiatePrefab(pickup.Item.WorldPrefab) as GameObject;

                if (instance == null)
                {
                    continue;
                }

                instance.name = "Pickup_" + pickup.Item.ItemName;
                instance.transform.SetParent(old.parent, true);
                instance.transform.SetPositionAndRotation(old.position, old.rotation);

                ItemPickup newPickup = instance.GetComponent<ItemPickup>();

                if (newPickup != null)
                {
                    SerializedObject so = new SerializedObject(newPickup);
                    so.FindProperty("item").objectReferenceValue = pickup.Item;
                    so.FindProperty("count").intValue = pickup.Count;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                // On conserve un eventuel trigger d'objectif pose par le level designer.
                HouseOfSilence.Objectives.ObjectiveTrigger trigger = pickup.GetComponent<HouseOfSilence.Objectives.ObjectiveTrigger>();

                if (trigger != null)
                {
                    UnityEditorInternal.ComponentUtility.CopyComponent(trigger);
                    UnityEditorInternal.ComponentUtility.PasteComponentAsNew(instance);
                }

                Undo.DestroyObjectImmediate(old.gameObject);
                replaced++;
            }

            if (replaced > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }

            Debug.Log("[Setup] Objets au sol remplaces par leur visuel : " + replaced);
        }

        // ------------------------------------------------------------------
        // Construction generique
        // ------------------------------------------------------------------

        private delegate void ShapeBuilder(Transform visual, Material overrideMaterial);

        private static int Build(string itemAssetName, ShapeBuilder builder, Material overrideMaterial)
        {
            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(ItemsFolder + "/" + itemAssetName + ".asset");

            if (item == null)
            {
                Debug.LogWarning("[Setup] ItemData introuvable : " + itemAssetName + " (lance Create Item Assets d'abord).");
                return 0;
            }

            string path = PrefabsFolder + "/" + itemAssetName + ".prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
            {
                AssignWorldPrefab(item, existing);
                return 0;
            }

            GameObject root = new GameObject(itemAssetName);

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            builder(visual.transform, overrideMaterial);

            // Un seul collider simple sur la racine, ajuste au visuel.
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);

            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;

                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.08f);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.5f;
            body.linearDamping = 0.6f;
            body.angularDamping = 3f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            ItemPickup pickup = root.AddComponent<ItemPickup>();
            root.AddComponent<InteractableHighlight>();

            SerializedObject so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.FindProperty("count").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            AssignWorldPrefab(item, prefab);
            return 1;
        }

        private static void AssignWorldPrefab(ItemData item, GameObject prefab)
        {
            SerializedObject so = new SerializedObject(item);
            SerializedProperty property = so.FindProperty("worldPrefab");

            if (property != null && property.objectReferenceValue != prefab)
            {
                property.objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
            }
        }

        // ------------------------------------------------------------------
        // Formes
        // ------------------------------------------------------------------

        private static void BuildKey(Transform parent, Material material)
        {
            Material m = material != null ? material : s_Brass;

            // Anneau (tete de la cle), plat, dans le plan XZ.
            Part(parent, "Bow", PrimitiveType.Cylinder, new Vector3(-0.045f, 0f, 0f), new Vector3(0.06f, 0.008f, 0.06f), Quaternion.identity, m);
            Part(parent, "BowHole", PrimitiveType.Cylinder, new Vector3(-0.045f, 0f, 0f), new Vector3(0.025f, 0.012f, 0.025f), Quaternion.identity, s_Steel);

            // Tige et dents.
            Part(parent, "Shaft", PrimitiveType.Cube, new Vector3(0.03f, 0f, 0f), new Vector3(0.09f, 0.008f, 0.012f), Quaternion.identity, m);
            Part(parent, "Tooth_1", PrimitiveType.Cube, new Vector3(0.055f, 0f, 0.012f), new Vector3(0.012f, 0.008f, 0.014f), Quaternion.identity, m);
            Part(parent, "Tooth_2", PrimitiveType.Cube, new Vector3(0.035f, 0f, 0.01f), new Vector3(0.01f, 0.008f, 0.01f), Quaternion.identity, m);
        }

        private static void BuildFuse(Transform parent, Material material)
        {
            // Corps en ceramique, embouts metalliques.
            Part(parent, "Body", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.05f, 0.05f, 0.05f), Quaternion.Euler(0f, 0f, 90f), s_Ceramic);
            Part(parent, "Cap_L", PrimitiveType.Cylinder, new Vector3(-0.055f, 0f, 0f), new Vector3(0.056f, 0.012f, 0.056f), Quaternion.Euler(0f, 0f, 90f), s_Steel);
            Part(parent, "Cap_R", PrimitiveType.Cylinder, new Vector3(0.055f, 0f, 0f), new Vector3(0.056f, 0.012f, 0.056f), Quaternion.Euler(0f, 0f, 90f), s_Steel);
            Part(parent, "Label", PrimitiveType.Cube, new Vector3(0f, 0.026f, 0f), new Vector3(0.05f, 0.002f, 0.02f), Quaternion.identity, s_Rust);
        }

        private static void BuildBattery(Transform parent, Material material)
        {
            Part(parent, "Body", PrimitiveType.Capsule, Vector3.zero, new Vector3(0.036f, 0.045f, 0.036f), Quaternion.identity, s_Plastic);
            Part(parent, "Cap", PrimitiveType.Cylinder, new Vector3(0f, 0.052f, 0f), new Vector3(0.014f, 0.004f, 0.014f), Quaternion.identity, s_Copper);
            Part(parent, "Band", PrimitiveType.Cylinder, new Vector3(0f, -0.01f, 0f), new Vector3(0.037f, 0.01f, 0.037f), Quaternion.identity, s_Copper);
        }

        private static void BuildFlashlight(Transform parent, Material material)
        {
            Part(parent, "Body", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.05f, 0.11f, 0.05f), Quaternion.Euler(0f, 0f, 90f), s_Steel);
            Part(parent, "Head", PrimitiveType.Cylinder, new Vector3(0.125f, 0f, 0f), new Vector3(0.07f, 0.02f, 0.07f), Quaternion.Euler(0f, 0f, 90f), s_Plastic);
            Part(parent, "Lens", PrimitiveType.Cylinder, new Vector3(0.147f, 0f, 0f), new Vector3(0.06f, 0.003f, 0.06f), Quaternion.Euler(0f, 0f, 90f), s_Glass);
            Part(parent, "Grip", PrimitiveType.Cylinder, new Vector3(-0.06f, 0f, 0f), new Vector3(0.054f, 0.03f, 0.054f), Quaternion.Euler(0f, 0f, 90f), s_Plastic);
            Part(parent, "Button", PrimitiveType.Cube, new Vector3(0.03f, 0.026f, 0f), new Vector3(0.02f, 0.006f, 0.012f), Quaternion.identity, s_Rust);
        }

        private static void BuildCrowbar(Transform parent, Material material)
        {
            Part(parent, "Bar", PrimitiveType.Cube, Vector3.zero, new Vector3(0.5f, 0.025f, 0.025f), Quaternion.identity, s_Steel);
            Part(parent, "Hook", PrimitiveType.Cube, new Vector3(0.26f, 0.035f, 0f), new Vector3(0.05f, 0.09f, 0.025f), Quaternion.Euler(0f, 0f, -25f), s_Steel);
            Part(parent, "Claw", PrimitiveType.Cube, new Vector3(-0.255f, 0f, 0f), new Vector3(0.03f, 0.045f, 0.012f), Quaternion.identity, s_Steel);
            Part(parent, "Grip", PrimitiveType.Cube, new Vector3(-0.05f, 0f, 0f), new Vector3(0.14f, 0.028f, 0.028f), Quaternion.identity, s_Rust);
        }

        private static void BuildCursedObject(Transform parent, Material material)
        {
            // Une idole sombre : socle de bois, sphere qui luit faiblement.
            Part(parent, "Base", PrimitiveType.Cylinder, new Vector3(0f, -0.05f, 0f), new Vector3(0.11f, 0.015f, 0.11f), Quaternion.identity, s_Wood);
            Part(parent, "Stem", PrimitiveType.Cylinder, new Vector3(0f, -0.025f, 0f), new Vector3(0.03f, 0.02f, 0.03f), Quaternion.identity, s_Wood);
            Part(parent, "Orb", PrimitiveType.Sphere, new Vector3(0f, 0.045f, 0f), new Vector3(0.12f, 0.12f, 0.12f), Quaternion.identity, s_Cursed);
            Part(parent, "Ring", PrimitiveType.Cylinder, new Vector3(0f, 0.045f, 0f), new Vector3(0.15f, 0.006f, 0.15f), Quaternion.Euler(20f, 0f, 15f), s_Rust);
        }

        private static void Part(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;

            Collider collider = part.GetComponent<Collider>();

            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            if (material != null)
            {
                part.GetComponent<Renderer>().sharedMaterial = material;
            }
        }

        // ------------------------------------------------------------------
        // Materiaux
        // ------------------------------------------------------------------

        private static void LoadMaterials()
        {
            s_Brass = Mat("M_Item_Brass", new Color(0.78f, 0.62f, 0.28f), 0.7f, 0.9f, Color.black);
            s_Rust = Mat("M_Item_Rust", new Color(0.42f, 0.24f, 0.14f), 0.25f, 0.3f, Color.black);
            s_Ceramic = Mat("M_Item_Ceramic", new Color(0.88f, 0.86f, 0.8f), 0.55f, 0f, Color.black);
            s_Steel = Mat("M_Item_Steel", new Color(0.35f, 0.36f, 0.38f), 0.6f, 0.85f, Color.black);
            s_Plastic = Mat("M_Item_Plastic", new Color(0.08f, 0.08f, 0.09f), 0.45f, 0f, Color.black);
            s_Copper = Mat("M_Item_Copper", new Color(0.72f, 0.45f, 0.2f), 0.65f, 0.9f, Color.black);
            s_Glass = Mat("M_Item_Glass", new Color(0.85f, 0.9f, 1f), 0.95f, 0.2f, new Color(0.3f, 0.32f, 0.35f));
            s_Cursed = Mat("M_Item_Cursed", new Color(0.12f, 0.02f, 0.03f), 0.75f, 0.1f, new Color(0.55f, 0.03f, 0.02f));
            s_Wood = Mat("M_Item_Wood", new Color(0.3f, 0.2f, 0.12f), 0.3f, 0f, Color.black);
        }

        private static Material Mat(string name, Color color, float smoothness, float metallic, Color emission)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material != null)
            {
                return material;
            }

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);

            if (emission.maxColorComponent > 0.001f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(folder).Replace("\\", "/");
            string leaf = System.IO.Path.GetFileName(folder);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
