using HouseOfSilence.Doors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Creuse un sous-sol sombre sous la salle de test : escalier ferme par une
    /// porte a cle, couloir, deux pieces, une seule ampoule faiblarde.
    ///
    /// Menu : Tools > House of Silence > Create Test Basement
    ///
    /// Sert a tester la peur dans le noir (Phase 7) puis les lumieres (Phase 8).
    /// Prevu pour cohabiter avec la TestRoom existante : le sol est redecoupe
    /// pour laisser passer l'escalier.
    /// </summary>
    public static class BasementBuilder
    {
        private const string RootName = "TestBasement";

        // Niveau du sol du sous-sol (dessus de la dalle).
        private const float FloorY = -3.6f;

        // Dessous de la dalle du rez-de-chaussee = plafond du sous-sol.
        private const float CeilingY = -0.5f;

        private const float WallThickness = 0.2f;

        // Cage d'escalier : trou dans le sol du rez-de-chaussee.
        private const float StairMinX = -10.5f;
        private const float StairMaxX = -7.5f;
        private const float StairMinZ = 7f;
        private const float StairMaxZ = 11f;

        private const int StairSteps = 11;
        private const float StairRise = 0.3f;
        private const float StairRun = 0.35f;

        // Bas de l'escalier = debut du couloir (11 - 0.35 * 11 = 7.15).
        private const float StairBottomZ = StairMaxZ - StairRun * StairSteps;

        [MenuItem("Tools/House of Silence/Create Test Basement", false, 42)]
        public static void CreateTestBasement()
        {
            GameObject existing = GameObject.Find(RootName);

            if (existing != null)
            {
                Debug.LogWarning("[Setup] '" + RootName + "' existe deja. Supprime-le avant d'en recreer un.", existing);
                Selection.activeGameObject = existing;
                return;
            }

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Test Basement");

            CutStairHoleInGroundFloor(root.transform);
            BuildStairwell(root.transform);
            BuildBasement(root.transform);
            BuildLighting(root.transform);
            BuildEntranceDoor(root.transform);

            SetupHorrorAmbient();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[Setup] Sous-sol cree sous la salle de test. Entree : porte a cle 'basement' en (-9, 0, 11). "
                + "Ramasse la Cle du sous-sol, descends, et regarde la peur monter dans le noir.", root);
        }

        // ------------------------------------------------------------------
        // Sol du rez-de-chaussee : on remplace la dalle unique par 4 dalles
        // qui laissent un trou pour l'escalier.
        // ------------------------------------------------------------------

        private static void CutStairHoleInGroundFloor(Transform parent)
        {
            GameObject testRoom = GameObject.Find("TestRoom");
            Transform oldFloor = testRoom != null ? testRoom.transform.Find("Floor") : null;

            float minX = -12f, maxX = 12f, minZ = -12f, maxZ = 12f;

            if (oldFloor != null)
            {
                Vector3 c = oldFloor.position;
                Vector3 s = oldFloor.lossyScale;
                minX = c.x - s.x * 0.5f;
                maxX = c.x + s.x * 0.5f;
                minZ = c.z - s.z * 0.5f;
                maxZ = c.z + s.z * 0.5f;

                Undo.RecordObject(oldFloor.gameObject, "Disable floor");
                oldFloor.gameObject.SetActive(false);
                Debug.Log("[Setup] Ancien sol 'Floor' desactive et remplace par 4 dalles avec une tremie d'escalier.", oldFloor);
            }

            GameObject slabs = new GameObject("GroundFloorSlabs");
            slabs.transform.SetParent(parent, false);

            const float thickness = 0.5f;
            const float y = -0.25f;

            // Ouest du trou
            Slab(slabs.transform, "Slab_West", minX, StairMinX, minZ, maxZ, y, thickness);
            // Est du trou
            Slab(slabs.transform, "Slab_East", StairMaxX, maxX, minZ, maxZ, y, thickness);
            // Sud du trou (entre les deux bandes)
            Slab(slabs.transform, "Slab_South", StairMinX, StairMaxX, minZ, StairMinZ, y, thickness);
            // Nord du trou
            Slab(slabs.transform, "Slab_North", StairMinX, StairMaxX, StairMaxZ, maxZ, y, thickness);
        }

        // ------------------------------------------------------------------
        // Escalier : 12 marches, 30 cm de haut, 35 cm de profondeur, qui
        // descend du nord (z = 11) vers le sud.
        // ------------------------------------------------------------------

        private static void BuildStairwell(Transform parent)
        {
            GameObject stairs = new GameObject("Stairwell");
            stairs.transform.SetParent(parent, false);

            float centerX = (StairMinX + StairMaxX) * 0.5f;
            float width = StairMaxX - StairMinX;

            // Chaque marche est un bloc plein jusqu'a la dalle : aucun vide en dessous.
            for (int i = 0; i < StairSteps; i++)
            {
                float top = -StairRise * (i + 1);
                float height = top - FloorY;

                float zMax = StairMaxZ - StairRun * i;
                float zMin = zMax - StairRun;

                EditorSetupUtility.CreateBox(stairs.transform, "Step_" + (i + 1),
                    new Vector3(centerX, FloorY + height * 0.5f, (zMin + zMax) * 0.5f),
                    new Vector3(width, height, StairRun));
            }

            // Parois de la cage, du sol du sous-sol jusqu'au dessus du rez-de-chaussee,
            // alignees sur les murs du couloir.
            Wall(stairs.transform, "Cage_West", StairMinX - WallThickness, StairMinX, StairBottomZ, StairMaxZ + 0.3f, FloorY, 2.3f);
            Wall(stairs.transform, "Cage_East", StairMaxX, StairMaxX + WallThickness, StairBottomZ, StairMaxZ + 0.3f, FloorY, 2.3f);
        }

        // ------------------------------------------------------------------
        // Sous-sol : couloir + chaufferie + stockage.
        // ------------------------------------------------------------------

        private static void BuildBasement(Transform parent)
        {
            GameObject basement = new GameObject("Basement");
            basement.transform.SetParent(parent, false);

            // Dalle du sous-sol, sous toute la maison.
            Slab(basement.transform, "Basement_Floor", -12.5f, 12.5f, -12.5f, 12.5f, FloorY - 0.25f, 0.5f);

            float b = FloorY;
            float t = CeilingY;

            // Couloir : x [-10.5, -7.5], z de 6.8 (bas de l'escalier) a -4.
            const float corridorMinZ = -4f;
            float corridorMaxZ = StairBottomZ;

            Wall(basement.transform, "Corridor_West", StairMinX - WallThickness, StairMinX, corridorMinZ, corridorMaxZ, b, t);
            Wall(basement.transform, "Corridor_End", StairMinX - WallThickness, StairMaxX + WallThickness, corridorMinZ - WallThickness, corridorMinZ, b, t);

            // Mur est du couloir, perce de deux ouvertures (1.8 m) vers les pieces.
            float eastMinX = StairMaxX;
            float eastMaxX = StairMaxX + WallThickness;
            Wall(basement.transform, "Corridor_East_A", eastMinX, eastMaxX, 6.3f, corridorMaxZ + 0.01f, b, t);
            Wall(basement.transform, "Corridor_East_B", eastMinX, eastMaxX, -1.4f, 4.5f, b, t);
            Wall(basement.transform, "Corridor_East_C", eastMinX, eastMaxX, corridorMinZ, -3.2f, b, t);

            // Chaufferie : x [-7.3, -1], z [0, 6.8]
            const float roomMinX = -7.3f;
            const float roomMaxX = -1f;

            Wall(basement.transform, "BoilerRoom_North", roomMinX, roomMaxX + WallThickness, corridorMaxZ, corridorMaxZ + WallThickness, b, t);
            Wall(basement.transform, "BoilerRoom_East", roomMaxX, roomMaxX + WallThickness, -0.2f, corridorMaxZ + WallThickness, b, t);
            Wall(basement.transform, "Rooms_Divider", roomMinX, roomMaxX, -0.2f, 0f, b, t);

            // Stockage : x [-7.3, -1], z [-4, -0.2]
            Wall(basement.transform, "Storage_South", roomMinX, roomMaxX + WallThickness, corridorMinZ - WallThickness, corridorMinZ, b, t);
            Wall(basement.transform, "Storage_East", roomMaxX, roomMaxX + WallThickness, corridorMinZ, -0.2f, b, t);

            // Quelques caisses pour casser les lignes de vue.
            EditorSetupUtility.CreateBox(basement.transform, "Crate_1", new Vector3(-3f, FloorY + 0.4f, 5.5f), new Vector3(0.8f, 0.8f, 0.8f));
            EditorSetupUtility.CreateBox(basement.transform, "Crate_2", new Vector3(-2.2f, FloorY + 0.3f, 4.6f), new Vector3(0.6f, 0.6f, 0.6f));
            EditorSetupUtility.CreateBox(basement.transform, "Shelf", new Vector3(-1.4f, FloorY + 0.9f, -2f), new Vector3(0.5f, 1.8f, 3f));
            EditorSetupUtility.CreateBox(basement.transform, "Boiler", new Vector3(-6.2f, FloorY + 0.9f, 1.2f), new Vector3(1.2f, 1.8f, 1.2f));
        }

        // ------------------------------------------------------------------
        // Eclairage : UNE ampoule nue dans le couloir. Le reste est noir.
        // ------------------------------------------------------------------

        private static void BuildLighting(Transform parent)
        {
            GameObject lights = new GameObject("BasementLights");
            lights.transform.SetParent(parent, false);

            GameObject bulb = new GameObject("Bulb_Corridor");
            bulb.transform.SetParent(lights.transform, false);
            bulb.transform.position = new Vector3(-9f, CeilingY - 0.3f, 2f);

            Light light = bulb.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.82f, 0.55f);
            light.intensity = 1.2f;
            light.range = 5f;
            light.shadows = LightShadows.Soft;

            // Petit repere visuel de l'ampoule.
            GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glass.name = "Glass";
            glass.transform.SetParent(bulb.transform, false);
            glass.transform.localScale = Vector3.one * 0.12f;
            Object.DestroyImmediate(glass.GetComponent<Collider>());
        }

        // ------------------------------------------------------------------
        // Porte a cle en haut de l'escalier, serrure "basement".
        // ------------------------------------------------------------------

        private static void BuildEntranceDoor(Transform parent)
        {
            float doorZ = StairMaxZ + 0.2f;
            float centerX = (StairMinX + StairMaxX) * 0.5f;

            GameObject door = DoorSetupMenu.BuildDoor<KeyDoor>("Door_Basement", new Vector3(centerX, 0f, doorZ));
            door.transform.SetParent(parent, true);

            SerializedObject serialized = new SerializedObject(door.GetComponent<KeyDoor>());

            SerializedProperty lockId = serialized.FindProperty("lockId");
            if (lockId != null) lockId.stringValue = "basement";

            SerializedProperty displayName = serialized.FindProperty("displayName");
            if (displayName != null) displayName.stringValue = "Porte du sous-sol";

            SerializedProperty missing = serialized.FindProperty("missingKeyPrompt");
            if (missing != null) missing.stringValue = "Verrouillee - il faut la cle du sous-sol";

            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Remplissage de part et d'autre de la porte (le trou fait 3 m, la porte 1.1).
            GameObject fillers = new GameObject("DoorFillers");
            fillers.transform.SetParent(parent, false);

            Wall(fillers.transform, "Filler_West", StairMinX - WallThickness * 0.5f, centerX - 0.55f, doorZ - 0.07f, doorZ + 0.07f, 0f, 2.3f);
            Wall(fillers.transform, "Filler_East", centerX + 0.55f, StairMaxX + WallThickness * 0.5f, doorZ - 0.07f, doorZ + 0.07f, 0f, 2.3f);
            Wall(fillers.transform, "Filler_Top", StairMinX - WallThickness * 0.5f, StairMaxX + WallThickness * 0.5f, doorZ - 0.07f, doorZ + 0.07f, 2.2f, 2.3f);
        }

        // ------------------------------------------------------------------
        // Ambiance : sans ambiante sombre, aucun sous-sol n'est jamais noir.
        // ------------------------------------------------------------------

        /// <summary>
        /// Passe la lumiere ambiante en couleur unie tres sombre (sinon aucun
        /// interieur n'est jamais noir) et s'assure que le soleil projette des
        /// ombres. Reversible dans Window > Rendering > Lighting > Environment.
        /// </summary>
        [MenuItem("Tools/House of Silence/Setup Horror Ambient", false, 43)]
        public static void SetupHorrorAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.03f, 0.035f, 0.05f);

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type != LightType.Directional)
                {
                    continue;
                }

                // Sans ombres, le soleil traverse les plafonds : le sous-sol serait eclaire.
                if (lights[i].shadows == LightShadows.None)
                {
                    Undo.RecordObject(lights[i], "Enable directional shadows");
                    lights[i].shadows = LightShadows.Soft;
                    Debug.Log("[Setup] Ombres douces activees sur '" + lights[i].name + "' pour que les plafonds bloquent la lumiere.", lights[i]);
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Setup] Ambiante passee en couleur unie sombre. Pour revenir : Window > Rendering > Lighting > Environment.");
        }

        // ------------------------------------------------------------------

        private static GameObject Slab(Transform parent, string name, float minX, float maxX, float minZ, float maxZ, float centerY, float thickness)
        {
            if (maxX - minX <= 0.001f || maxZ - minZ <= 0.001f)
            {
                return null;
            }

            return EditorSetupUtility.CreateBox(parent, name,
                new Vector3((minX + maxX) * 0.5f, centerY, (minZ + maxZ) * 0.5f),
                new Vector3(maxX - minX, thickness, maxZ - minZ));
        }

        private static GameObject Wall(Transform parent, string name, float minX, float maxX, float minZ, float maxZ, float bottomY, float topY)
        {
            if (maxX - minX <= 0.001f || maxZ - minZ <= 0.001f || topY - bottomY <= 0.001f)
            {
                return null;
            }

            return EditorSetupUtility.CreateBox(parent, name,
                new Vector3((minX + maxX) * 0.5f, (bottomY + topY) * 0.5f, (minZ + maxZ) * 0.5f),
                new Vector3(maxX - minX, topY - bottomY, maxZ - minZ));
        }
    }
}
