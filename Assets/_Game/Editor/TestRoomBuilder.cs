using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Genere une salle de test en primitives : sol, murs, passage bas (test de
    /// l'accroupissement), escalier, rampe et obstacles.
    ///
    /// Menu : Tools > House of Silence > Create Test Room
    /// C'est un decor jetable, uniquement destine a valider le controleur joueur.
    /// </summary>
    public static class TestRoomBuilder
    {
        private const string RootName = "TestRoom";

        [MenuItem("Tools/House of Silence/Create Test Room", false, 40)]
        public static void CreateTestRoom()
        {
            GameObject existing = GameObject.Find(RootName);

            if (existing != null)
            {
                Debug.LogWarning("[Setup] Une '" + RootName + "' existe deja. Supprime-la avant d'en creer une nouvelle.", existing);
                Selection.activeGameObject = existing;
                return;
            }

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Test Room");

            // Sol 24 x 24
            CreateBox(root, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(24f, 0.5f, 24f));

            // Murs peripheriques
            CreateBox(root, "Wall_North", new Vector3(0f, 1.75f, 12f), new Vector3(24f, 4f, 0.4f));
            CreateBox(root, "Wall_South", new Vector3(0f, 1.75f, -12f), new Vector3(24f, 4f, 0.4f));
            CreateBox(root, "Wall_East", new Vector3(12f, 1.75f, 0f), new Vector3(0.4f, 4f, 24f));
            CreateBox(root, "Wall_West", new Vector3(-12f, 1.75f, 0f), new Vector3(0.4f, 4f, 24f));

            // Cloison interieure avec une ouverture
            CreateBox(root, "Divider_A", new Vector3(-4.5f, 1.75f, 2f), new Vector3(7f, 4f, 0.3f));
            CreateBox(root, "Divider_B", new Vector3(6.5f, 1.75f, 2f), new Vector3(7f, 4f, 0.3f));

            // Passage bas : oblige a s'accroupir (hauteur libre 1.05 m)
            GameObject crawl = new GameObject("CrouchPassage");
            crawl.transform.SetParent(root.transform, false);
            CreateBox(crawl, "Ceiling", new Vector3(0f, 1.55f, 6f), new Vector3(3f, 1f, 4f));
            CreateBox(crawl, "Side_Left", new Vector3(-1.7f, 1f, 6f), new Vector3(0.4f, 2f, 4f));
            CreateBox(crawl, "Side_Right", new Vector3(1.7f, 1f, 6f), new Vector3(0.4f, 2f, 4f));

            // Escalier de 6 marches
            GameObject stairs = new GameObject("Stairs");
            stairs.transform.SetParent(root.transform, false);

            for (int i = 0; i < 6; i++)
            {
                float height = 0.25f * (i + 1);
                CreateBox(stairs, "Step_" + i, new Vector3(-8f, height * 0.5f, -4f - i * 0.4f), new Vector3(3f, height, 0.4f));
            }

            // Plateforme en haut de l'escalier
            CreateBox(root, "Platform", new Vector3(-8f, 1.4f, -8.5f), new Vector3(3f, 0.3f, 3f));

            // Rampe (test de pente)
            GameObject ramp = CreateBox(root, "Ramp", new Vector3(8f, 0.75f, -6f), new Vector3(3f, 0.3f, 6f));
            ramp.transform.rotation = Quaternion.Euler(-20f, 0f, 0f);

            // Obstacles bas (test du pas de marche)
            CreateBox(root, "Crate_A", new Vector3(3f, 0.15f, -3f), new Vector3(1f, 0.3f, 1f));
            CreateBox(root, "Crate_B", new Vector3(4.5f, 0.3f, -3f), new Vector3(1f, 0.6f, 1f));
            CreateBox(root, "Crate_C", new Vector3(6f, 0.5f, -3f), new Vector3(1f, 1f, 1f));

            StaticEditorFlags staticFlags = StaticEditorFlags.ContributeGI
                | StaticEditorFlags.OccluderStatic
                | StaticEditorFlags.OccludeeStatic
                | StaticEditorFlags.BatchingStatic;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; i++)
            {
                GameObjectUtility.SetStaticEditorFlags(all[i].gameObject, staticFlags);
            }

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[Setup] Salle de test creee : sol, murs, passage bas (accroupissement), escalier, rampe et caisses.", root);
        }

        private static GameObject CreateBox(GameObject parent, string name, Vector3 position, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent.transform, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            return box;
        }
    }
}
