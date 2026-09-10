using System.Collections.Generic;
using System.IO;
using HouseOfSilence.Items;
using HouseOfSilence.Objectives;
using HouseOfSilence.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Outils de la Phase 6 : generation des 8 objectifs du vertical slice et
    /// mise en place de l'ObjectiveManager.
    /// Menu : Tools > House of Silence > ...
    /// </summary>
    public static class ObjectiveSetupMenu
    {
        private const string ObjectivesFolder = "Assets/_Game/ScriptableObjects/Objectives";
        private const string ItemsFolder = "Assets/_Game/ScriptableObjects/Items";

        private struct ObjectiveDefinition
        {
            public string AssetName;
            public string Id;
            public string Title;
            public string Description;
            public string Hint;
            public ObjectiveCompletionMode Mode;
            public string RequiredItemAsset;
            public int RequiredCount;
        }

        /// <summary>La chaine du vertical slice, dans l'ordre du cahier des charges.</summary>
        private static readonly ObjectiveDefinition[] Definitions =
        {
            new ObjectiveDefinition
            {
                AssetName = "Objective_01_BasementAccess", Id = "find_basement_access",
                Title = "Trouver un moyen d'entrer dans le sous-sol",
                Description = "La porte du sous-sol ne s'ouvre pas. Quelqu'un a du garder la cle.",
                Hint = "Le bureau du rez-de-chaussee n'a pas encore ete fouille.",
                Mode = ObjectiveCompletionMode.Manual, RequiredCount = 1
            },
            new ObjectiveDefinition
            {
                AssetName = "Objective_02_FindFuse", Id = "find_fuse",
                Title = "Trouver le fusible",
                Description = "Sans fusible, le tableau electrique restera mort.",
                Hint = "Les tiroirs de la cuisine et l'etabli du garage.",
                Mode = ObjectiveCompletionMode.CollectItem, RequiredItemAsset = "Item_Fuse", RequiredCount = 1
            },
            new ObjectiveDefinition
            {
                AssetName = "Objective_03_RepairFusebox", Id = "repair_fusebox",
                Title = "Reparer le tableau electrique",
                Description = "Placer le fusible et remettre le circuit en etat.",
                Hint = "Le tableau se trouve dans la chaufferie.",
                Mode = ObjectiveCompletionMode.Manual, RequiredCount = 1
            },
            new ObjectiveDefinition
            {
                AssetName = "Objective_04_RestorePower", Id = "restore_power",
                Title = "Retablir l'electricite",
                Description = "Abaisser le disjoncteur principal.",
                Hint = "Le levier rouge, a cote du tableau.",
                Mode = ObjectiveCompletionMode.Manual, RequiredCount = 1
            },
            new ObjectiveDefinition
            {
                AssetName = "Objective_05_ExploreBasement", Id = "explore_basement",
                Title = "Explorer le sous-sol",
                Description = "Quelque chose descend rarement jusqu'ici.",
                Hint = "Suivre le couloir jusqu'a la piece de stockage.",
                Mode = ObjectiveCompletionMode.ReachZone, RequiredCount = 1
            },
            new ObjectiveDefinition
            {
                AssetName = "Objective_06_FindSymbol", Id = "find_symbol",
                Title = "Trouver le symbole grave",
                Description = "Il est cense marquer l'endroit exact.",
                Hint = "Regarder les murs derriere les etageres.",
                Mode = ObjectiveCompletionMode.Manual, RequiredCount = 1
            },
            new ObjectiveDefinition
            {
                AssetName = "Objective_07_TakeCursedObject", Id = "take_cursed_object",
                Title = "Recuperer l'objet maudit",
                Description = "Le prendre reveillera ce qui dort ici.",
                Hint = "Il est tiede. Ne pas le lacher.",
                Mode = ObjectiveCompletionMode.CollectItem, RequiredItemAsset = "Item_CursedObject", RequiredCount = 1
            },
            new ObjectiveDefinition
            {
                AssetName = "Objective_08_ReturnToExit", Id = "return_to_exit",
                Title = "Retourner a la sortie",
                Description = "Remonter jusqu'a la porte d'entree. Sans s'arreter.",
                Hint = "L'entree principale, au rez-de-chaussee.",
                Mode = ObjectiveCompletionMode.ReachZone, RequiredCount = 1
            }
        };

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Create Objective Assets", false, 100)]
        public static void CreateObjectiveAssets()
        {
            EnsureFolder(ObjectivesFolder);

            int created = 0;

            for (int i = 0; i < Definitions.Length; i++)
            {
                ObjectiveDefinition definition = Definitions[i];
                string path = ObjectivesFolder + "/" + definition.AssetName + ".asset";

                if (AssetDatabase.LoadAssetAtPath<ObjectiveData>(path) != null)
                {
                    continue;
                }

                ObjectiveData asset = ScriptableObject.CreateInstance<ObjectiveData>();
                AssetDatabase.CreateAsset(asset, path);
                Apply(asset, definition);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Setup] ObjectiveData : " + created + " cree(s), " + (Definitions.Length - created) + " deja present(s), dans " + ObjectivesFolder);
        }

        private static void Apply(ObjectiveData asset, ObjectiveDefinition definition)
        {
            SerializedObject serialized = new SerializedObject(asset);

            SetString(serialized, "objectiveId", definition.Id);
            SetString(serialized, "title", definition.Title);
            SetString(serialized, "description", definition.Description);
            SetString(serialized, "hint", definition.Hint);

            SerializedProperty mode = serialized.FindProperty("completionMode");

            if (mode != null)
            {
                mode.enumValueIndex = (int)definition.Mode;
            }

            SerializedProperty count = serialized.FindProperty("requiredCount");

            if (count != null)
            {
                count.intValue = Mathf.Max(1, definition.RequiredCount);
            }

            if (!string.IsNullOrEmpty(definition.RequiredItemAsset))
            {
                ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(ItemsFolder + "/" + definition.RequiredItemAsset + ".asset");

                if (item == null)
                {
                    Debug.LogWarning("[Setup] ItemData introuvable pour l'objectif '" + definition.Id + "' : " + definition.RequiredItemAsset
                        + ". Lance d'abord Tools > House of Silence > Create Item Assets.");
                }
                else
                {
                    SerializedProperty itemProperty = serialized.FindProperty("requiredItem");

                    if (itemProperty != null)
                    {
                        itemProperty.objectReferenceValue = item;
                    }
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Create Objective Manager", false, 101)]
        public static void CreateObjectiveManager()
        {
            CreateObjectiveAssets();

            ObjectiveManager manager = Object.FindAnyObjectByType<ObjectiveManager>(FindObjectsInactive.Include);

            if (manager == null)
            {
                GameObject host = GameObject.Find("[GameSystems]");

                if (host == null)
                {
                    host = new GameObject("[GameSystems]");
                    Undo.RegisterCreatedObjectUndo(host, "Create GameSystems");
                }

                manager = Undo.AddComponent<ObjectiveManager>(host);
            }

            // Remplit la sequence dans l'ordre des assets.
            SerializedObject serialized = new SerializedObject(manager);
            SerializedProperty list = serialized.FindProperty("objectives");

            if (list == null)
            {
                Debug.LogError("[Setup] Champ 'objectives' introuvable sur ObjectiveManager.");
                return;
            }

            list.ClearArray();

            int added = 0;

            for (int i = 0; i < Definitions.Length; i++)
            {
                ObjectiveData asset = AssetDatabase.LoadAssetAtPath<ObjectiveData>(ObjectivesFolder + "/" + Definitions[i].AssetName + ".asset");

                if (asset == null)
                {
                    continue;
                }

                list.InsertArrayElementAtIndex(added);
                list.GetArrayElementAtIndex(added).objectReferenceValue = asset;
                added++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EnsureObjectiveHud();

            Selection.activeGameObject = manager.gameObject;
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

            Debug.Log("[Setup] ObjectiveManager pret avec " + added + " objectifs sur '" + manager.gameObject.name + "'.", manager);
        }

        /// <summary>Ajoute l'affichage temporaire des objectifs s'il manque.</summary>
        public static void EnsureObjectiveHud()
        {
            ObjectiveHudOverlay overlay = Object.FindAnyObjectByType<ObjectiveHudOverlay>(FindObjectsInactive.Include);

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

            Undo.AddComponent<ObjectiveHudOverlay>(host);
            Debug.Log("[Setup] ObjectiveHudOverlay ajoute sur '" + host.name + "' (HUD temporaire, remplace en Phase 15).", host);
        }

        // ------------------------------------------------------------------

        /// <summary>Cree une zone d'objectif prete a l'emploi devant la vue de scene.</summary>
        [MenuItem("Tools/House of Silence/Create Objective Zone", false, 102)]
        public static void CreateObjectiveZone()
        {
            GameObject zone = new GameObject("ObjectiveZone");

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 position = view != null ? view.pivot : Vector3.zero;
            position.y = 0f;
            zone.transform.position = position;

            Undo.RegisterCreatedObjectUndo(zone, "Create Objective Zone");

            BoxCollider box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(4f, 3f, 4f);
            box.center = new Vector3(0f, 1.5f, 0f);

            zone.AddComponent<ObjectiveTrigger>();

            Selection.activeGameObject = zone;
            EditorSceneManager.MarkSceneDirty(zone.scene);

            Debug.Log("[Setup] Zone d'objectif creee. Renseigne son champ 'Objective' ou 'Objective Id'.", zone);
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
    }
}
