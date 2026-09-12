using HouseOfSilence.Interaction;
using HouseOfSilence.Items;
using HouseOfSilence.Lights;
using HouseOfSilence.Objectives;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Outils de la Phase 8 : manager, conversion des Light en LightController,
    /// interrupteurs, tableau electrique, disjoncteur, et equipement du sous-sol.
    /// Menu : Tools > House of Silence > Lights > ...
    /// </summary>
    public static class LightSetupMenu
    {
        private const string FuseItemPath = "Assets/_Game/ScriptableObjects/Items/Item_Fuse.asset";
        private const string ObjectivesFolder = "Assets/_Game/ScriptableObjects/Objectives";

        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/Lights/Add Light Manager", false, 120)]
        public static LightManager AddLightManager()
        {
            LightManager manager = Object.FindAnyObjectByType<LightManager>(FindObjectsInactive.Include);

            if (manager != null)
            {
                return manager;
            }

            GameObject host = GameObject.Find("[GameSystems]");

            if (host == null)
            {
                host = new GameObject("[GameSystems]");
                Undo.RegisterCreatedObjectUndo(host, "Create GameSystems");
            }

            manager = Undo.AddComponent<LightManager>(host);
            EditorSceneManager.MarkSceneDirty(host.scene);

            Debug.Log("[Setup] LightManager ajoute sur '" + host.name + "'.", manager);
            return manager;
        }

        /// <summary>Transforme chaque Light selectionnee en lampe pilotable.</summary>
        [MenuItem("Tools/House of Silence/Lights/Convert Selected Lights To Controllers", false, 121)]
        public static void ConvertSelectedLights()
        {
            int converted = 0;

            foreach (GameObject go in Selection.gameObjects)
            {
                if (go.GetComponentInChildren<Light>(true) == null)
                {
                    continue;
                }

                MakeController(go, true, true);
                converted++;
            }

            if (converted == 0)
            {
                Debug.LogWarning("[Setup] Selectionne un ou plusieurs GameObjects portant une Light.");
                return;
            }

            AddLightManager();
            Debug.Log("[Setup] " + converted + " lampe(s) converties en LightController.");
        }

        [MenuItem("Tools/House of Silence/Lights/Create Light Switch", false, 122)]
        public static void CreateLightSwitch()
        {
            GameObject sw = BuildSwitch("LightSwitch", GetSpawnPosition() + Vector3.up * 1.3f, new Vector3(0.12f, 0.18f, 0.06f));
            sw.AddComponent<LightSwitch>();
            AddLightManager();
            Finish(sw, "Interrupteur cree. Sans cibles, il commande les lampes dans un rayon de 6 m.");
        }

        [MenuItem("Tools/House of Silence/Lights/Create Fuse Box", false, 123)]
        public static void CreateFuseBox()
        {
            GameObject box = BuildSwitch("FuseBox", GetSpawnPosition() + Vector3.up * 1.4f, new Vector3(0.5f, 0.7f, 0.12f));
            FuseBox fuseBox = box.AddComponent<FuseBox>();
            EditorSetupUtility.SetObjectField(fuseBox, "requiredItem", AssetDatabase.LoadAssetAtPath<ItemData>(FuseItemPath));
            AddLightManager();
            Finish(box, "Tableau electrique cree (reclame Item_Fuse).");
        }

        [MenuItem("Tools/House of Silence/Lights/Create Power Switch", false, 124)]
        public static void CreatePowerSwitch()
        {
            GameObject sw = BuildSwitch("PowerSwitch", GetSpawnPosition() + Vector3.up * 1.4f, new Vector3(0.3f, 0.5f, 0.12f));
            sw.AddComponent<PowerSwitch>();
            AddLightManager();
            Finish(sw, "Disjoncteur cree.");
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Equipe le sous-sol de test : ampoule du couloir pilotable et capricieuse,
        /// ampoule de la chaufferie sur le courant, interrupteurs, tableau
        /// electrique, disjoncteur, et les triggers d'objectifs 1, 3 et 4.
        /// Le courant demarre COUPE et sans fusible.
        /// </summary>
        [MenuItem("Tools/House of Silence/Lights/Setup Basement Lighting", false, 130)]
        public static void SetupBasementLighting()
        {
            GameObject basementRoot = GameObject.Find("TestBasement");

            if (basementRoot == null)
            {
                Debug.LogWarning("[Setup] Aucun 'TestBasement'. Lance d'abord Tools > House of Silence > Create Test Basement.");
                return;
            }

            if (basementRoot.transform.Find("BasementFixtures") != null)
            {
                Debug.LogWarning("[Setup] Le sous-sol est deja equipe (BasementFixtures existe).", basementRoot);
                return;
            }

            LightManager manager = AddLightManager();

            SerializedObject managerSo = new SerializedObject(manager);
            managerSo.FindProperty("startPowered").boolValue = false;
            managerSo.FindProperty("startWithFuse").boolValue = false;
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject fixtures = new GameObject("BasementFixtures");
            fixtures.transform.SetParent(basementRoot.transform, false);
            Undo.RegisterCreatedObjectUndo(fixtures, "Setup Basement Lighting");

            // --- Ampoule du couloir : toujours alimentee, mais capricieuse -----
            Transform corridorBulb = basementRoot.transform.Find("BasementLights/Bulb_Corridor");
            LightController corridor = null;

            if (corridorBulb != null)
            {
                corridor = MakeController(corridorBulb.gameObject, true, false);
                SetFloat(corridor, "randomFailuresPerMinute", 2f);
                SetFloat(corridor, "flickerDuration", 2f);
                SetFloat(corridor, "chanceToBreak", 0.05f);
            }

            // --- Ampoule de la chaufferie : sur le courant general -------------
            GameObject boilerBulb = BuildBulb(fixtures.transform, "Bulb_BoilerRoom", new Vector3(-4f, -0.8f, 3.5f), new Color(1f, 0.9f, 0.7f), 1.4f, 6f);
            LightController boiler = MakeController(boilerBulb, true, true);
            SetFloat(boiler, "randomFailuresPerMinute", 0.3f);

            // --- Interrupteurs ---------------------------------------------------
            GameObject corridorSwitch = BuildSwitch("Switch_Corridor", new Vector3(-10.35f, -2.3f, 6.5f), new Vector3(0.06f, 0.18f, 0.12f));
            corridorSwitch.transform.SetParent(fixtures.transform, true);
            LightSwitch corridorLs = corridorSwitch.AddComponent<LightSwitch>();
            SetTargets(corridorLs, corridor);

            GameObject boilerSwitch = BuildSwitch("Switch_BoilerRoom", new Vector3(-7.15f, -2.3f, 4.3f), new Vector3(0.06f, 0.18f, 0.12f));
            boilerSwitch.transform.SetParent(fixtures.transform, true);
            LightSwitch boilerLs = boilerSwitch.AddComponent<LightSwitch>();
            SetTargets(boilerLs, boiler);

            // --- Tableau electrique + disjoncteur, sur le mur ouest de la chaufferie
            GameObject fuseBoxGo = BuildSwitch("FuseBox", new Vector3(-7.15f, -2.2f, 2.4f), new Vector3(0.12f, 0.7f, 0.5f));
            fuseBoxGo.transform.SetParent(fixtures.transform, true);
            FuseBox fuseBox = fuseBoxGo.AddComponent<FuseBox>();
            EditorSetupUtility.SetObjectField(fuseBox, "requiredItem", AssetDatabase.LoadAssetAtPath<ItemData>(FuseItemPath));
            AttachObjectiveTrigger(fuseBoxGo, "Objective_03_RepairFusebox");

            GameObject breakerGo = BuildSwitch("PowerSwitch", new Vector3(-7.15f, -2.2f, 1.5f), new Vector3(0.12f, 0.5f, 0.3f));
            breakerGo.transform.SetParent(fixtures.transform, true);
            breakerGo.AddComponent<PowerSwitch>();
            AttachObjectiveTrigger(breakerGo, "Objective_04_RestorePower");

            // --- Objectif 1 : entrer dans le sous-sol = deverrouiller sa porte ---
            Transform basementDoor = basementRoot.transform.Find("Door_Basement");

            if (basementDoor != null)
            {
                AttachObjectiveTrigger(basementDoor.gameObject, "Objective_01_BasementAccess");
            }

            Selection.activeGameObject = fixtures;
            EditorSceneManager.MarkSceneDirty(fixtures.scene);

            Debug.Log("[Setup] Sous-sol equipe : courant COUPE au depart. Fusible -> tableau (chaufferie, mur ouest) -> disjoncteur. "
                + "Interrupteurs : bas de l'escalier (couloir) et entree de la chaufferie.", fixtures);
        }

        // ------------------------------------------------------------------

        /// <summary>Pose un LightController sur un GameObject portant des Light (utilise par le constructeur de niveau).</summary>
        public static LightController MakeController(GameObject go, bool startOn, bool requiresPower)
        {
            LightController controller = EditorSetupUtility.EnsureComponent<LightController>(go);

            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("startOn").boolValue = startOn;
            so.FindProperty("requiresPower").boolValue = requiresPower;

            // Ampoule visible : un enfant "Glass" avec un Renderer.
            Transform glass = go.transform.Find("Glass");

            if (glass != null)
            {
                Renderer renderer = glass.GetComponent<Renderer>();

                if (renderer != null)
                {
                    so.FindProperty("bulbRenderer").objectReferenceValue = renderer;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return controller;
        }

        /// <summary>Cree une ampoule nue : Light ponctuelle + petite sphere visible.</summary>
        public static GameObject BuildBulb(Transform parent, string name, Vector3 position, Color color, float intensity, float range)
        {
            GameObject bulb = new GameObject(name);
            bulb.transform.SetParent(parent, false);
            bulb.transform.position = position;

            Light light = bulb.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;

            GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glass.name = "Glass";
            glass.transform.SetParent(bulb.transform, false);
            glass.transform.localScale = Vector3.one * 0.12f;
            Object.DestroyImmediate(glass.GetComponent<Collider>());

            return bulb;
        }

        /// <summary>Cree un boitier interactif (interrupteur, tableau, disjoncteur).</summary>
        public static GameObject BuildSwitch(string name, Vector3 position, Vector3 size)
        {
            GameObject sw = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sw.name = name;
            sw.transform.position = position;
            sw.transform.localScale = size;
            sw.AddComponent<InteractableHighlight>();

            Undo.RegisterCreatedObjectUndo(sw, "Create " + name);
            return sw;
        }

        private static void SetTargets(LightSwitch lightSwitch, LightController target)
        {
            if (lightSwitch == null || target == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(lightSwitch);
            SerializedProperty list = so.FindProperty("targets");

            if (list == null)
            {
                return;
            }

            list.ClearArray();
            list.InsertArrayElementAtIndex(0);
            list.GetArrayElementAtIndex(0).objectReferenceValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachObjectiveTrigger(GameObject go, string objectiveAssetName)
        {
            ObjectiveData objective = AssetDatabase.LoadAssetAtPath<ObjectiveData>(ObjectivesFolder + "/" + objectiveAssetName + ".asset");

            if (objective == null)
            {
                Debug.LogWarning("[Setup] Objectif introuvable : " + objectiveAssetName + " (lance Create Objective Assets).");
                return;
            }

            ObjectiveTrigger trigger = EditorSetupUtility.EnsureComponent<ObjectiveTrigger>(go);

            SerializedObject so = new SerializedObject(trigger);
            so.FindProperty("objective").objectReferenceValue = objective;
            so.FindProperty("requireObjectiveActive").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(Object target, string field, float value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);

            if (property != null)
            {
                property.floatValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Vector3 GetSpawnPosition()
        {
            SceneView view = SceneView.lastActiveSceneView;

            if (view == null)
            {
                return Vector3.zero;
            }

            Vector3 pivot = view.pivot;
            pivot.y = 0f;
            return pivot;
        }

        private static void Finish(GameObject go, string message)
        {
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[Setup] " + message, go);
        }
    }
}
