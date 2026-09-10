using System;
using System.Collections.Generic;
using System.Text;
using HouseOfSilence.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HouseOfSilence.Core.Debugging
{
    /// <summary>
    /// Console de debug runtime.
    ///
    /// F1 : affiche / masque l'overlay.
    /// Les autres touches sont enregistrees par les systemes concernes via
    /// Register(), pour que ce fichier n'ait aucune dependance sur eux.
    ///
    ///     DebugManager.Register(Key.F2, "Spawn monster", SpawnMonster);   // OnEnable
    ///     DebugManager.Unregister(Key.F2);                                // OnDisable
    ///
    /// Meme principe pour les lignes d'information de l'overlay :
    ///
    ///     DebugManager.RegisterInfo("Player", () => "Speed : " + speed);
    ///     DebugManager.UnregisterInfo("Player");
    ///
    /// Dependance : com.unity.inputsystem (Input System).
    /// </summary>
    [DisallowMultipleComponent]
    public class DebugManager : MonoSingleton<DebugManager>
    {
        /// <summary>Une commande de debug associee a une touche.</summary>
        public class DebugCommand
        {
            public Key Key;
            public string Label;
            public Action Action;
        }

        /// <summary>Un bloc d'information affiche dans l'overlay.</summary>
        public class DebugInfo
        {
            public string Id;
            public Func<string> Provider;
        }

        [Header("Activation")]
        [Tooltip("Si false, le debug est totalement inactif (aucun input, aucun affichage).")]
        [SerializeField] private bool debugEnabled = true;

        [Tooltip("Autoriser le debug dans une build release. A laisser sur false pour la version finale.")]
        [SerializeField] private bool allowInReleaseBuild = false;

        [Header("Overlay")]
        [SerializeField] private bool overlayVisible = true;
        [SerializeField] private bool showLogHistory = true;
        [SerializeField, Range(4, 30)] private int maxLogLines = 10;
        [SerializeField, Range(260f, 700f)] private float overlayWidth = 400f;
        [SerializeField] private KeyLabel overlayToggleKey = KeyLabel.F1;

        /// <summary>Sous-ensemble de touches proposees dans l'Inspector.</summary>
        public enum KeyLabel
        {
            F1,
            F2,
            F9,
            F10,
            F12,
            Backquote
        }

        private readonly List<DebugCommand> _commands = new List<DebugCommand>(16);
        private readonly List<DebugInfo> _infos = new List<DebugInfo>(8);
        private readonly List<string> _logLines = new List<string>(32);
        private readonly StringBuilder _builder = new StringBuilder(1024);

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private float _fpsSmoothed;
        private bool _logHooked;

        /// <summary>Vrai si le systeme de debug est reellement actif dans ce contexte.</summary>
        public bool IsActive
        {
            get { return debugEnabled && (Application.isEditor || UnityEngine.Debug.isDebugBuild || allowInReleaseBuild); }
        }

        // ------------------------------------------------------------------
        // API statique (sure meme si le manager n'existe pas encore)
        // ------------------------------------------------------------------

        /// <summary>Associe une touche a une action de debug. Remplace la commande existante sur cette touche.</summary>
        public static void Register(Key key, string label, Action action)
        {
            DebugManager manager = Instance;

            if (manager == null || action == null)
            {
                return;
            }

            manager.RegisterInternal(key, label, action);
        }

        /// <summary>Retire la commande associee a une touche.</summary>
        public static void Unregister(Key key)
        {
            if (!HasInstance)
            {
                return;
            }

            DebugManager manager = Instance;

            if (manager != null)
            {
                manager.UnregisterInternal(key);
            }
        }

        /// <summary>
        /// Ajoute un bloc d'information a l'overlay. Le provider est appele a chaque frame
        /// d'affichage : il doit rester tres leger et ne rien allouer d'inutile.
        /// </summary>
        public static void RegisterInfo(string id, Func<string> provider)
        {
            DebugManager manager = Instance;

            if (manager == null || provider == null || string.IsNullOrEmpty(id))
            {
                return;
            }

            manager.RegisterInfoInternal(id, provider);
        }

        /// <summary>Retire un bloc d'information de l'overlay.</summary>
        public static void UnregisterInfo(string id)
        {
            if (!HasInstance || string.IsNullOrEmpty(id))
            {
                return;
            }

            DebugManager manager = Instance;

            if (manager != null)
            {
                manager.UnregisterInfoInternal(id);
            }
        }

        private void RegisterInternal(Key key, string label, Action action)
        {
            for (int i = 0; i < _commands.Count; i++)
            {
                if (_commands[i].Key == key)
                {
                    _commands[i].Label = label;
                    _commands[i].Action = action;
                    return;
                }
            }

            DebugCommand command = new DebugCommand();
            command.Key = key;
            command.Label = label;
            command.Action = action;

            _commands.Add(command);
        }

        private void UnregisterInternal(Key key)
        {
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                if (_commands[i].Key == key)
                {
                    _commands.RemoveAt(i);
                }
            }
        }

        private void RegisterInfoInternal(string id, Func<string> provider)
        {
            for (int i = 0; i < _infos.Count; i++)
            {
                if (_infos[i].Id == id)
                {
                    _infos[i].Provider = provider;
                    return;
                }
            }

            DebugInfo info = new DebugInfo();
            info.Id = id;
            info.Provider = provider;

            _infos.Add(info);
        }

        private void UnregisterInfoInternal(string id)
        {
            for (int i = _infos.Count - 1; i >= 0; i--)
            {
                if (_infos[i].Id == id)
                {
                    _infos.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            if (!IsActive)
            {
                return;
            }

            if (showLogHistory && !_logHooked)
            {
                Application.logMessageReceived += OnLogMessageReceived;
                _logHooked = true;
            }
        }

        private void Update()
        {
            if (!IsActive)
            {
                return;
            }

            // FPS lisse (temps non affecte par la pause).
            float unscaled = Time.unscaledDeltaTime;

            if (unscaled > 0.0001f)
            {
                _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, 1f / unscaled, 0.1f);
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return;
            }

            if (keyboard[ToKey(overlayToggleKey)].wasPressedThisFrame)
            {
                overlayVisible = !overlayVisible;
            }

            // On identifie d'abord la commande, puis on l'invoque : une action
            // peut ainsi modifier la liste sans casser l'iteration.
            Action pending = null;

            for (int i = 0; i < _commands.Count; i++)
            {
                DebugCommand command = _commands[i];

                if (command == null || command.Action == null || command.Key == Key.None)
                {
                    continue;
                }

                if (keyboard[command.Key].wasPressedThisFrame)
                {
                    pending = command.Action;
                    break;
                }
            }

            if (pending != null)
            {
                try
                {
                    pending.Invoke();
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }
        }

        private static Key ToKey(KeyLabel label)
        {
            switch (label)
            {
                case KeyLabel.F2: return Key.F2;
                case KeyLabel.F9: return Key.F9;
                case KeyLabel.F10: return Key.F10;
                case KeyLabel.F12: return Key.F12;
                case KeyLabel.Backquote: return Key.Backquote;
                default: return Key.F1;
            }
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log)
            {
                return;
            }

            _logLines.Add(type + " : " + condition);

            while (_logLines.Count > maxLogLines)
            {
                _logLines.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------------
        // Overlay IMGUI (aucune dependance UI / TextMeshPro)
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!IsActive || !overlayVisible)
            {
                return;
            }

            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box);
                _boxStyle.alignment = TextAnchor.UpperLeft;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.alignment = TextAnchor.UpperLeft;
                _labelStyle.fontSize = 12;
                _labelStyle.richText = true;
                _labelStyle.wordWrap = true;
                _labelStyle.normal.textColor = Color.white;
            }

            string text = BuildOverlayText();
            GUIContent content = new GUIContent(text);

            float innerWidth = overlayWidth - 24f;
            float height = _labelStyle.CalcHeight(content, innerWidth) + 20f;

            GUI.Box(new Rect(8f, 8f, overlayWidth, height), GUIContent.none, _boxStyle);
            GUI.Label(new Rect(20f, 18f, innerWidth, height - 20f), content, _labelStyle);
        }

        private string BuildOverlayText()
        {
            _builder.Length = 0;

            _builder.Append("<b>THE HOUSE OF SILENCE - DEBUG</b>  [").Append(overlayToggleKey).Append("]\n");

            GameManager game = GameManager.HasInstance ? GameManager.Instance : null;

            if (game != null)
            {
                _builder.Append("State      : ").Append(game.State).Append('\n');
                _builder.Append("Mode       : ").Append(game.Mode).Append("  /  Role : ").Append(game.Role).Append('\n');
                _builder.Append("PlayTime   : ").Append(game.PlayTime.ToString("F1")).Append(" s   LastResult : ").Append(game.LastResult).Append('\n');
            }
            else
            {
                _builder.Append("State      : (aucun GameManager)\n");
            }

            SceneLoader loader = SceneLoader.HasInstance ? SceneLoader.Instance : null;

            _builder.Append("Scene      : ").Append(SceneManager.GetActiveScene().name);

            if (loader != null && loader.IsLoading)
            {
                _builder.Append("   -> ").Append(loader.LoadingSceneName).Append(' ').Append(Mathf.RoundToInt(loader.Progress * 100f)).Append('%');
            }

            _builder.Append('\n');
            _builder.Append("FPS        : ").Append(Mathf.RoundToInt(_fpsSmoothed));
            _builder.Append("   timeScale : ").Append(Time.timeScale.ToString("F2"));
            _builder.Append("   EventBus : ").Append(EventBus.SubscribedTypeCount).Append('\n');

            for (int i = 0; i < _infos.Count; i++)
            {
                DebugInfo info = _infos[i];

                if (info == null || info.Provider == null)
                {
                    continue;
                }

                string line;

                try
                {
                    line = info.Provider.Invoke();
                }
                catch (Exception exception)
                {
                    line = "(erreur) " + exception.Message;
                }

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                _builder.Append('\n').Append("<b>").Append(info.Id).Append("</b>\n");
                _builder.Append(line).Append('\n');
            }

            _builder.Append("\n<b>Commandes</b>\n");

            if (_commands.Count == 0)
            {
                _builder.Append("  (aucune)\n");
            }
            else
            {
                for (int i = 0; i < _commands.Count; i++)
                {
                    _builder.Append("  ").Append(_commands[i].Key).Append(" : ").Append(_commands[i].Label).Append('\n');
                }
            }

            if (showLogHistory && _logLines.Count > 0)
            {
                _builder.Append("\n<b>Warnings / Erreurs</b>\n");

                for (int i = 0; i < _logLines.Count; i++)
                {
                    _builder.Append("  ").Append(_logLines[i]).Append('\n');
                }
            }

            return _builder.ToString();
        }

        protected override void OnDestroy()
        {
            if (_logHooked)
            {
                Application.logMessageReceived -= OnLogMessageReceived;
                _logHooked = false;
            }

            _commands.Clear();
            _infos.Clear();
            _logLines.Clear();

            base.OnDestroy();
        }
    }
}
