using System;
using System.Collections.Generic;
using System.IO;
using HouseOfSilence.Demon;
using HouseOfSilence.Level;
using HouseOfSilence.Player;
using HouseOfSilence.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HouseOfSilence.EditorTools.Characters
{
    /// <summary>
    /// Demon jouable a partir du pack DemonDoll :
    ///  1. SKM_DemonDoll_Var1.fbx en Humanoid (os UE4 reconnus automatiquement) ;
    ///  2. materiaux URP Lit recrees depuis les textures du pack (les originaux sont en Standard) ;
    ///  3. animations Idle / Walk / Fly (a la place de la course) / SitCry generees par code (poses cle -> muscles Humanoid,
    ///     donc reutilisables sur n'importe quel personnage Humanoid) ;
    ///  4. Animator Controller (blend tree sur "Speed") ;
    ///  5. prefab DemonDoll_Playable (controleur TPS, pouvoirs, sons, camera).
    /// "Add Playable Demon To Open Scene" le pose ensuite dans la scene ouverte.
    /// </summary>
    public static class DemonDollSetup
    {
        public const string FbxPath = "Assets/DemonDoll/Mesh/SKM_DemonDoll_Var1.fbx";
        private const string PackPrefabPath = "Assets/DemonDoll/Prefab/SKM_DemonDoll_Var1.prefab";
        private const string Root = "Assets/_Game/Art/DemonDoll";
        private const string MaterialFolder = Root + "/Materials";
        private const string AnimFolder = Root + "/Animations";
        public const string ControllerPath = AnimFolder + "/DemonDoll_Locomotion.controller";
        public const string PrefabPath = "Assets/_Game/Prefabs/Characters/DemonDoll_Playable.prefab";
        private const string AudioFolder = "Assets/_Game/Audio/Demon/";
        private const string SoundKit = "Assets/_Game/Audio/Small Sound Kit/";

        /// <summary>Rouge des yeux, le meme que la poupee du menu principal.</summary>
        private static readonly Color EyeColor = new Color(1f, 0.02f, 0.01f);

        // Vitesses du controleur : seuils du blend tree.
        private const float WalkSpeed = 2.4f;
        private const float RunSpeed = 5.5f;

        [MenuItem("Tools/House of Silence/Characters/Build Playable Demon Doll")]
        public static void BuildAll()
        {
            Avatar avatar = SetupHumanoid();
            if (avatar == null) return;

            Dictionary<Material, Material> materials = ConvertMaterials();
            AnimationClip[] clips = BuildClips(avatar);
            AnimatorController controller = BuildController(clips[0], clips[1], clips[2], clips[3], clips[4], clips[5]);
            BuildPrefab(avatar, controller, materials);

            AssetDatabase.SaveAssets();
            Debug.Log("[DemonDoll] Pret : " + PrefabPath);
        }

        [MenuItem("Tools/House of Silence/Characters/Add Playable Demon To Open Scene")]
        public static void AddToOpenSceneMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                BuildAll();
            }

            AddToOpenScene();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // ------------------------------------------------------------------
        // 1. Humanoid
        // ------------------------------------------------------------------

        public static Avatar SetupHumanoid()
        {
            ModelImporter importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;

            if (importer == null)
            {
                Debug.LogError("[DemonDoll] " + FbxPath + " introuvable.");
                return null;
            }

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }

            Avatar avatar = LoadAvatar();

            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError("[DemonDoll] Avatar Humanoid invalide : ouvre l'onglet Rig de " + FbxPath + " > Configure.");
                return null;
            }

            return avatar;
        }

        private static Avatar LoadAvatar()
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            {
                if (asset is Avatar a) return a;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // 2. Materiaux URP
        // ------------------------------------------------------------------

        /// <summary>Materiau du pack (Standard) -> copie URP Lit avec les memes textures.</summary>
        public static Dictionary<Material, Material> ConvertMaterials()
        {
            EnsureFolder(MaterialFolder);
            Dictionary<Material, Material> map = new Dictionary<Material, Material>();
            GameObject pack = AssetDatabase.LoadAssetAtPath<GameObject>(PackPrefabPath);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");

            foreach (Renderer r in pack.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material source in r.sharedMaterials)
                {
                    if (source == null || map.ContainsKey(source)) continue;

                    string path = MaterialFolder + "/URP_" + source.name + ".mat";
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

                    if (m == null)
                    {
                        m = new Material(lit);
                        AssetDatabase.CreateAsset(m, path);
                    }

                    m.shader = lit;
                    m.shaderKeywords = new string[0];

                    Texture baseMap = source.GetTexture("_MainTex");
                    Texture normal = source.HasProperty("_BumpMap") ? source.GetTexture("_BumpMap") : null;
                    Texture emission = source.HasProperty("_EmissionMap") ? source.GetTexture("_EmissionMap") : null;
                    Color color = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                    float mode = source.HasProperty("_Mode") ? source.GetFloat("_Mode") : 0f;
                    bool doubleSided = source.shader.name.Contains("Double");
                    bool hairLike = source.name.Contains("Hair") || source.name.Contains("Lashes");

                    // Cheveux noirs dans le pack (couleur 0) : on garde un brun tres sombre, lisible.
                    if (hairLike && color.maxColorComponent < 0.05f) color = new Color(0.09f, 0.07f, 0.06f, 1f);

                    m.SetTexture("_BaseMap", baseMap);
                    m.SetColor("_BaseColor", color);
                    m.SetFloat("_Metallic", 0f);
                    m.SetFloat("_Smoothness", source.name.Contains("Eyes") || source.name.Contains("Teeth") ? 0.75f : hairLike ? 0.25f : 0.35f);

                    if (normal != null)
                    {
                        m.SetTexture("_BumpMap", normal);
                        m.SetFloat("_BumpScale", 1f);
                        m.EnableKeyword("_NORMALMAP");
                    }

                    // Decoupe (robe, cheveux, cils) plutot que transparence : pas de tri a gerer.
                    bool clip = mode >= 1f;
                    m.SetFloat("_Surface", 0f);
                    m.SetFloat("_AlphaClip", clip ? 1f : 0f);
                    m.SetFloat("_Cutoff", clip ? Mathf.Clamp(source.GetFloat("_Cutoff"), 0.2f, 0.6f) : 0.5f);
                    m.SetOverrideTag("RenderType", clip ? "TransparentCutout" : "Opaque");
                    m.renderQueue = clip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
                    if (clip) m.EnableKeyword("_ALPHATEST_ON");

                    m.SetFloat("_Cull", doubleSided || hairLike ? (float)CullMode.Off : (float)CullMode.Back);

                    // Yeux rouges lumineux, comme la poupee du menu (HorrorForestMenuBuilder).
                    if (source.name.Contains("Eyes"))
                    {
                        m.SetColor("_BaseColor", new Color(0.25f, 0.01f, 0.01f));
                        m.SetFloat("_Smoothness", 0.9f);
                        m.SetTexture("_EmissionMap", emission);
                        m.SetColor("_EmissionColor", EyeColor * 8f);
                        m.EnableKeyword("_EMISSION");
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    }

                    EditorUtility.SetDirty(m);
                    map[source] = m;
                }
            }

            return map;
        }

        // ------------------------------------------------------------------
        // 3. Animations (poses cle -> muscles Humanoid)
        // ------------------------------------------------------------------

        internal delegate void PoseFunction(Poser poser, float phase);

        /// <summary>Pose le squelette d'une instance a partir de sa pose de repos, en rotations monde.</summary>
        internal class Poser
        {
            private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
            private readonly Dictionary<Transform, Vector3> _restPos = new Dictionary<Transform, Vector3>();
            private readonly Dictionary<Transform, Quaternion> _restRot = new Dictionary<Transform, Quaternion>();

            public Poser(GameObject instance)
            {
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    _bones[t.name] = t;
                    _restPos[t] = t.localPosition;
                    _restRot[t] = t.localRotation;
                }
            }

            public Transform this[string name] { get { return _bones[name]; } }

            public void Reset()
            {
                foreach (KeyValuePair<Transform, Vector3> p in _restPos) p.Key.localPosition = p.Value;
                foreach (KeyValuePair<Transform, Quaternion> r in _restRot) r.Key.localRotation = r.Value;
            }

            /// <summary>Tourne l'os autour d'un axe monde (les enfants suivent).</summary>
            public void Rot(string bone, Vector3 axis, float degrees)
            {
                Transform t = _bones[bone];
                t.rotation = Quaternion.AngleAxis(degrees, axis) * t.rotation;
            }

            /// <summary>Oriente l'os pour que la direction os -> enfant pointe vers 'direction'.</summary>
            public void Aim(string bone, string child, Vector3 direction)
            {
                Transform t = _bones[bone];
                Vector3 current = _bones[child].position - t.position;
                t.rotation = Quaternion.FromToRotation(current.normalized, direction.normalized) * t.rotation;
            }

            public void Move(string bone, Vector3 offset)
            {
                _bones[bone].position += offset;
            }
        }

        internal static readonly Vector3 X = Vector3.right;   // axe gauche-droite : avant / arriere
        internal static readonly Vector3 Y = Vector3.up;      // torsion
        internal static readonly Vector3 Z = Vector3.forward; // inclinaison laterale

        /// <summary>Bras qui pendent (depuis la pose A du modele), coudes a peine plies.</summary>
        internal static void HangArms(Poser p, float spread, float elbow)
        {
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "_l" : "_r";   // _l est en -X (la poupee regarde +Z)
                p.Aim("upperarm" + side, "lowerarm" + side, new Vector3(s * spread, -1f, 0.02f));
                p.Aim("lowerarm" + side, "hand" + side, new Vector3(s * spread * 0.6f, -1f, elbow));
                p.Aim("hand" + side, "middle_01" + side, new Vector3(s * spread * 0.4f, -1f, elbow * 0.5f));
            }
        }

        /// <summary>Valeur interpolee (en douceur) entre des cles (temps, valeur) triees.</summary>
        internal static float Keys(float t, params float[] pairs)
        {
            if (t <= pairs[0]) return pairs[1];

            for (int i = 2; i < pairs.Length; i += 2)
            {
                if (t <= pairs[i])
                {
                    float k = Mathf.InverseLerp(pairs[i - 2], pairs[i], t);
                    return Mathf.Lerp(pairs[i - 1], pairs[i + 1], Mathf.SmoothStep(0f, 1f, k));
                }
            }

            return pairs[pairs.Length - 1];
        }

        internal static float Wave(float phase, float offset = 0f)
        {
            return Mathf.Sin((phase + offset) * Mathf.PI * 2f);
        }

        /// <summary>Idle : poupee immobile, tete penchee, respiration et petits spasmes.</summary>
        private static void IdlePose(Poser p, float phase)
        {
            HangArms(p, 0.16f, 0.12f);

            float breath = Wave(phase * 2f);
            p.Move("pelvis", Vector3.up * 0.003f * breath);
            p.Rot("spine_01", X, 2f + 0.6f * breath);
            p.Rot("spine_03", X, 1.5f * breath);
            p.Rot("spine_02", Z, 1.5f * Wave(phase));

            // Tete penchee sur le cote, qui oscille lentement, avec deux saccades.
            float twitch = Mathf.Exp(-Mathf.Pow((phase - 0.62f) * 40f, 2f)) - 0.6f * Mathf.Exp(-Mathf.Pow((phase - 0.66f) * 40f, 2f));
            float twitch2 = Mathf.Exp(-Mathf.Pow((phase - 0.2f) * 50f, 2f));
            p.Rot("neck_01", Z, 10f + 3f * Wave(phase, 0.1f) + 14f * twitch);
            p.Rot("head", Z, 12f + 2f * Wave(phase, 0.3f) - 8f * twitch2);
            p.Rot("head", X, 6f + 2f * breath);
            p.Rot("head", Y, 6f * twitch2);

            // Bras mous qui se balancent a peine ; les doigts tressaillent.
            p.Rot("upperarm_l", X, 2f * Wave(phase, 0.25f));
            p.Rot("upperarm_r", X, 2f * Wave(phase, 0.75f));
            p.Rot("middle_01_r", X, 12f * twitch);
            p.Rot("index_01_r", X, 15f * twitch);
        }

        /// <summary>Marche : pas raides et saccades de poupee, bras qui pendent, tete penchee.</summary>
        private static void WalkPose(Poser p, float phase)
        {
            HangArms(p, 0.14f, 0.15f);

            float s = Wave(phase);                 // jambe gauche en avant quand s > 0
            float bob = Mathf.Abs(Wave(phase, 0.25f));
            p.Move("pelvis", Vector3.up * (0.012f * bob - 0.012f));
            p.Rot("pelvis", Y, 6f * s);
            p.Rot("pelvis", Z, 3f * Wave(phase, 0.25f));
            p.Rot("spine_01", X, 6f);
            p.Rot("spine_02", Y, -8f * s);

            Leg(p, "_l", s, phase, 0f, 26f, 45f);
            Leg(p, "_r", -s, phase, 0.5f, 26f, 45f);

            // Bras mous, en opposition, un peu en retard.
            // (rotation +X = bras vers l'arriere ; le bras gauche recule quand la jambe gauche avance)
            p.Rot("upperarm_l", X, 14f * Wave(phase, 0.05f));
            p.Rot("upperarm_r", X, -14f * Wave(phase, 0.05f));

            p.Rot("neck_01", Z, 9f + 3f * Wave(phase, 0.1f));
            p.Rot("head", Z, 10f);
            p.Rot("head", X, 4f - 3f * bob);
        }

        /// <summary>
        /// Assise par terre, genoux releves, dos voute, visage cache dans les mains ;
        /// les epaules et le dos sont secoues de sanglots (deux crises par cycle).
        /// </summary>
        private static void SitCryPose(Poser p, float phase)
        {
            // Centre du visage en pose de repos, exprime dans le repere de l'os de la tete.
            Vector3 faceLocal = p["head"].InverseTransformPoint(new Vector3(0f, 0.87f, 0.075f));

            // Sanglots : deux crises par cycle, secousses rapides dans chacune.
            float crisis = Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 4f));
            float sob = crisis * Mathf.Pow(Mathf.Abs(Mathf.Sin(phase * Mathf.PI * 2f * 7f)), 3f);
            float breath = Wave(phase * 2f);

            // Jambes : cuisses levees vers l'avant, genoux plies, pieds a plat.
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "_l" : "_r";
                p.Aim("thigh" + side, "calf" + side, new Vector3(s * 0.2f, 0.7f, 1f));
                p.Aim("calf" + side, "foot" + side, new Vector3(s * 0.04f, -1f, -0.6f));
                p.Aim("foot" + side, "ball" + side, new Vector3(s * 0.12f, -0.12f, 1f));
            }

            // Tout le corps descend jusqu'a poser les pieds (et les fesses) au sol.
            float drop = Mathf.Min(p["ball_l"].position.y - 0.02f, p["foot_l"].position.y - 0.06f);
            p.Move("pelvis", Vector3.down * drop);
            p.Move("pelvis", Vector3.up * 0.003f * breath);

            // Dos voute, tete baissee dans les mains.
            p.Rot("spine_01", X, 24f + 3f * sob + 1f * breath);
            p.Rot("spine_02", X, 14f + 4f * sob);
            p.Rot("spine_03", X, 8f + 3f * sob);
            p.Rot("neck_01", X, 16f);
            p.Rot("head", X, 20f - 7f * sob);
            p.Rot("head", Y, 3f * Mathf.Sin(phase * Mathf.PI * 2f * 5f) * crisis);

            // Epaules qui se soulevent a chaque sanglot.
            p.Rot("clavicle_l", Z, -9f * sob);
            p.Rot("clavicle_r", Z, 9f * sob);

            // Mains plaquees sur le visage, coudes vers l'avant et vers le bas.
            Vector3 face = p["head"].TransformPoint(faceLocal);

            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "_l" : "_r";
                Vector3 elbowTarget = face + new Vector3(s * 0.1f, -0.17f, 0.02f);
                p.Aim("upperarm" + side, "lowerarm" + side, elbowTarget - p["upperarm" + side].position);

                Vector3 wristTarget = face + new Vector3(s * 0.04f, -0.05f, 0.035f);
                p.Aim("lowerarm" + side, "hand" + side, wristTarget - p["lowerarm" + side].position);

                Vector3 fingersTarget = face + new Vector3(s * 0.015f, 0.05f, 0.045f);
                p.Aim("hand" + side, "middle_01" + side, fingersTarget - p["hand" + side].position);
            }
        }

        /// <summary>
        /// Vol (remplace la course) : la poupee flotte a ~40 cm du sol, le corps incline
        /// vers l'avant, la tete relevee vers la direction de vol, jambes et bras qui
        /// trainent derriere en ondulant doucement.
        /// </summary>
        private static void FlyPose(Poser p, float phase)
        {
            float bob = Wave(phase);

            // Souleve et incline : tourner le bassin penche le buste en avant et envoie les jambes en arriere.
            p.Move("pelvis", Vector3.up * (0.38f + 0.035f * bob));
            p.Rot("pelvis", X, 40f + 3f * Wave(phase, 0.15f));
            p.Rot("pelvis", Z, 3f * Wave(phase, 0.4f));
            p.Rot("spine_01", X, 4f + 2f * bob);

            // Tete relevee : elle regarde ou elle va.
            p.Rot("neck_01", X, -28f);
            p.Rot("head", X, -18f + 3f * Wave(phase, 0.3f));
            p.Rot("head", Z, 6f);

            // Jambes serrees qui trainent, pointes tendues, legere ondulation decalee.
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "_l" : "_r";
                float lag = s < 0 ? 0f : 0.12f;
                p.Rot("thigh" + side, Z, s * 3f);
                p.Rot("thigh" + side, X, 10f + 6f * Wave(phase, 0.2f + lag));
                p.Rot("calf" + side, X, 22f + 10f * Wave(phase, 0.35f + lag));
                p.Rot("foot" + side, X, 40f);
            }

            // Bras qui trainent le long du corps, vers l'arriere.
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "_l" : "_r";
                float sway = 0.05f * Wave(phase, 0.25f + (s < 0 ? 0f : 0.1f));
                p.Aim("upperarm" + side, "lowerarm" + side, new Vector3(s * 0.32f, -0.5f + sway, -1f));
                p.Aim("lowerarm" + side, "hand" + side, new Vector3(s * 0.25f, -0.35f + sway, -1f));
                p.Aim("hand" + side, "middle_01" + side, new Vector3(s * 0.2f, -0.45f, -1f));
            }
        }

        /// <summary>
        /// Mise a mort (2 s, sans boucle) : la poupee s'accroupit, bondit sur la victime
        /// (en avant et en l'air), griffe au visage, puis retombe et reste la tete penchee.
        /// </summary>
        private static void KillPose(Poser p, float t)
        {
            float crouch = Keys(t, 0f, 0f, 0.12f, 1f, 0.2f, 0f, 0.78f, 0f, 0.86f, 0.6f, 1f, 0f);
            float leap = Keys(t, 0.12f, 0f, 0.3f, 1f, 0.68f, 1f, 0.84f, 0f);
            float claw = t > 0.3f && t < 0.7f ? Mathf.Sin((t - 0.3f) * Mathf.PI * 2f * 5f) : 0f;
            float forward = Keys(t, 0.1f, 0f, 0.3f, 0.45f, 0.7f, 0.45f, 0.9f, 0.15f, 1f, 0.1f);

            // Corps : accroupi, puis projete en avant et en l'air, puis retombe.
            p.Move("pelvis", Vector3.up * (0.55f * leap - 0.12f * crouch) + Vector3.forward * forward);
            p.Rot("pelvis", X, 20f * crouch + 25f * leap);
            p.Rot("spine_01", X, 15f * crouch + 8f * leap);
            p.Rot("spine_02", Y, 12f * claw);

            // Jambes repliees pendant le saut, flechies a la reception.
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "_l" : "_r";
                p.Rot("thigh" + side, X, -(55f * leap + 50f * crouch));
                p.Rot("calf" + side, X, 90f * leap + 80f * crouch);
                p.Rot("foot" + side, X, 25f * leap);
            }

            // Tete tendue vers la proie, puis penchee.
            p.Rot("neck_01", X, -20f * leap);
            p.Rot("head", X, -15f * leap + 10f * (1f - leap));
            p.Rot("head", Z, 18f * Keys(t, 0.75f, 0f, 1f, 1f));

            // Bras : en arriere accroupie, puis tendus vers le visage de la victime et qui griffent.
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "_l" : "_r";
                float swipe = claw * s;
                Vector3 back = new Vector3(s * 0.3f, -0.6f, -1f);
                Vector3 reach = new Vector3(s * (0.25f + 0.15f * swipe), 0.55f + 0.35f * swipe, 1f);
                Vector3 rest = new Vector3(s * 0.15f, -1f, 0.1f);
                Vector3 dir = leap > 0.01f ? Vector3.Lerp(back, reach, leap) : Vector3.Lerp(rest, back, crouch);
                p.Aim("upperarm" + side, "lowerarm" + side, dir);
                p.Aim("lowerarm" + side, "hand" + side, dir + Vector3.up * 0.2f * leap);
                p.Aim("hand" + side, "middle_01" + side, dir + Vector3.up * 0.4f * leap);

                // Doigts crochus.
                foreach (string finger in new[] { "index", "middle", "ring", "pinky" })
                {
                    p.Rot(finger + "_01" + side, X, 35f * leap);
                    p.Rot(finger + "_02" + side, X, 40f * leap);
                }
            }
        }

        /// <summary>
        /// Jumpscare (3,6 s, sans boucle), vu par la victime : la poupee s'accroupit, bondit
        /// jusqu'au visage (~1,5 m), s'y accroche jambes serrees autour du buste et
        /// etrangle a deux mains en tremblant, tete qui tressaute en fixant la victime.
        /// </summary>
        private static void JumpscarePose(Poser p, float t)
        {
            float crouch = Keys(t, 0f, 0f, 0.05f, 1f, 0.09f, 0f);
            float leap = Keys(t, 0.05f, 0f, 0.125f, 1f);
            float strangle = Mathf.Clamp01((t - 0.13f) / 0.1f);
            float time = t * 3.6f;
            float shake = strangle * (0.6f * Mathf.Sin(time * Mathf.PI * 2f * 7.3f) + 0.4f * Mathf.Sin(time * Mathf.PI * 2f * 11.1f));
            float twitch = strangle * Mathf.Sign(Mathf.Sin(time * Mathf.PI * 2f * 1.7f)) * Mathf.Abs(Mathf.Sin(time * Mathf.PI * 2f * 0.9f));

            // Monte au niveau du visage, collee a la victime.
            p.Move("pelvis", Vector3.up * (0.65f * leap - 0.12f * crouch + 0.012f * shake) + Vector3.forward * (0.12f * leap + 0.01f * shake));
            p.Rot("pelvis", X, 20f * crouch - 8f * leap);
            p.Rot("spine_01", X, 15f * crouch + 6f * leap);
            p.Rot("spine_02", Y, 5f * shake);

            // Jambes serrees autour du buste.
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "_l" : "_r";
                p.Rot("thigh" + side, X, -(65f * leap + 50f * crouch));
                p.Rot("thigh" + side, Z, s * 18f * leap);
                p.Rot("calf" + side, X, 85f * leap + 80f * crouch);
                p.Rot("foot" + side, X, 30f * leap);
            }

            // Tete levee vers les yeux de la victime, tressaute.
            p.Rot("neck_01", X, -18f * leap);
            p.Rot("head", X, -12f * leap + 4f * shake);
            p.Rot("head", Z, 14f * twitch + 3f * shake);

            // Mains a la gorge (juste sous la camera), qui serrent et tremblent.
            foreach (int s in new[] { -1, 1 })
            {
                string side = s < 0 ? "_l" : "_r";
                float squeeze = 0.06f * shake * s;
                Vector3 rest = new Vector3(s * 0.15f, -1f, 0.1f);
                Vector3 back = new Vector3(s * 0.3f, -0.6f, -1f);
                Vector3 grip = new Vector3(s * (0.32f + squeeze), 0.35f, 1f);
                Vector3 dir = leap > 0.01f ? Vector3.Lerp(back, grip, leap) : Vector3.Lerp(rest, back, crouch);
                p.Aim("upperarm" + side, "lowerarm" + side, dir);
                p.Aim("lowerarm" + side, "hand" + side, new Vector3(-s * 0.35f * leap, 0.25f * leap, 1f) + (1f - leap) * dir);
                p.Aim("hand" + side, "middle_01" + side, new Vector3(-s * 0.6f * leap, 0.2f * leap, 1f) + (1f - leap) * dir);

                foreach (string finger in new[] { "index", "middle", "ring", "pinky" })
                {
                    p.Rot(finger + "_01" + side, X, 45f * strangle);
                    p.Rot(finger + "_02" + side, X, 55f * strangle);
                    p.Rot(finger + "_03" + side, X, 35f * strangle);
                }
            }
        }

        /// <summary>
        /// Jambe : cuisse qui balance (swing > 0 = en avant), genou qui plie pendant
        /// le passage de la jambe, pied qui se deroule.
        /// </summary>
        private static void Leg(Poser p, string side, float swing, float phase, float offset, float hipAmplitude, float kneeAmplitude)
        {
            // swing = sin(2 pi local) : la jambe revient vers l'avant entre local 0.75 et 1.25.
            float local = Mathf.Repeat(phase + offset, 1f);
            // Genou plie pendant ce retour (pic vers 0.9), presque tendu en appui.
            float swingPhase = Mathf.Repeat(local - 0.7f, 1f);
            float knee = (swingPhase < 0.45f ? Mathf.Sin(swingPhase / 0.45f * Mathf.PI) : 0f) * kneeAmplitude + 4f;
            // Pointe relevee a l'attaque du talon, poussee de la pointe juste avant le decollage.
            float pushOff = Mathf.Exp(-Mathf.Pow((local - 0.7f) * 9f, 2f));
            float foot = -swing * 10f + pushOff * 18f;

            p.Rot("thigh" + side, X, -hipAmplitude * swing);
            p.Rot("calf" + side, X, knee);
            p.Rot("foot" + side, X, foot);
        }

        public static AnimationClip[] BuildClips(Avatar avatar)
        {
            EnsureFolder(AnimFolder);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            GameObject instance = Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            try
            {
                Poser poser = new Poser(instance);
                HumanPoseHandler handler = new HumanPoseHandler(avatar, instance.transform);

                return new[]
                {
                    Record("DemonDoll_Idle", poser, handler, IdlePose, 3.2f),
                    Record("DemonDoll_Walk", poser, handler, WalkPose, 0.72f),
                    Record("DemonDoll_Fly", poser, handler, FlyPose, 1.4f),
                    Record("DemonDoll_SitCry", poser, handler, SitCryPose, 3f),
                    Record("DemonDoll_Kill", poser, handler, KillPose, 2f, AnimFolder, false),
                    Record("DemonDoll_Jumpscare", poser, handler, JumpscarePose, 3.6f, AnimFolder, false),
                };
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        internal static AnimationClip Record(string name, Poser poser, HumanPoseHandler handler, PoseFunction pose, float period, string folder = AnimFolder, bool loop = true)
        {
            const float fps = 30f;
            int frames = Mathf.Max(2, Mathf.RoundToInt(period * fps));
            string[] muscles = HumanTrait.MuscleName;

            AnimationCurve[] muscleCurves = new AnimationCurve[muscles.Length];
            for (int i = 0; i < muscles.Length; i++) muscleCurves[i] = new AnimationCurve();
            AnimationCurve[] rootT = { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            AnimationCurve[] rootQ = { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };

            HumanPose human = new HumanPose();
            Quaternion previousQ = Quaternion.identity;

            for (int f = 0; f <= frames; f++)
            {
                // Boucle : derniere cle = premiere. Sinon la phase va de 0 a 1 (derniere pose tenue).
                float phase = loop ? (f % frames) / (float)frames : f / (float)frames;
                float time = f / fps * (period * fps / frames);

                poser.Reset();
                pose(poser, phase);
                handler.GetHumanPose(ref human);

                for (int i = 0; i < muscles.Length; i++) muscleCurves[i].AddKey(time, human.muscles[i]);

                rootT[0].AddKey(time, human.bodyPosition.x);
                rootT[1].AddKey(time, human.bodyPosition.y);
                rootT[2].AddKey(time, human.bodyPosition.z);

                // Garde le quaternion du meme cote que le precedent (pas de saut d'interpolation).
                Quaternion q = human.bodyRotation;
                if (f > 0 && Quaternion.Dot(q, previousQ) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                previousQ = q;
                rootQ[0].AddKey(time, q.x);
                rootQ[1].AddKey(time, q.y);
                rootQ[2].AddKey(time, q.z);
                rootQ[3].AddKey(time, q.w);
            }

            EnsureFolder(folder);
            string path = folder + "/" + name + ".anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }

            clip.ClearCurves();
            clip.frameRate = fps;

            for (int i = 0; i < muscles.Length; i++)
            {
                clip.SetCurve("", typeof(Animator), MuscleProperty(muscles[i]), muscleCurves[i]);
            }

            string[] axes = { "x", "y", "z", "w" };
            for (int i = 0; i < 3; i++) clip.SetCurve("", typeof(Animator), "RootT." + axes[i], rootT[i]);
            for (int i = 0; i < 4; i++) clip.SetCurve("", typeof(Animator), "RootQ." + axes[i], rootQ[i]);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlend = loop;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>"Left Index 1 Stretched" -> "LeftHand.Index.1 Stretched" (nom de propriete des clips).</summary>
        private static string MuscleProperty(string muscle)
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    string prefix = side + " " + finger + " ";
                    if (muscle.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return side + "Hand." + finger + "." + muscle.Substring(prefix.Length);
                    }
                }
            }

            return muscle;
        }

        // ------------------------------------------------------------------
        // 4. Animator
        // ------------------------------------------------------------------

        public static AnimatorController BuildController(AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip sitCry, AnimationClip kill, AnimationClip jumpscare)
        {
            // Reutilise l'asset existant (meme GUID) : les prefabs et scenes gardent leur reference.
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else
            {
                while (controller.parameters.Length > 0) controller.RemoveParameter(0);
                AnimatorStateMachine old = controller.layers[0].stateMachine;
                foreach (ChildAnimatorState child in old.states) old.RemoveState(child.state);
                foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                {
                    if (sub is BlendTree) Object.DestroyImmediate(sub, true);
                }
            }

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "AnimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            BlendTree tree;
            AnimatorState state = controller.CreateBlendTreeInController("Locomotion", out tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, WalkSpeed);
            tree.AddChild(run, RunSpeed);

            state.speedParameterActive = true;
            state.speedParameter = "AnimSpeed";
            state.iKOnFeet = false; // les clips generes n'ont pas de cibles IK : le Foot IK replierait les jambes
            machine.defaultState = state;

            // Assise en pleurs (DemonCry) : parametre "Crying".
            controller.AddParameter("Crying", AnimatorControllerParameterType.Bool);
            AnimatorState cry = machine.AddState("SitCry");
            cry.motion = sitCry;

            AnimatorStateTransition sitDown = state.AddTransition(cry);
            sitDown.hasExitTime = false;
            sitDown.duration = 0.55f;
            sitDown.AddCondition(AnimatorConditionMode.If, 0f, "Crying");

            AnimatorStateTransition standUp = cry.AddTransition(state);
            standUp.hasExitTime = false;
            standUp.duration = 0.45f;
            standUp.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crying");

            // Mise a mort (DemonKill) : declencheur "Kill", retour a la locomotion a la fin du clip.
            controller.AddParameter("Kill", AnimatorControllerParameterType.Trigger);
            AnimatorState killState = machine.AddState("Kill");
            killState.motion = kill;

            AnimatorStateTransition strike = state.AddTransition(killState);
            strike.hasExitTime = false;
            strike.duration = 0.12f;
            strike.AddCondition(AnimatorConditionMode.If, 0f, "Kill");

            AnimatorStateTransition recover = killState.AddTransition(state);
            recover.hasExitTime = true;
            recover.exitTime = 0.95f;
            recover.duration = 0.3f;

            // Jumpscare (DemonKill, vue FPS de la victime) : declencheur "Jumpscare".
            controller.AddParameter("Jumpscare", AnimatorControllerParameterType.Trigger);
            AnimatorState scare = machine.AddState("Jumpscare");
            scare.motion = jumpscare;

            AnimatorStateTransition leapIn = state.AddTransition(scare);
            leapIn.hasExitTime = false;
            leapIn.duration = 0.08f;
            leapIn.AddCondition(AnimatorConditionMode.If, 0f, "Jumpscare");

            // Le clip tient sa derniere pose ; retour a la locomotion quand DemonKill rend la main.
            AnimatorStateTransition leapOut = scare.AddTransition(state);
            leapOut.hasExitTime = true;
            leapOut.exitTime = 1.55f;
            leapOut.duration = 0.4f;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ------------------------------------------------------------------
        // 5. Prefab jouable
        // ------------------------------------------------------------------

        public static GameObject BuildPrefab(Avatar avatar, AnimatorController controller, Dictionary<Material, Material> materials)
        {
            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));

            GameObject root = new GameObject("DemonDoll");

            CharacterController cc = root.AddComponent<CharacterController>();
            cc.height = 1.05f;
            cc.radius = 0.2f;
            cc.center = new Vector3(0f, 0.53f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.02f;
            cc.minMoveDistance = 0f;

            // Modele + Animator (materiaux URP repris du prefab du pack, emplacement par emplacement).
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath));
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);

            GameObject pack = AssetDatabase.LoadAssetAtPath<GameObject>(PackPrefabPath);
            Dictionary<string, Renderer> packRenderers = new Dictionary<string, Renderer>();
            foreach (Renderer r in pack.GetComponentsInChildren<Renderer>(true)) packRenderers[r.name] = r;

            foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Renderer source;
                if (!packRenderers.TryGetValue(r.name, out source)) continue;
                Material[] mats = source.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material converted;
                    if (mats[i] != null && materials.TryGetValue(mats[i], out converted)) mats[i] = converted;
                }
                r.sharedMaterials = mats;
                r.updateWhenOffscreen = true;
            }

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Camera TPS (desactivee tant qu'on ne joue pas le demon).
            GameObject camObject = new GameObject("DemonCamera");
            camObject.transform.SetParent(root.transform, false);
            camObject.transform.localPosition = new Vector3(0.35f, 1.5f, -2.4f);
            Camera cam = camObject.AddComponent<Camera>();
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 220f;
            cam.enabled = false;
            UniversalAdditionalCameraData camData = camObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            AudioListener listener = camObject.AddComponent<AudioListener>();
            listener.enabled = false;

            // Sons : cri 3D de tres longue portee, effets.
            GameObject voice = new GameObject("Voice");
            voice.transform.SetParent(root.transform, false);
            voice.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            AudioSource scream = voice.AddComponent<AudioSource>();
            scream.playOnAwake = false;
            scream.spatialBlend = 1f;
            scream.rolloffMode = AudioRolloffMode.Logarithmic;
            scream.minDistance = 15f;
            scream.maxDistance = 300f;
            scream.dopplerLevel = 0f;

            AudioSource sfx = root.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0.5f;

            DemonController demon = root.AddComponent<DemonController>();
            EditorSetupUtility.SetObjectField(demon, "tpsCamera", cam);
            EditorSetupUtility.SetObjectField(demon, "animator", animator);

            // Pleurs en boucle (DemonCry), a hauteur de tete.
            GameObject tears = new GameObject("Crying");
            tears.transform.SetParent(root.transform, false);
            tears.transform.localPosition = new Vector3(0f, 0.5f, 0.1f);
            AudioSource crying = tears.AddComponent<AudioSource>();
            crying.playOnAwake = false;
            crying.loop = true;
            crying.spatialBlend = 1f;
            crying.rolloffMode = AudioRolloffMode.Logarithmic;
            crying.minDistance = 6f;
            crying.maxDistance = 60f;
            crying.dopplerLevel = 0f;
            crying.volume = 0f;

            DemonCry cry = root.AddComponent<DemonCry>();
            EditorSetupUtility.SetObjectField(cry, "controller", demon);
            EditorSetupUtility.SetObjectField(cry, "animator", animator);
            EditorSetupUtility.SetObjectField(cry, "cryingSource", crying);
            EditorSetupUtility.SetObjectField(cry, "cryingClip", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Game/Audio/horror/woman-crying.mp3"));

            DemonKill killer = root.AddComponent<DemonKill>();
            EditorSetupUtility.SetObjectField(killer, "controller", demon);
            EditorSetupUtility.SetObjectField(killer, "animator", animator);
            EditorSetupUtility.SetObjectField(killer, "sfxSource", sfx);
            EditorSetupUtility.SetObjectField(killer, "killScream", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Game/Audio/Demon/horror-demon-cry.mp3"));
            EditorSetupUtility.SetObjectField(killer, "impactSound", AssetDatabase.LoadAssetAtPath<AudioClip>(SoundKit + "Dark/Breaking_Bones_01.wav"));

            DemonPowers powers = root.AddComponent<DemonPowers>();
            EditorSetupUtility.SetObjectField(powers, "controller", demon);
            EditorSetupUtility.SetObjectField(powers, "screamSource", scream);
            EditorSetupUtility.SetObjectField(powers, "sfxSource", sfx);
            // Cri : le son ajoute au projet (horror-demon-cry), sinon le cri synthetise (Tools/synth_demon_scream.py).
            AudioClip screamClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Game/Audio/Demon/horror-demon-cry.mp3");
            if (screamClip == null) screamClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + "Demon_Scream.wav");
            EditorSetupUtility.SetObjectField(powers, "screamClip", screamClip);
            EditorSetupUtility.SetObjectField(powers, "visionSound", AssetDatabase.LoadAssetAtPath<AudioClip>(SoundKit + "Dark/Drn_Alien_01.wav"));
            EditorSetupUtility.SetObjectField(powers, "teleportDepartSound", AssetDatabase.LoadAssetAtPath<AudioClip>(SoundKit + "Magic/Mgc_Electric_Throw_01.wav"));
            EditorSetupUtility.SetObjectField(powers, "teleportArriveSound", AssetDatabase.LoadAssetAtPath<AudioClip>(SoundKit + "Magic/Mgc_Electric_Impact_01.wav"));
            EditorSetupUtility.SetObjectField(powers, "revealMaskMaterial", RevealMaterial("M_RevealMask", "HouseOfSilence/RevealMask"));
            EditorSetupUtility.SetObjectField(powers, "revealOutlineMaterial", RevealMaterial("M_RevealOutline", "HouseOfSilence/RevealOutline"));

            AddEyes(root, model);
            AddNightVision(root, demon);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------
        // Yeux rouges visibles de loin
        // ------------------------------------------------------------------

        /// <summary>
        /// Halo sur chaque oeil (position calculee depuis les sous-maillages "Eyes" de la
        /// tete en pose de repos), accroche a l'os de la tete, regard vers +Z.
        /// </summary>
        private static void AddEyes(GameObject root, GameObject model)
        {
            Transform head = null;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "head") head = t;
            }

            SkinnedMeshRenderer headRenderer = null;
            foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.name == "SK_Head") headRenderer = r;
            }

            if (head == null || headRenderer == null)
            {
                Debug.LogWarning("[DemonDoll] Tete introuvable : pas de halo sur les yeux.");
                return;
            }

            List<Vector3> eyes = new List<Vector3>();
            Mesh baked = new Mesh();
            headRenderer.BakeMesh(baked);
            Vector3[] vertices = baked.vertices;
            Material[] mats = headRenderer.sharedMaterials;

            for (int sub = 0; sub < baked.subMeshCount && sub < mats.Length; sub++)
            {
                if (mats[sub] == null || !mats[sub].name.Contains("Eyes")) continue;

                int[] triangles = baked.GetTriangles(sub);
                Vector3 sum = Vector3.zero;
                float front = float.MinValue;
                foreach (int index in triangles)
                {
                    Vector3 world = headRenderer.transform.TransformPoint(vertices[index]);
                    sum += world;
                    front = Mathf.Max(front, world.z);
                }

                Vector3 center = sum / Mathf.Max(1, triangles.Length);
                center.z = front;   // a la surface de l'oeil, cote regard (+Z)
                eyes.Add(center);
            }

            Object.DestroyImmediate(baked);

            if (eyes.Count == 0)
            {
                Debug.LogWarning("[DemonDoll] Sous-maillages des yeux introuvables.");
                return;
            }

            Mesh quad = EyeQuad();
            Material glowMaterial = RevealMaterial("M_EyeGlow", "HouseOfSilence/EyeGlow");
            List<Renderer> glows = new List<Renderer>();

            for (int i = 0; i < eyes.Count; i++)
            {
                GameObject glow = new GameObject(eyes[i].x < 0f ? "EyeGlow_L" : "EyeGlow_R");
                glow.transform.SetParent(head, true);
                glow.transform.SetPositionAndRotation(eyes[i] + Vector3.forward * 0.004f, Quaternion.LookRotation(Vector3.forward, Vector3.up));
                glow.AddComponent<MeshFilter>().sharedMesh = quad;
                MeshRenderer r = glow.AddComponent<MeshRenderer>();
                r.sharedMaterial = glowMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                glows.Add(r);
            }

            // Pas de lumiere sur le visage : le rouge reste sur les yeux seulement.

            DemonEyes demonEyes = root.AddComponent<DemonEyes>();
            SerializedObject so = new SerializedObject(demonEyes);
            SerializedProperty list = so.FindProperty("glows");
            list.arraySize = glows.Count;
            for (int i = 0; i < glows.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = glows[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Quad unitaire (le shader l'oriente vers la camera) aux bornes larges.</summary>
        private static Mesh EyeQuad()
        {
            string path = Root + "/EyeGlowQuad.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, path);
            }

            mesh.Clear();
            mesh.name = "EyeGlowQuad";
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            // Le halo grandit avec la distance (jusqu'a ~1 m) : bornes larges pour ne pas etre elimine.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------------
        // Vision nocturne
        // ------------------------------------------------------------------

        private static void AddNightVision(GameObject root, DemonController demon)
        {
            GameObject volumeObject = new GameObject("NightVision");
            volumeObject.transform.SetParent(root.transform, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.weight = 0f;
            volume.sharedProfile = NightVisionProfile();

            DemonNightVision nightVision = root.AddComponent<DemonNightVision>();
            EditorSetupUtility.SetObjectField(nightVision, "controller", demon);
            EditorSetupUtility.SetObjectField(nightVision, "volume", volume);
        }

        /// <summary>Exposition relevee, image desaturee teintee de rouge, vignette sombre, grain.</summary>
        private static VolumeProfile NightVisionProfile()
        {
            string path = Root + "/DemonNightVision_Profile.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            // Repart de zero a chaque build (valeurs de ce script).
            foreach (VolumeComponent component in profile.components.ToArray())
            {
                profile.components.Remove(component);
                Object.DestroyImmediate(component, true);
            }

            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(2.8f);
            color.contrast.Override(18f);
            color.saturation.Override(-70f);
            color.colorFilter.Override(new Color(1f, 0.78f, 0.74f));

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.4f);
            vignette.smoothness.Override(0.55f);
            vignette.color.Override(new Color(0.14f, 0f, 0f));

            FilmGrain grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.35f);

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.5f);

            foreach (VolumeComponent component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        internal static Material RevealMaterial(string name, string shaderName)
        {
            EnsureFolder(MaterialFolder);
            string path = MaterialFolder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (m == null)
            {
                m = new Material(Shader.Find(shaderName));
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = Shader.Find(shaderName);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------
        // Scene
        // ------------------------------------------------------------------

        /// <summary>
        /// Pose le demon a 12 m du survivant, un corps visible pour le survivant et le
        /// selecteur F2. Remplace un demon deja present.
        /// </summary>
        public static void AddToOpenScene()
        {
            PlayerCharacter survivor = Object.FindAnyObjectByType<PlayerCharacter>();

            if (survivor == null)
            {
                Debug.LogError("[DemonDoll] Aucun joueur (PlayerCharacter) dans la scene.");
                return;
            }

            foreach (DemonController old in Object.FindObjectsByType<DemonController>()) Object.DestroyImmediate(old.gameObject);
            foreach (PlayableCharacterSwitcher old in Object.FindObjectsByType<PlayableCharacterSwitcher>()) Object.DestroyImmediate(old.gameObject);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject demonObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            // A 12 m devant le survivant, pose au sol, tourne vers lui.
            Vector3 start = survivor.transform.position + survivor.transform.forward * 12f;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null) start.y = terrain.SampleHeight(start) + terrain.transform.position.y + 0.05f;
            Vector3 look = survivor.transform.position - start;
            look.y = 0f;
            demonObject.transform.SetPositionAndRotation(start, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);

            DemonController demon = demonObject.GetComponent<DemonController>();
            InputReader gate = survivor.GetComponent<InputReader>();
            EditorSetupUtility.SetObjectField(demon, "inputGate", gate);

            // La camera du demon reprend le post-traitement du survivant.
            if (survivor.Camera != null)
            {
                UniversalAdditionalCameraData from = survivor.Camera.GetComponent<UniversalAdditionalCameraData>();
                UniversalAdditionalCameraData to = demon.Camera.GetComponent<UniversalAdditionalCameraData>();
                if (from != null && to != null) to.renderPostProcessing = from.renderPostProcessing;
                demon.Camera.backgroundColor = survivor.Camera.backgroundColor;
                demon.Camera.clearFlags = survivor.Camera.clearFlags;
            }

            GameObject body = EnsureSurvivorBody(survivor);
            EnsureProximityVHS(survivor);

            GameObject switcherObject = new GameObject("[DemonTest]");
            PlayableCharacterSwitcher switcher = switcherObject.AddComponent<PlayableCharacterSwitcher>();
            EditorSetupUtility.SetObjectField(switcher, "survivor", survivor);
            EditorSetupUtility.SetObjectField(switcher, "demon", demon);
            EditorSetupUtility.SetObjectField(switcher, "survivorBody", body);
            EditorSetupUtility.SetObjectField(switcher, "compass", Object.FindAnyObjectByType<CompassHud>());
            EditorSetupUtility.SetObjectField(switcher, "zoneTitle", Object.FindAnyObjectByType<ZoneTitleDisplay>());
            EditorSetupUtility.SetObjectField(switcher, "map", Object.FindAnyObjectByType<ForestMap3D>());

            Debug.Log("[DemonDoll] Demon ajoute a la scene (F2 en jeu pour le jouer).");
        }

        /// <summary>
        /// Effet VHS du survivant quand le demon approche : quad plein ecran colle a sa
        /// camera (materiau VHSOverlay) + DemonProximityVHS sur le joueur.
        /// </summary>
        private static void EnsureProximityVHS(PlayerCharacter survivor)
        {
            Camera cam = survivor.Camera;

            if (cam == null)
            {
                Debug.LogWarning("[DemonDoll] Camera du survivant introuvable : pas d'effet VHS.");
                return;
            }

            Transform existing = cam.transform.Find("VHSOverlay");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            GameObject quad = new GameObject("VHSOverlay");
            quad.transform.SetParent(cam.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.1f);
            quad.AddComponent<MeshFilter>().sharedMesh = EyeQuad();   // quad unitaire : le shader le met plein ecran
            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RevealMaterial("M_VHSOverlay", "HouseOfSilence/VHSOverlay");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;

            DemonProximityVHS vhs = survivor.GetComponent<DemonProximityVHS>();
            if (vhs == null) vhs = survivor.gameObject.AddComponent<DemonProximityVHS>();
            EditorSetupUtility.SetObjectField(vhs, "overlay", renderer);
            EditorSetupUtility.SetObjectField(vhs, "view", cam);
        }

        /// <summary>Silhouette simple du survivant (capsule 1,8 m) : cible visible quand on joue le demon.</summary>
        private static GameObject EnsureSurvivorBody(PlayerCharacter survivor)
        {
            // Modele "Modern Female Character B" si le pack est importe, sinon capsule.
            GameObject model = SurvivorBodySetup.CreateBody(survivor);
            if (model != null) return model;

            Transform existing = survivor.transform.Find("SurvivorBody");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "SurvivorBody";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(survivor.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);

            string path = MaterialFolder + "/M_SurvivorBody.mat";
            EnsureFolder(MaterialFolder);
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", new Color(0.32f, 0.36f, 0.42f));
                m.SetFloat("_Smoothness", 0.2f);
                AssetDatabase.CreateAsset(m, path);
            }

            Renderer r = body.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off; // la lampe torche du survivant est collee a la capsule
            body.SetActive(false);
            return body;
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
