using System.Collections.Generic;
using HouseOfSilence.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HouseOfSilence.EditorTools.UI
{
    /// <summary>
    /// Menus du jeu en uGUI (Canvas + Text/Button classiques, sans TextMeshPro) :
    ///  - Tools > House of Silence > UI > Create Main Menu Scene :
    ///    scene Assets/_Game/Scenes/MainMenu.unity avec JOUER / QUITTER,
    ///    placee en tete de la Scene List.
    ///  - Tools > House of Silence > UI > Add Pause Menu To Scene :
    ///    ajoute EventSystem + Canvas de pause / fin de partie a la scene ouverte.
    ///
    /// L'EventSystem utilise InputSystemUIInputModule (le projet est en
    /// Input System seul : le StandaloneInputModule leverait une exception).
    /// </summary>
    public static class MenuSetupMenu
    {
        public const string MainMenuScenePath = "Assets/_Game/Scenes/MainMenu.unity";
        public const string GameplayScenePath = "Assets/_Game/Scenes/Prototype_House.unity";

        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        private static readonly Color Ink = new Color(0.93f, 0.9f, 0.84f, 1f);
        private static readonly Color InkDim = new Color(0.62f, 0.58f, 0.52f, 1f);
        private static readonly Color ButtonNormal = new Color(0.12f, 0.1f, 0.1f, 0.92f);
        private static readonly Color ButtonHover = new Color(0.36f, 0.12f, 0.1f, 0.95f);
        private static readonly Color ButtonPressed = new Color(0.55f, 0.16f, 0.12f, 1f);

        // ------------------------------------------------------------------
        // Menu principal
        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/UI/Create Main Menu Scene", false, 300)]
        public static void CreateMainMenuScene()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[UI] Arrete le Play Mode avant de creer la scene de menu.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, MainMenuScenePath);

            // Camera : fond noir, rien d'autre a rendre que l'UI.
            GameObject cameraObject = new GameObject("MenuCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.01f, 0.01f, 0.015f, 1f);
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;
            cameraObject.AddComponent<AudioListener>();

            // Noyau persistant + hote des systemes (vide ici).
            CoreSetupMenu.CreateCoreSystems();

            EnsureEventSystem();

            // --- Canvas ----------------------------------------------------------
            GameObject canvasObject = CreateCanvas("MainMenuCanvas", 0);
            Transform canvas = canvasObject.transform;

            Image background = CreateImage(canvas, "Background", new Color(0.02f, 0.02f, 0.03f, 1f));
            Stretch(background.rectTransform);

            // Bande sombre a droite pour poser les boutons : la gauche reste libre
            // pour l'overlay de debug (F1) qui s'affiche en haut a gauche.
            const float bandX = 0.56f;
            Image band = CreateImage(canvas, "Band", new Color(0.05f, 0.04f, 0.045f, 0.9f));
            band.rectTransform.anchorMin = new Vector2(bandX, 0f);
            band.rectTransform.anchorMax = new Vector2(1f, 1f);
            band.rectTransform.offsetMin = Vector2.zero;
            band.rectTransform.offsetMax = Vector2.zero;

            Text title = CreateText(canvas, "Title", "THE HOUSE\nOF SILENCE", 84, Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            Place(title.rectTransform, new Vector2(bandX, 1f), new Vector2(0f, 1f), new Vector2(80f, -140f), new Vector2(760f, 260f));
            title.lineSpacing = 0.95f;

            Text subtitle = CreateText(canvas, "Subtitle", "Survival horror cooperatif  -  prototype", 26, InkDim, TextAnchor.UpperLeft, FontStyle.Italic);
            Place(subtitle.rectTransform, new Vector2(bandX, 1f), new Vector2(0f, 1f), new Vector2(84f, -400f), new Vector2(760f, 40f));

            Button play = CreateButton(canvas, "Button_Play", "JOUER", 34);
            Place(((RectTransform)play.transform), new Vector2(bandX, 0.5f), new Vector2(0f, 0.5f), new Vector2(80f, -20f), new Vector2(380f, 72f));

            Button quit = CreateButton(canvas, "Button_Quit", "QUITTER", 34);
            Place(((RectTransform)quit.transform), new Vector2(bandX, 0.5f), new Vector2(0f, 0.5f), new Vector2(80f, -112f), new Vector2(380f, 72f));

            Text hint = CreateText(canvas, "Hint", "Z Q S D se deplacer   -   Shift courir   -   E interagir   -   Echap pause", 20, InkDim, TextAnchor.LowerLeft, FontStyle.Normal);
            Place(hint.rectTransform, new Vector2(bandX, 0f), new Vector2(0f, 0f), new Vector2(80f, 96f), new Vector2(800f, 30f));

            Text version = CreateText(canvas, "Version", "", 18, new Color(0.45f, 0.42f, 0.38f, 1f), TextAnchor.LowerLeft, FontStyle.Normal);
            Place(version.rectTransform, new Vector2(bandX, 0f), new Vector2(0f, 0f), new Vector2(80f, 48f), new Vector2(800f, 26f));

            MainMenuController controller = canvasObject.AddComponent<MainMenuController>();
            EditorSetupUtility.SetObjectField(controller, "playButton", play);
            EditorSetupUtility.SetObjectField(controller, "quitButton", quit);
            EditorSetupUtility.SetObjectField(controller, "versionText", version);

            // Navigation clavier / manette : JOUER <-> QUITTER.
            LinkVertical(play, quit);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EnsureSceneListOrder();

            Debug.Log("[UI] Scene de menu principal creee : " + MainMenuScenePath);
        }

        // ------------------------------------------------------------------
        // Menu de pause
        // ------------------------------------------------------------------

        [MenuItem("Tools/House of Silence/UI/Add Pause Menu To Scene", false, 301)]
        public static void AddPauseMenu()
        {
            PauseMenuController existing = Object.FindAnyObjectByType<PauseMenuController>(FindObjectsInactive.Include);

            if (existing != null)
            {
                Debug.LogWarning("[UI] Un menu de pause existe deja dans la scene.", existing);
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            EnsureEventSystem();

            GameObject canvasObject = CreateCanvas("PauseMenuCanvas", 50);
            Transform canvas = canvasObject.transform;

            // Racine activee / desactivee par le controleur.
            GameObject root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(canvas, false);
            Stretch((RectTransform)root.transform);

            Image dim = CreateImage(root.transform, "Dim", new Color(0f, 0f, 0f, 0.7f));
            Stretch(dim.rectTransform);

            Image panel = CreateImage(root.transform, "Panel", new Color(0.05f, 0.045f, 0.05f, 0.96f));
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 470f));

            Text title = CreateText(panel.transform, "Title", "PAUSE", 44, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(520f, 60f));

            Text hint = CreateText(panel.transform, "Hint", "Echap pour reprendre", 20, InkDim, TextAnchor.MiddleCenter, FontStyle.Italic);
            Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(520f, 30f));

            // Les boutons s'empilent dans un layout : un bouton masque (Reprendre en
            // fin de partie, Rejouer en pause) ne laisse pas de trou.
            GameObject buttonsObject = new GameObject("Buttons", typeof(RectTransform));
            buttonsObject.transform.SetParent(panel.transform, false);
            RectTransform buttons = (RectTransform)buttonsObject.transform;
            Place(buttons, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(400f, 330f));

            VerticalLayoutGroup layout = buttonsObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            Button resume = CreateButton(buttons, "Button_Resume", "REPRENDRE", 28);
            Button restart = CreateButton(buttons, "Button_Restart", "REJOUER", 28);
            Button mainMenu = CreateButton(buttons, "Button_MainMenu", "MENU PRINCIPAL", 28);
            Button quit = CreateButton(buttons, "Button_Quit", "QUITTER", 28);

            foreach (Button b in new[] { resume, restart, mainMenu, quit })
            {
                LayoutElement element = b.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = 64f;
                element.minHeight = 64f;
            }

            LinkVertical(resume, restart);
            LinkVertical(restart, mainMenu);
            LinkVertical(mainMenu, quit);

            PauseMenuController controller = canvasObject.AddComponent<PauseMenuController>();
            EditorSetupUtility.SetObjectField(controller, "root", root);
            EditorSetupUtility.SetObjectField(controller, "titleText", title);
            EditorSetupUtility.SetObjectField(controller, "hintText", hint);
            EditorSetupUtility.SetObjectField(controller, "resumeButton", resume);
            EditorSetupUtility.SetObjectField(controller, "restartButton", restart);
            EditorSetupUtility.SetObjectField(controller, "mainMenuButton", mainMenu);
            EditorSetupUtility.SetObjectField(controller, "quitButton", quit);

            // Cache en edition : le controleur l'affiche a la pause.
            root.SetActive(false);

            Selection.activeGameObject = canvasObject;
            EditorSceneManager.MarkSceneDirty(canvasObject.scene);

            Debug.Log("[UI] Menu de pause ajoute (Echap en jeu).", canvasObject);
        }

        // ------------------------------------------------------------------
        // Scene List : menu principal en premier, puis le niveau
        // ------------------------------------------------------------------

        public static void EnsureSceneListOrder()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            scenes.RemoveAll(s => s.path == MainMenuScenePath || s.path == GameplayScenePath);

            List<EditorBuildSettingsScene> ordered = new List<EditorBuildSettingsScene>();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) != null)
            {
                ordered.Add(new EditorBuildSettingsScene(MainMenuScenePath, true));
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameplayScenePath) != null)
            {
                ordered.Add(new EditorBuildSettingsScene(GameplayScenePath, true));
            }

            ordered.AddRange(scenes);
            EditorBuildSettings.scenes = ordered.ToArray();
        }

        // ------------------------------------------------------------------
        // Briques uGUI
        // ------------------------------------------------------------------

        public static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);

            if (eventSystem == null)
            {
                GameObject go = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
                eventSystem = go.AddComponent<EventSystem>();
            }

            // Jamais de StandaloneInputModule : l'ancien Input est desactive dans ce projet.
            StandaloneInputModule legacy = eventSystem.GetComponent<StandaloneInputModule>();

            if (legacy != null)
            {
                Object.DestroyImmediate(legacy);
            }

            InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();

            if (module == null)
            {
                module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

            if (actions != null && module.actionsAsset != actions)
            {
                // L'affectation retrouve d'elle-meme Point / Click / Navigate / Submit / Cancel dans la map "UI".
                module.actionsAsset = actions;
            }

            EditorUtility.SetDirty(eventSystem.gameObject);
        }

        private static GameObject CreateCanvas(string name, int sortingOrder)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string content, int size, Color color, TextAnchor anchor, FontStyle style)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.text = content;
            text.font = DefaultFont();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, int fontSize)
        {
            Image image = CreateImage(parent, name, ButtonNormal);

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = ButtonNormal;
            colors.highlightedColor = ButtonHover;
            colors.selectedColor = ButtonHover;
            colors.pressedColor = ButtonPressed;
            colors.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            // Fin lisere clair a gauche, comme un marque-page.
            Image edge = CreateImage(image.transform, "Edge", new Color(0.75f, 0.2f, 0.15f, 1f));
            edge.raycastTarget = false;
            edge.rectTransform.anchorMin = new Vector2(0f, 0f);
            edge.rectTransform.anchorMax = new Vector2(0f, 1f);
            edge.rectTransform.pivot = new Vector2(0f, 0.5f);
            edge.rectTransform.anchoredPosition = Vector2.zero;
            edge.rectTransform.sizeDelta = new Vector2(6f, 0f);

            Text text = CreateText(image.transform, "Label", label, fontSize, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(text.rectTransform);

            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            button.navigation = navigation;

            return button;
        }

        private static void LinkVertical(Button upper, Button lower)
        {
            Navigation up = upper.navigation;
            up.mode = Navigation.Mode.Explicit;
            up.selectOnDown = lower;
            upper.navigation = up;

            Navigation low = lower.navigation;
            low.mode = Navigation.Mode.Explicit;
            low.selectOnUp = upper;
            lower.navigation = low;
        }

        /// <summary>Ancre un element a un point du parent, avec son pivot, sa position et sa taille en pixels de reference.</summary>
        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Font DefaultFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return font;
        }
    }
}
