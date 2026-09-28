using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HouseOfSilence.EditorTools.Characters
{
    /// <summary>
    /// Configure Demon.fbx (Blender/demon_builder.py) en Humanoid :
    /// correspondance explicite os Blender -> os Unity, puis pose en T calculee
    /// (le modele est en A-pose, bras le long du corps) comme "Enforce T-Pose".
    /// Les os hors Humanoid (queue, orteils, ergot) restent animables en Generic
    /// via le squelette, ils sont simplement ignores par le retargeting.
    /// </summary>
    public static class DemonAvatarSetup
    {
        public const string FbxPath = "Assets/_Game/Art/Demon/Demon.fbx";

        /// <summary>Nom Humanoid Unity -> nom de l'os dans le FBX.</summary>
        private static Dictionary<string, string> BoneMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>
            {
                { "Hips", "Hips" }, { "Spine", "Spine" }, { "Chest", "Chest" }, { "UpperChest", "UpperChest" },
                { "Neck", "Neck" }, { "Head", "Head" }, { "Jaw", "Jaw" },
            };

            foreach (string side in new[] { "Left", "Right" })
            {
                string s = side == "Left" ? ".L" : ".R";
                map[side + "Eye"] = "Eye" + s;
                map[side + "Shoulder"] = "Shoulder" + s;
                map[side + "UpperArm"] = "UpperArm" + s;
                map[side + "LowerArm"] = "LowerArm" + s;
                map[side + "Hand"] = "Hand" + s;
                map[side + "UpperLeg"] = "UpperLeg" + s;
                map[side + "LowerLeg"] = "LowerLeg" + s;
                map[side + "Foot"] = "Foot" + s;
                map[side + "Toes"] = "Toes" + s;

                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    foreach (string part in new[] { "Proximal", "Intermediate", "Distal" })
                    {
                        map[side + " " + finger + " " + part] = finger + part + s;
                    }
                }
            }

            return map;
        }

        [MenuItem("Tools/House of Silence/Characters/Setup Demon Avatar (Humanoid)")]
        public static void Setup()
        {
            ModelImporter importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;

            if (importer == null)
            {
                Debug.LogError("[Demon] " + FbxPath + " introuvable : exporte-le depuis Blender (demon_builder.export_demon).");
                return;
            }

            // 1) Import brut (Generic) pour lire la hierarchie en pose de repos.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.SaveAndReimport();

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            GameObject instance = Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                Dictionary<string, string> map = BoneMap();
                Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    bones[t.name] = t;
                }

                EnforceTPose(bones);

                List<HumanBone> human = new List<HumanBone>();

                foreach (KeyValuePair<string, string> pair in map)
                {
                    if (!bones.ContainsKey(pair.Value))
                    {
                        Debug.LogWarning("[Demon] Os manquant : " + pair.Value);
                        continue;
                    }

                    HumanBone hb = new HumanBone { humanName = pair.Key, boneName = pair.Value };
                    hb.limit.useDefaultValues = true;
                    human.Add(hb);
                }

                List<SkeletonBone> skeleton = new List<SkeletonBone>();

                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    skeleton.Add(new SkeletonBone
                    {
                        name = t == instance.transform ? model.name : t.name,
                        position = t.localPosition,
                        rotation = t.localRotation,
                        scale = t.localScale,
                    });
                }

                HumanDescription description = importer.humanDescription;
                description.human = human.ToArray();
                description.skeleton = skeleton.ToArray();
                description.upperArmTwist = 0.5f;
                description.lowerArmTwist = 0.5f;
                description.upperLegTwist = 0.5f;
                description.lowerLegTwist = 0.5f;
                description.armStretch = 0.05f;
                description.legStretch = 0.05f;
                description.feetSpacing = 0f;
                description.hasTranslationDoF = false;

                // 2) Humanoid avec la correspondance et la pose en T.
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.humanDescription = description;
                importer.SaveAndReimport();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            Avatar avatar = null;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            {
                if (asset is Avatar a) avatar = a;
            }

            if (avatar != null && avatar.isValid && avatar.isHuman)
            {
                Debug.Log("[Demon] Avatar Humanoid valide.");
            }
            else
            {
                Debug.LogError("[Demon] Avatar invalide : verifie le Rig dans l'Inspector de Demon.fbx.");
            }
        }

        /// <summary>
        /// Met les bras a l'horizontale (axe X du modele), avant-bras et mains dans
        /// le prolongement, doigts a plat : la pose de reference attendue par Unity.
        /// </summary>
        private static void EnforceTPose(Dictionary<string, Transform> bones)
        {
            foreach (string s in new[] { ".L", ".R" })
            {
                Transform root = bones["Hips"].root;
                Vector3 outward = root.TransformDirection(s == ".L" ? Vector3.right : Vector3.left);

                // Blender +X (gauche du demon) devient -X dans Unity (conversion d'axes) :
                // on prend la direction reelle epaule -> coude pour choisir le bon cote.
                Vector3 current = bones["LowerArm" + s].position - bones["UpperArm" + s].position;
                if (Vector3.Dot(current, outward) < 0f) outward = -outward;

                Align(bones["UpperArm" + s], bones["LowerArm" + s], outward);
                Align(bones["LowerArm" + s], bones["Hand" + s], outward);

                Align(bones["Hand" + s], bones["MiddleProximal" + s], outward);

                foreach (string finger in new[] { "Index", "Middle", "Ring", "Little" })
                {
                    Align(bones[finger + "Proximal" + s], bones[finger + "Intermediate" + s], outward);
                    Align(bones[finger + "Intermediate" + s], bones[finger + "Distal" + s], outward);
                }
            }
        }

        /// <summary>Tourne 'bone' pour que la direction bone -> child pointe vers 'direction'.</summary>
        private static void Align(Transform bone, Transform child, Vector3 direction)
        {
            Vector3 current = child.position - bone.position;

            if (current.sqrMagnitude < 1e-8f)
            {
                return;
            }

            bone.rotation = Quaternion.FromToRotation(current.normalized, direction.normalized) * bone.rotation;
        }
    }
}
