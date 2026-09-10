using System.Collections.Generic;
using System.IO;
using HouseOfSilence.Interaction;
using HouseOfSilence.Inventory;
using HouseOfSilence.Items;
using HouseOfSilence.Player;
using HouseOfSilence.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Outils de la Phase 4 : creation des ItemData, du prefab de ramassage
    /// par defaut, et mise a niveau du joueur.
    /// Menu : Tools > House of Silence > ...
    /// </summary>
    public static class ItemSetupMenu
    {
        private const string ItemsFolder = "Assets/_Game/ScriptableObjects/Items";
        private const string PrefabsFolder = "Assets/_Game/Prefabs";
        private const string DefaultPickupPath = PrefabsFolder + "/Pickup_Default.prefab";
        private const string TestRootName = "TestPickups";

        /// <summary>Description compacte d'un objet a generer.</summary>
        private struct ItemDefinition
        {
            public string AssetName;
            public string ItemId;
            public string ItemName;
            public string Description;
            public ItemType Type;
            public bool Stackable;
            public int MaxStack;
            public bool Usable;
            public bool ConsumeOnUse;
            public bool Droppable;
            public string KeyId;
        }

        private static readonly ItemDefinition[] Definitions =
        {
            new ItemDefinition
            {
                AssetName = "Item_Key_Office", ItemId = "key_office", ItemName = "Cle du bureau",
                Description = "Une petite cle en laiton, gravee d'un B.",
                Type = ItemType.Key, Droppable = true, KeyId = "office", MaxStack = 1
            },
            new ItemDefinition
            {
                AssetName = "Item_Key_Basement", ItemId = "key_basement", ItemName = "Cle du sous-sol",
                Description = "Une cle lourde et rouillee. Elle sent la terre humide.",
                Type = ItemType.Key, Droppable = true, KeyId = "basement", MaxStack = 1
            },
            new ItemDefinition
            {
                AssetName = "Item_Fuse", ItemId = "fuse", ItemName = "Fusible",
                Description = "Un fusible en ceramique. Le tableau electrique en reclame.",
                Type = ItemType.Fuse, Stackable = true, MaxStack = 3, Droppable = true
            },
            new ItemDefinition
            {
                AssetName = "Item_Battery", ItemId = "battery", ItemName = "Pile",
                Description = "Recharge une lampe torche.",
                Type = ItemType.Consumable, Stackable = true, MaxStack = 4,
                Usable = true, ConsumeOnUse = true, Droppable = true
            },
            new ItemDefinition
            {
                AssetName = "Item_Flashlight", ItemId = "flashlight", ItemName = "Lampe torche",
                Description = "Faisceau etroit, batterie limitee.",
                Type = ItemType.Tool, Usable = true, Droppable = true, MaxStack = 1
            },
            new ItemDefinition
            {
                AssetName = "Item_Crowbar", ItemId = "crowbar", ItemName = "Pied-de-biche",
                Description = "De quoi forcer ce qui resiste.",
                Type = ItemType.Tool, Usable = true, Droppable = true, MaxStack = 1
            },
            new ItemDefinition
            {
                AssetName = "Item_CursedObject", ItemId = "cursed_object", ItemName = "Objet maudit",
                Description = "Il est tiede. Il ne devrait pas etre tiede.",
                Type = ItemType.QuestItem, Droppable = false, MaxStack = 1
            }
        };

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Create Item Assets", false, 60)]
        public static void CreateItemAssets()
        {
            EnsureFolder(ItemsFolder);

            int created = 0;

            for (int i = 0; i < Definitions.Length; i++)
            {
                ItemDefinition definition = Definitions[i];
                string path = ItemsFolder + "/" + definition.AssetName + ".asset";

                if (AssetDatabase.LoadAssetAtPath<ItemData>(path) != null)
                {
                    continue;
                }

                ItemData asset = ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(asset, path);
                ApplyDefinition(asset, definition);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Setup] ItemData : " + created + " cree(s), " + (Definitions.Length - created) + " deja present(s), dans " + ItemsFolder);
        }

        private static void ApplyDefinition(ItemData asset, ItemDefinition definition)
        {
            SerializedObject serialized = new SerializedObject(asset);

            SetString(serialized, "itemId", definition.ItemId);
            SetString(serialized, "itemName", definition.ItemName);
            SetString(serialized, "description", definition.Description);
            SetString(serialized, "keyId", definition.KeyId);

            SerializedProperty type = serialized.FindProperty("itemType");

            if (type != null)
            {
                type.enumValueIndex = (int)definition.Type;
            }

            SetBool(serialized, "stackable", definition.Stackable);
            SetBool(serialized, "usable", definition.Usable);
            SetBool(serialized, "consumeOnUse", definition.ConsumeOnUse);
            SetBool(serialized, "droppable", definition.Droppable);

            SerializedProperty maxStack = serialized.FindProperty("maxStack");

            if (maxStack != null)
            {
                maxStack.intValue = Mathf.Max(1, definition.MaxStack);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Create Default Pickup Prefab", false, 61)]
        public static GameObject CreateDefaultPickupPrefab()
        {
            EnsureFolder(PrefabsFolder);

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPickupPath);

            if (existing != null)
            {
                Debug.Log("[Setup] Le prefab " + DefaultPickupPath + " existe deja.");
                return existing;
            }

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.name = "Pickup_Default";
            temp.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);

            Rigidbody body = temp.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.linearDamping = 0.6f;
            body.angularDamping = 3f;

            temp.AddComponent<ItemPickup>();
            temp.AddComponent<InteractableHighlight>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, DefaultPickupPath);
            Object.DestroyImmediate(temp);

            AssetDatabase.SaveAssets();

            Debug.Log("[Setup] Prefab de ramassage par defaut cree : " + DefaultPickupPath, prefab);
            return prefab;
        }

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Add Inventory To Player", false, 22)]
        public static void AddInventoryToPlayer()
        {
            PlayerCharacter player = Object.FindAnyObjectByType<PlayerCharacter>(FindObjectsInactive.Include);

            if (player == null)
            {
                Debug.LogWarning("[Setup] Aucun joueur dans la scene. Utilise d'abord Tools > House of Silence > Create Player.");
                return;
            }

            PlayerInventory inventory = EditorSetupUtility.EnsureComponent<PlayerInventory>(player.gameObject);

            EditorSetupUtility.SetObjectField(inventory, "player", player);
            EditorSetupUtility.SetObjectField(inventory, "input", player.GetComponent<InputReader>());

            GameObject pickupPrefab = CreateDefaultPickupPrefab();
            EditorSetupUtility.SetObjectField(inventory, "defaultDropPrefab", pickupPrefab);

            EnsureInventoryHud();

            Selection.activeGameObject = player.gameObject;
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);

            Debug.Log("[Setup] PlayerInventory ajoute et cable sur '" + player.name + "'.", player);
        }

        /// <summary>Ajoute la barre d'inventaire temporaire si elle manque.</summary>
        public static void EnsureInventoryHud()
        {
            InventoryHudOverlay overlay = Object.FindAnyObjectByType<InventoryHudOverlay>(FindObjectsInactive.Include);

            if (overlay != null)
            {
                return;
            }

            GameObject host = GameObject.Find("[GameSystems]");

            if (host == null)
            {
                host = GameObject.Find("[HUD]");
            }

            if (host == null)
            {
                host = new GameObject("[HUD]");
                Undo.RegisterCreatedObjectUndo(host, "Create HUD");
            }

            Undo.AddComponent<InventoryHudOverlay>(host);
            Debug.Log("[Setup] InventoryHudOverlay ajoute sur '" + host.name + "' (HUD temporaire, remplace en Phase 15).", host);
        }

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Create Test Pickups", false, 62)]
        public static void CreateTestPickups()
        {
            GameObject existing = GameObject.Find(TestRootName);

            if (existing != null)
            {
                Debug.LogWarning("[Setup] '" + TestRootName + "' existe deja. Supprime-le avant d'en recreer un.", existing);
                Selection.activeGameObject = existing;
                return;
            }

            CreateItemAssets();
            GameObject prefab = CreateDefaultPickupPrefab();

            if (prefab == null)
            {
                return;
            }

            GameObject root = new GameObject(TestRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Test Pickups");

            List<KeyValuePair<string, int>> spawns = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>("Item_Key_Office", 1),
                new KeyValuePair<string, int>("Item_Key_Basement", 1),
                new KeyValuePair<string, int>("Item_Fuse", 2),
                new KeyValuePair<string, int>("Item_Battery", 3),
                new KeyValuePair<string, int>("Item_Flashlight", 1),
                new KeyValuePair<string, int>("Item_Crowbar", 1),
                new KeyValuePair<string, int>("Item_CursedObject", 1)
            };

            float x = -3f;

            for (int i = 0; i < spawns.Count; i++)
            {
                ItemData data = AssetDatabase.LoadAssetAtPath<ItemData>(ItemsFolder + "/" + spawns[i].Key + ".asset");

                if (data == null)
                {
                    Debug.LogWarning("[Setup] ItemData introuvable : " + spawns[i].Key);
                    continue;
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

                if (instance == null)
                {
                    continue;
                }

                instance.name = "Pickup_" + data.ItemName;
                instance.transform.SetParent(root.transform, false);
                instance.transform.localPosition = new Vector3(x, 0.4f, 6.5f);

                ItemPickup pickup = instance.GetComponent<ItemPickup>();

                if (pickup != null)
                {
                    SerializedObject serialized = new SerializedObject(pickup);
                    SerializedProperty itemProperty = serialized.FindProperty("item");

                    if (itemProperty != null)
                    {
                        itemProperty.objectReferenceValue = data;
                    }

                    SerializedProperty countProperty = serialized.FindProperty("count");

                    if (countProperty != null)
                    {
                        countProperty.intValue = spawns[i].Value;
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                x += 1f;
            }

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[Setup] " + spawns.Count + " objets a ramasser crees devant le joueur (z = 6.5).", root);
        }

        // ------------------------------------------------------------------

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace("\\", "/");
            string leaf = Path.GetFileName(folder);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void SetString(SerializedObject serialized, string field, string value)
        {
            SerializedProperty property = serialized.FindProperty(field);

            if (property != null)
            {
                property.stringValue = value == null ? string.Empty : value;
            }
        }

        private static void SetBool(SerializedObject serialized, string field, bool value)
        {
            SerializedProperty property = serialized.FindProperty(field);

            if (property != null)
            {
                property.boolValue = value;
            }
        }
    }
}
