using System.Collections.Generic;
using HouseOfSilence.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace HouseOfSilence.EditorTools.Characters
{
    /// <summary>
    /// Corps visible du survivant : "Modern Female Character B" (Alpen Wolf,
    /// Assets/civilian_girl) a la place de la capsule. Materiaux Standard convertis en
    /// URP Lit, taille ramenee a 1,70 m, Idle genere par code (debout, respiration,
    /// leger balancement). Le corps n'est affiche que quand on joue le demon (F2).
    /// </summary>
    public static class SurvivorBodySetup
    {
        private const string PackPrefab = "Assets/civilian_girl/Prefabs/civilian_girl.prefab";
        private const string PackFbx = "Assets/civilian_girl/Models/civilian_girl.FBX";
        private const string Root = "Assets/_Game/Art/Survivor";
        private const string MaterialFolder = Root + "/Materials";
        private const string AnimFolder = Root + "/Animations";
        private const string ControllerPath = AnimFolder + "/Survivor.controller";

        /// <summary>Taille voulue du personnage (sommet des cheveux), en metres.</summary>
        private const float TargetHeight = 1.70f;

        [MenuItem("Tools/House of Silence/Characters/Replace Survivor Capsule With Female Character")]
        public static void ReplaceInOpenScene()
        {
            PlayerCharacter survivor = Object.FindAnyObjectByType<PlayerCharacter>();

            if (survivor == null)
            {
                Debug.LogError("[Survivor] Aucun joueur (PlayerCharacter) dans la scene.");
                return;
            }

            GameObject body = CreateBody(survivor);
            if (body == null) return;

            // Le selecteur F2 affiche ce corps quand on joue le demon.
            foreach (Demon.PlayableCharacterSwitcher switcher in Object.FindObjectsByType<Demon.PlayableCharacterSwitcher>())
            {
                EditorSetupUtility.SetObjectField(switcher, "survivorBody", body);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(survivor.gameObject.scene);
        }

        /// <summary>
        /// Remplace l'ancien "SurvivorBody" du joueur par le personnage. Renvoie null si le
        /// pack n'est pas importe (l'appelant garde alors la capsule).
        /// </summary>
        public static GameObject CreateBody(PlayerCharacter survivor)
        {
            GameObject pack = AssetDatabase.LoadAssetAtPath<GameObject>(PackPrefab);

            if (pack == null)
            {
                return null;
            }

            Avatar avatar = LoadAvatar();

            if (avatar == null || !avatar.isHuman)
            {
                Debug.LogWarning("[Survivor] Avatar Humanoid introuvable pour " + PackFbx + " : capsule conservee.");
                return null;
            }

            Dictionary<Material, Material> materials = ConvertMaterials(pack);
            AnimatorController controller = BuildController(avatar);

            Transform existing = survivor.transform.Find("SurvivorBody");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(pack);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = "SurvivorBody";
            body.transform.SetParent(survivor.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;

            // Taille : sommet du modele ramene a TargetHeight.
            Bounds bounds = new Bounds(body.transform.position, Vector3.zero);
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(r.bounds);
            float height = bounds.max.y - body.transform.position.y;
            body.transform.localScale = Vector3.one * (height > 0.1f ? TargetHeight / height : 1f);

            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material converted;
                    if (mats[i] != null && materials.TryGetValue(mats[i], out converted)) mats[i] = converted;
                }

                r.sharedMaterials = mats;
                // La lampe torche du survivant est tenue contre le corps : pas d'ombre portee.
                r.shadowCastingMode = ShadowCastingMode.Off;

                if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }

            Animator animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Etat mort / vivant du survivant (tue par DemonKill).
            Demon.SurvivorDeath death = survivor.GetComponent<Demon.SurvivorDeath>();
            if (death == null) death = survivor.gameObject.AddComponent<Demon.SurvivorDeath>();
            EditorSetupUtility.SetObjectField(death, "bodyAnimator", animator);

            // Jumpscare de mort vu par les yeux du survivant (DemonKill).
            Camera view = survivor.Camera;
            if (view != null)
            {
                Transform voiceTransform = view.transform.Find("JumpscareAudio");
                if (voiceTransform != null) Object.DestroyImmediate(voiceTransform.gameObject);
                GameObject voice = new GameObject("JumpscareAudio");
                voice.transform.SetParent(view.transform, false);
                AudioSource source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;

                Transform lightTransform = view.transform.Find("JumpscareLight");
                if (lightTransform != null) Object.DestroyImmediate(lightTransform.gameObject);
                GameObject lightObject = new GameObject("JumpscareLight");
                lightObject.transform.SetParent(view.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 0.05f, 0.05f);
                Light faceLight = lightObject.AddComponent<Light>();
                faceLight.type = LightType.Point;
                faceLight.color = new Color(0.78f, 0.85f, 1f);
                faceLight.range = 2.5f;
                faceLight.intensity = 0f;
                faceLight.shadows = LightShadows.None;
                faceLight.enabled = false;
                lightObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();

                Demon.SurvivorJumpscare scare = survivor.GetComponent<Demon.SurvivorJumpscare>();
                if (scare == null) scare = survivor.gameObject.AddComponent<Demon.SurvivorJumpscare>();
                EditorSetupUtility.SetObjectField(scare, "view", view);
                EditorSetupUtility.SetObjectField(scare, "body", body);
                EditorSetupUtility.SetObjectField(scare, "audioSource", source);
                EditorSetupUtility.SetObjectField(scare, "faceLight", faceLight);
                EditorSetupUtility.SetObjectField(scare, "scream", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Game/Audio/Demon/horror-demon-cry.mp3"));
                EditorSetupUtility.SetObjectField(scare, "heartbeat", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Game/Audio/horror/freesound_community-heart-beating-5857.mp3"));
            }

            body.SetActive(false);
            return body;
        }

        private static Avatar LoadAvatar()
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(PackFbx))
            {
                if (asset is Avatar a) return a;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Materiaux
        // ------------------------------------------------------------------

        /// <summary>Standard -> URP Lit (memes textures ; la carte MetallicOcclusionSmoothness sert aux deux).</summary>
        private static Dictionary<Material, Material> ConvertMaterials(GameObject pack)
        {
            DemonDollSetup.EnsureFolder(MaterialFolder);
            Dictionary<Material, Material> map = new Dictionary<Material, Material>();
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
                    m.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                    m.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);

                    Texture normal = source.HasProperty("_BumpMap") ? source.GetTexture("_BumpMap") : null;
                    if (normal != null)
                    {
                        m.SetTexture("_BumpMap", normal);
                        m.EnableKeyword("_NORMALMAP");
                    }

                    Texture metallic = source.HasProperty("_MetallicGlossMap") ? source.GetTexture("_MetallicGlossMap") : null;
                    if (metallic != null)
                    {
                        m.SetTexture("_MetallicGlossMap", metallic);
                        m.SetFloat("_Smoothness", source.HasProperty("_GlossMapScale") ? source.GetFloat("_GlossMapScale") : 1f);
                        m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    }
                    else
                    {
                        m.SetFloat("_Metallic", 0f);
                        m.SetFloat("_Smoothness", source.name.Contains("eye") ? 0.85f : 0.3f);
                    }

                    Texture occlusion = source.HasProperty("_OcclusionMap") ? source.GetTexture("_OcclusionMap") : null;
                    if (occlusion != null)
                    {
                        m.SetTexture("_OcclusionMap", occlusion);
                        m.SetFloat("_OcclusionStrength", 1f);
                        m.EnableKeyword("_OCCLUSIONMAP");
                    }

                    // Cheveux et cils : decoupe + double face.
                    bool clip = source.HasProperty("_Mode") && source.GetFloat("_Mode") >= 1f;
                    m.SetFloat("_Surface", 0f);
                    m.SetFloat("_AlphaClip", clip ? 1f : 0f);
                    m.SetFloat("_Cutoff", clip ? source.GetFloat("_Cutoff") : 0.5f);
                    m.SetOverrideTag("RenderType", clip ? "TransparentCutout" : "Opaque");
                    m.renderQueue = clip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
                    if (clip) m.EnableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Cull", clip ? (float)CullMode.Off : (float)CullMode.Back);

                    EditorUtility.SetDirty(m);
                    map[source] = m;
                }
            }

            return map;
        }

        // ------------------------------------------------------------------
        // Animation
        // ------------------------------------------------------------------

        /// <summary>Debout, bras le long du corps, respiration, regard qui balaie, poids qui passe d'une jambe a l'autre.</summary>
        private static void IdlePose(DemonDollSetup.Poser p, float phase)
        {
            DemonDollSetup.HangArms(p, 0.1f, 0.07f);

            float breath = DemonDollSetup.Wave(phase * 3f);
            float sway = DemonDollSetup.Wave(phase);

            p.Move("pelvis", Vector3.up * 0.004f * breath + Vector3.right * 0.012f * sway);
            p.Rot("pelvis", DemonDollSetup.Z, 1.5f * sway);
            p.Rot("spine_01", DemonDollSetup.Z, -1.2f * sway);
            p.Rot("spine_03", DemonDollSetup.X, -1.2f * breath);
            p.Rot("clavicle_l", DemonDollSetup.Z, -1.5f * breath);
            p.Rot("clavicle_r", DemonDollSetup.Z, 1.5f * breath);

            // Regarde autour d'elle, inquiete.
            p.Rot("neck_01", DemonDollSetup.Y, 14f * DemonDollSetup.Wave(phase, 0.1f));
            p.Rot("head", DemonDollSetup.Y, 8f * DemonDollSetup.Wave(phase * 2f, 0.3f));
            p.Rot("head", DemonDollSetup.X, 3f);

            p.Rot("upperarm_l", DemonDollSetup.X, 2f * DemonDollSetup.Wave(phase, 0.25f));
            p.Rot("upperarm_r", DemonDollSetup.X, 2f * DemonDollSetup.Wave(phase, 0.75f));
        }

        /// <summary>
        /// Mort (2,6 s, sans boucle) : sursaut en arriere, mains a la gorge, les genoux
        /// lachent et elle tombe a la renverse ; derniere pose tenue, allongee sur le dos.
        /// </summary>
        private static void DeathPose(DemonDollSetup.Poser p, float t)
        {
            float flinch = DemonDollSetup.Keys(t, 0f, 0f, 0.1f, 1f, 0.45f, 1f, 0.6f, 0.3f);
            float buckle = DemonDollSetup.Keys(t, 0.35f, 0f, 0.6f, 1f, 0.72f, 0.6f, 1f, 0f);
            float fall = DemonDollSetup.Keys(t, 0.5f, 0f, 0.85f, 1f);
            float shake = t > 0.1f && t < 0.5f ? Mathf.Sin(t * Mathf.PI * 2f * 9f) : 0f;

            // Recule, s'affaisse, puis bascule en arriere jusqu'au sol (le bassin a ~12 cm).
            Transform pelvis = p["pelvis"];
            float standHeight = pelvis.position.y;
            p.Move("pelvis", Vector3.back * (0.15f * flinch + 0.35f * fall) + Vector3.down * (0.25f * buckle + (standHeight - 0.14f - 0.25f * buckle) * fall));
            p.Rot("pelvis", DemonDollSetup.X, -82f * fall);
            p.Rot("pelvis", DemonDollSetup.Z, 6f * fall);

            // Buste rejete en arriere, tete qui part en arriere puis retombe sur le cote.
            p.Rot("spine_01", DemonDollSetup.X, -12f * flinch + 8f * buckle);
            p.Rot("spine_03", DemonDollSetup.Y, 6f * shake);
            p.Rot("neck_01", DemonDollSetup.X, -20f * flinch + 10f * fall);
            p.Rot("head", DemonDollSetup.X, -15f * flinch);
            p.Rot("head", DemonDollSetup.Y, 35f * fall);

            // Jambes : genoux qui lachent, puis etendues au sol.
            foreach (int side in new[] { -1, 1 })
            {
                string sfx = side < 0 ? "_l" : "_r";
                p.Rot("thigh" + sfx, DemonDollSetup.X, -40f * buckle * (1f - fall) + 6f * fall);
                p.Rot("calf" + sfx, DemonDollSetup.X, 70f * buckle * (1f - fall) + (side < 0 ? 25f : 8f) * fall);
                p.Rot("thigh" + sfx, DemonDollSetup.Z, side * 6f * fall);
            }

            // Bras : mains a la gorge, puis ecartes au sol.
            Transform head = p["head"];
            Vector3 throat = head.position + Vector3.down * 0.1f + Vector3.forward * 0.08f;

            foreach (int side in new[] { -1, 1 })
            {
                string sfx = side < 0 ? "_l" : "_r";
                float grab = Mathf.Clamp01(flinch * (1f - fall));

                if (grab > 0.01f)
                {
                    Vector3 elbow = throat + new Vector3(side * 0.22f, -0.2f, 0.05f);
                    p.Aim("upperarm" + sfx, "lowerarm" + sfx, Vector3.Lerp(new Vector3(side * 0.12f, -1f, 0.05f), elbow - p["upperarm" + sfx].position, grab));
                    p.Aim("lowerarm" + sfx, "hand" + sfx, Vector3.Lerp(new Vector3(side * 0.1f, -1f, 0.1f), throat + new Vector3(side * 0.05f, 0f, 0f) - p["lowerarm" + sfx].position, grab));
                }

                if (fall > 0.01f)
                {
                    // Au sol : bras ecartes, a plat.
                    Vector3 spread = new Vector3(side * 1f, -0.25f, -0.45f);
                    Transform upper = p["upperarm" + sfx];
                    Vector3 current = p["lowerarm" + sfx].position - upper.position;
                    p.Aim("upperarm" + sfx, "lowerarm" + sfx, Vector3.Slerp(current.normalized, spread.normalized, fall));
                    Vector3 fore = p["hand" + sfx].position - p["lowerarm" + sfx].position;
                    p.Aim("lowerarm" + sfx, "hand" + sfx, Vector3.Slerp(fore.normalized, (spread + Vector3.back * 0.3f).normalized, fall));
                }
            }
        }

        private static AnimatorController BuildController(Avatar avatar)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(PackFbx);
            GameObject instance = Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            AnimationClip idle;
            AnimationClip death;

            try
            {
                DemonDollSetup.Poser poser = new DemonDollSetup.Poser(instance);
                HumanPoseHandler handler = new HumanPoseHandler(avatar, instance.transform);
                idle = DemonDollSetup.Record("Survivor_Idle", poser, handler, IdlePose, 6f, AnimFolder);
                death = DemonDollSetup.Record("Survivor_Death", poser, handler, DeathPose, 2.6f, AnimFolder, false);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            // Repart d'un controleur propre (meme asset : les references restent valides).
            while (controller.parameters.Length > 0) controller.RemoveParameter(0);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states) machine.RemoveState(child.state);

            AnimatorState state = machine.AddState("Idle");
            state.motion = idle;
            machine.defaultState = state;

            // Mort (SurvivorDeath) : "Die" -> Death (derniere pose tenue), "Revive" -> Idle.
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Revive", AnimatorControllerParameterType.Trigger);
            AnimatorState dead = machine.AddState("Death");
            dead.motion = death;

            AnimatorStateTransition toDeath = state.AddTransition(dead);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.1f;
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");

            AnimatorStateTransition toIdle = dead.AddTransition(state);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.4f;
            toIdle.AddCondition(AnimatorConditionMode.If, 0f, "Revive");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }
    }
}
