using System;
using System.Collections;
using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Demon;
using HouseOfSilence.Level;
using HouseOfSilence.Player;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HouseOfSilence.Network
{
    /// <summary>Etape du multijoueur, vue par ce joueur.</summary>
    public enum NetPhase
    {
        Menu,
        Connecting,
        Lobby,
        Loading,
        Playing,
        GameOver,
        Solo
    }

    /// <summary>
    /// Coeur du multijoueur (Netcode for GameObjects + Unity Relay), en hote / clients.
    ///
    /// - Cree le NetworkManager par code et porte la messagerie (messages nommes : aucun
    ///   NetworkObject ni prefab reseau a preparer). Les autres systemes s'y abonnent
    ///   (Register) et envoient par SendToServer / SendTo / SendToAll / SendToOthers.
    /// - Enchaine les etapes : menu -> lobby (LobbyManager) -> chargement synchronise de la
    ///   scene (l'hote commande, chacun recharge, l'hote attend que tout le monde soit pret)
    ///   -> partie (roles par RoleManager, etat par GameStateManager, heure et meteo par
    ///   NetworkTimeOfDay) -> fin de partie -> retour au lobby.
    /// - Synchronise les personnages : ~20 etats/s par joueur (position, orientation,
    ///   regard, lampe, pleurs), relayes par l'hote qui verifie que l'emetteur joue bien ce
    ///   personnage ; les actions du demon (NetBridge) sont rejouees chez les autres.
    ///
    /// Persistant (DontDestroyOnLoad) : l'exemplaire de la scene rechargee se detruit.
    /// Les autres composants reseau sont ajoutes ici, sur le meme objet.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public class NetworkGameManager : MonoBehaviour
    {
        // Messages de ce composant.
        private const string StateMessage = "HOS_State";
        private const string EventMessage = "HOS_Event";
        private const string StartMessage = "HOS_Start";
        private const string LoadMessage = "HOS_Load";
        private const string LoadedMessage = "HOS_Loaded";

        public const byte EventKill = 1;
        private const byte EventScream = 2;
        private const byte EventTeleport = 3;
        private const byte EventDisguise = 4;
        public const byte EventRevive = 5;

        [Header("Session")]
        [SerializeField, Range(2, 5)] private int maxPlayers = 5;
        [SerializeField] private bool showMenuAtStart = true;
        [Tooltip("Touche : quitter la partie en ligne / rouvrir le menu multijoueur en solo.")]
        [SerializeField] private string menuBinding = "<Keyboard>/f12";
        [Tooltip("Attente maximale du chargement de tous les joueurs (s).")]
        [SerializeField, Min(5f)] private float loadTimeout = 30f;

        [Header("Synchronisation")]
        [SerializeField, Range(5f, 60f)] private float statesPerSecond = 20f;

        [Header("Meteo (NetworkTimeOfDay)")]
        [SerializeField] private AudioClip[] thunderClips = new AudioClip[0];

        private readonly Dictionary<string, CustomMessagingManager.HandleNamedMessageDelegate> _handlers =
            new Dictionary<string, CustomMessagingManager.HandleNamedMessageDelegate>();
        private bool _handlersRegistered;

        private InputReader[] _readers = new InputReader[0];
        private InputAction _menuAction;
        private bool _menuPressed;
        private float _nextState;

        private readonly HashSet<ulong> _loaded = new HashSet<ulong>();
        private float _loadDeadline;
        private bool _roundBegun;
        private bool _sceneDirty;

        private GUIStyle _smallStyle;

        public static NetworkGameManager Instance { get; private set; }

        public NetPhase Phase { get; private set; } = NetPhase.Solo;
        public string Status { get; set; } = "";
        public int MaxPlayers { get { return maxPlayers; } }
        public AudioClip[] ThunderClips { get { return thunderClips; } }

        public LobbyManager Lobby { get; private set; }
        public GameStateManager GameState { get; private set; }
        public NetworkTimeOfDay TimeOfDay { get; private set; }

        /// <summary>References de la scene courante (retrouvees a chaque chargement).</summary>
        public PlayableCharacterSwitcher Switcher { get; private set; }
        public RandomSpawner Spawner { get; private set; }

        /// <summary>Personnages de la partie, dans le meme ordre chez tous (survivants puis demon).</summary>
        public List<Component> Characters { get; private set; } = new List<Component>();

        /// <summary>Index du personnage joue ici, -1 s'il n'y en a pas.</summary>
        public int MyIndex { get; private set; } = -1;

        private static NetworkManager Net { get { return NetworkManager.Singleton; } }

        public bool Online { get { return Net != null && Net.IsListening; } }
        public bool IsHost { get { return Online && Net.IsServer; } }
        public ulong LocalId { get { return Net != null ? Net.LocalClientId : 0; } }

        /// <summary>Clients connectes (hote seulement).</summary>
        public IReadOnlyList<ulong> ConnectedIds { get { return Net.ConnectedClientsIds; } }

        /// <summary>Menu, lobby, chargement ou fin de partie : pas de controle du personnage.</summary>
        public bool InMenu
        {
            get { return Phase != NetPhase.Playing && Phase != NetPhase.Solo; }
        }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Scene rechargee : le gestionnaire persistant est deja la.
                enabled = false;
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (transform.parent != null) transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);

            Register(StateMessage, OnStateMessage);
            Register(EventMessage, OnEventMessage);
            Register(StartMessage, OnStartMessage);
            Register(LoadMessage, OnLoadMessage);
            Register(LoadedMessage, OnLoadedMessage);

            Lobby = GetOrAdd<LobbyManager>();
            GameState = GetOrAdd<GameStateManager>();
            TimeOfDay = GetOrAdd<NetworkTimeOfDay>();
            GetOrAdd<PlayerListUI>();
            GetOrAdd<CharacterSelectionUI>();

            SceneManager.sceneLoaded += OnSceneLoaded;
            FindSceneReferences();
        }

        private T GetOrAdd<T>() where T : Component
        {
            T c = GetComponent<T>();
            return c != null ? c : gameObject.AddComponent<T>();
        }

        private void Start()
        {
            // Apres les Start des autres objets de la scene (PNJ changes en survivants).
            _readers = FindObjectsByType<InputReader>();
            Phase = showMenuAtStart ? NetPhase.Menu : NetPhase.Solo;
        }

        private void OnEnable()
        {
            NetBridge.DemonKilled += OnLocalKill;
            NetBridge.DemonScreamed += OnLocalScream;
            NetBridge.DemonTeleported += OnLocalTeleport;
            NetBridge.DemonDisguised += OnLocalDisguise;

            _menuAction = new InputAction("NetMenu", InputActionType.Button, menuBinding);
            _menuAction.performed += _ => _menuPressed = true;
            _menuAction.Enable();
        }

        private void OnDisable()
        {
            NetBridge.DemonKilled -= OnLocalKill;
            NetBridge.DemonScreamed -= OnLocalScream;
            NetBridge.DemonTeleported -= OnLocalTeleport;
            NetBridge.DemonDisguised -= OnLocalDisguise;

            if (_menuAction != null)
            {
                _menuAction.Disable();
                _menuAction.Dispose();
                _menuAction = null;
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            NetBridge.Online = false;
            ShutdownNetwork();
            Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            FindSceneReferences();
            MyIndex = -1;
            Characters = new List<Component>();

            if (Phase == NetPhase.Loading) StartCoroutine(ReportLoaded());
        }

        private void FindSceneReferences()
        {
            Switcher = FindAnyObjectByType<PlayableCharacterSwitcher>();
            Spawner = FindAnyObjectByType<RandomSpawner>();
            _readers = FindObjectsByType<InputReader>();
        }

        private void Update()
        {
            bool menuPressed = _menuPressed;
            _menuPressed = false;

            if (InMenu)
            {
                // Menu, lobby, chargement ou fin de partie : pas de deplacement, souris libre.
                SetAllInput(false);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (menuPressed)
            {
                if (Phase == NetPhase.Solo) Phase = NetPhase.Menu;
                else if (Phase == NetPhase.Playing || Phase == NetPhase.GameOver) Lobby.Leave("Vous avez quitte la partie.");
            }

            if (!Online) return;

            if (Phase == NetPhase.Loading && IsHost && !_roundBegun && _loaded.Count > 0 && Time.unscaledTime >= _loadDeadline)
            {
                BeginRound(); // les retardataires sont ignores
            }

            if (Phase == NetPhase.Playing && MyIndex >= 0 && Time.unscaledTime >= _nextState)
            {
                _nextState = Time.unscaledTime + 1f / statesPerSecond;
                SendMyState();
            }
        }

        // ------------------------------------------------------------------
        // Reseau : NetworkManager et messagerie
        // ------------------------------------------------------------------

        /// <summary>Enregistre un message (avant ou apres le demarrage du reseau).</summary>
        public void Register(string message, CustomMessagingManager.HandleNamedMessageDelegate handler)
        {
            _handlers[message] = handler;

            if (_handlersRegistered && Net != null && Net.CustomMessagingManager != null)
            {
                Net.CustomMessagingManager.RegisterNamedMessageHandler(message, handler);
            }
        }

        /// <summary>NetworkManager cree par code (aucun objet a preparer dans la scene).</summary>
        public void PrepareNetwork()
        {
            if (Net == null)
            {
                GameObject go = new GameObject("NetworkManager");
                UnityTransport transport = go.AddComponent<UnityTransport>();
                NetworkManager manager = go.AddComponent<NetworkManager>();

                if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
                manager.NetworkConfig.NetworkTransport = transport;
                // Pas de NetworkObject : la scene est chargee par nos messages (chargement synchronise).
                manager.NetworkConfig.EnableSceneManagement = false;
                manager.NetworkConfig.ConnectionApproval = false;
                manager.NetworkConfig.PlayerPrefab = null;
            }

            Net.OnServerStarted -= OnServerStarted;
            Net.OnServerStarted += OnServerStarted;
            Net.OnClientStarted -= RegisterHandlers;
            Net.OnClientStarted += RegisterHandlers;
            Net.OnClientConnectedCallback -= OnClientConnected;
            Net.OnClientConnectedCallback += OnClientConnected;
            Net.OnClientDisconnectCallback -= OnClientDisconnected;
            Net.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void RegisterHandlers()
        {
            if (_handlersRegistered || Net == null || Net.CustomMessagingManager == null) return;

            _handlersRegistered = true;
            foreach (KeyValuePair<string, CustomMessagingManager.HandleNamedMessageDelegate> h in _handlers)
            {
                Net.CustomMessagingManager.RegisterNamedMessageHandler(h.Key, h.Value);
            }
        }

        private void OnServerStarted()
        {
            RegisterHandlers();
            Lobby.OnLocalConnected(); // l'hote s'inscrit dans son propre lobby
        }

        private void OnClientConnected(ulong clientId)
        {
            if (Net == null) return;

            if (Net.IsServer)
            {
                Lobby.OnClientJoined(clientId);
            }
            else if (clientId == Net.LocalClientId)
            {
                RegisterHandlers();
                Lobby.OnLocalConnected();
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (Net == null) return;

            if (Net.IsServer)
            {
                Lobby.OnClientLeft(clientId);
                GameState.OnClientLeft(clientId);
                _loaded.Remove(clientId);
                return;
            }

            if (clientId == Net.LocalClientId || clientId == NetworkManager.ServerClientId)
            {
                Lobby.Leave("Connexion a l'hote perdue.");
            }
        }

        /// <summary>Arrete le reseau (appele par LobbyManager en quittant la session).</summary>
        public void ShutdownNetwork()
        {
            NetworkManager manager = Net;
            if (manager == null) return;

            if (_handlersRegistered && manager.CustomMessagingManager != null)
            {
                foreach (string name in _handlers.Keys) manager.CustomMessagingManager.UnregisterNamedMessageHandler(name);
            }

            _handlersRegistered = false;
            manager.OnServerStarted -= OnServerStarted;
            manager.OnClientStarted -= RegisterHandlers;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;

            if (manager.IsListening) manager.Shutdown();
            Destroy(manager.gameObject);
        }

        public void SendToServer(string message, FastBufferWriter w, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
        {
            if (!Online) return;
            Net.CustomMessagingManager.SendNamedMessage(message, NetworkManager.ServerClientId, w, delivery);
        }

        public void SendTo(ulong clientId, string message, FastBufferWriter w, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
        {
            if (!Online) return;
            Net.CustomMessagingManager.SendNamedMessage(message, clientId, w, delivery);
        }

        /// <summary>Hote : a tous les clients, lui compris.</summary>
        public void SendToAll(string message, FastBufferWriter w, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
        {
            if (!IsHost) return;
            Net.CustomMessagingManager.SendNamedMessageToAll(message, w, delivery);
        }

        /// <summary>Hote : a tous les clients sauf lui-meme et 'except'.</summary>
        public void SendToOthers(ulong except, string message, FastBufferWriter w, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
        {
            if (!IsHost) return;

            List<ulong> targets = new List<ulong>();
            foreach (ulong id in Net.ConnectedClientsIds)
            {
                if (id != Net.LocalClientId && id != except) targets.Add(id);
            }

            if (targets.Count > 0) Net.CustomMessagingManager.SendNamedMessage(message, targets, w, delivery);
        }

        // ------------------------------------------------------------------
        // Etapes : menu, lobby, chargement, partie, fin
        // ------------------------------------------------------------------

        public void SetPhase(NetPhase phase)
        {
            Phase = phase;
        }

        public void PlaySolo()
        {
            Status = "";

            if (_sceneDirty)
            {
                // La scene a servi a une partie en ligne : on repart d'une scene neuve.
                _sceneDirty = false;
                Phase = NetPhase.Solo;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            Phase = NetPhase.Solo;
            SetAllInput(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Hote : tout le monde recharge la scene de jeu, puis la partie commence ensemble.</summary>
        public void LaunchRound()
        {
            if (!IsHost || Phase != NetPhase.Lobby) return;

            _loaded.Clear();
            _roundBegun = false;
            _loadDeadline = Time.unscaledTime + loadTimeout;

            using (FastBufferWriter w = new FastBufferWriter(256, Allocator.Temp))
            {
                w.WriteValueSafe(SceneManager.GetActiveScene().name);
                SendToAll(LoadMessage, w);
            }
        }

        private void OnLoadMessage(ulong sender, FastBufferReader r)
        {
            string scene;
            r.ReadValueSafe(out scene);

            Phase = NetPhase.Loading;
            Status = "Chargement de la partie...";
            NetBridge.Online = false;
            GameState.ResetLocal();
            TimeOfDay.Stop();
            SceneManager.LoadScene(scene);
        }

        private IEnumerator ReportLoaded()
        {
            // Apres les Start de la nouvelle scene (PNJ changes en survivants, etc.).
            yield return null;
            yield return null;

            using (FastBufferWriter w = new FastBufferWriter(8, Allocator.Temp))
            {
                w.WriteValueSafe((byte)1);
                SendToServer(LoadedMessage, w);
            }
        }

        private void OnLoadedMessage(ulong sender, FastBufferReader r)
        {
            if (!IsHost || Phase != NetPhase.Loading || _roundBegun) return;

            _loaded.Add(sender);

            foreach (ulong id in Net.ConnectedClientsIds)
            {
                if (!_loaded.Contains(id)) return;
            }

            BeginRound();
        }

        /// <summary>Hote : roles, apparitions, heure, puis depart simultane.</summary>
        private void BeginRound()
        {
            _roundBegun = true;
            Characters = Switcher != null ? Switcher.Characters() : new List<Component>();
            if (Characters.Count == 0) return;

            List<int> survivors = new List<int>();
            int demon = -1;
            for (int i = 0; i < Characters.Count; i++)
            {
                if (Characters[i] is DemonController) demon = i;
                else survivors.Add(i);
            }

            List<LobbyPlayer> players = Lobby.PlayersIn(_loaded);
            Dictionary<ulong, int> assignments = RoleManager.Assign(players, survivors, demon, Lobby.DemonTurns, new System.Random());

            if (Spawner != null) Spawner.SpawnAll();

            GameState.ServerStartRound(assignments, players, Characters);
            TimeOfDay.ServerStartRound();

            foreach (KeyValuePair<ulong, int> a in assignments) SendStart(a.Key, a.Value);
        }

        /// <summary>Le personnage attribue au joueur, et la position de chacun.</summary>
        private void SendStart(ulong clientId, int characterIndex)
        {
            using (FastBufferWriter w = new FastBufferWriter(64 + Characters.Count * 20, Allocator.Temp))
            {
                w.WriteValueSafe((byte)characterIndex);
                w.WriteValueSafe((byte)Characters.Count);

                foreach (Component c in Characters)
                {
                    w.WriteValueSafe(c.transform.position);
                    w.WriteValueSafe(c.transform.eulerAngles.y);
                }

                SendTo(clientId, StartMessage, w);
            }
        }

        private void OnStartMessage(ulong sender, FastBufferReader r)
        {
            Characters = Switcher != null ? Switcher.Characters() : new List<Component>();

            byte mine;
            byte count;
            r.ReadValueSafe(out mine);
            r.ReadValueSafe(out count);

            for (int i = 0; i < count; i++)
            {
                Vector3 position;
                float yaw;
                r.ReadValueSafe(out position);
                r.ReadValueSafe(out yaw);

                if (i < Characters.Count) Place(Characters[i], position, yaw);
            }

            if (mine >= Characters.Count) return;

            MyIndex = mine;
            NetBridge.Online = true;
            _sceneDirty = true;

            // Les autres personnages suivent le reseau ; le notre est joue ici.
            for (int i = 0; i < Characters.Count; i++)
            {
                Component c = Characters[i];
                NetPuppet puppet = c.GetComponent<NetPuppet>();

                if (i == MyIndex)
                {
                    if (puppet != null) Destroy(puppet);
                }
                else if (puppet == null)
                {
                    c.gameObject.AddComponent<NetPuppet>();
                }
            }

            if (Switcher != null) Switcher.ControlOnly(Characters[MyIndex]);

            GameState.SetLocalRole(Characters[MyIndex] is DemonController ? PlayerRole.Demon : PlayerRole.Survivor);
            TimeOfDay.Begin();

            GameManager game = GameManager.HasInstance ? GameManager.Instance : null;
            if (game != null && !game.IsPlaying) game.StartSession(GameMode.Cooperative, IsHost ? NetworkRole.Host : NetworkRole.Client);

            Phase = NetPhase.Playing;
            Status = "";
            SetAllInput(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void Place(Component c, Vector3 position, float yaw)
        {
            PlayerCharacter survivor = c as PlayerCharacter;
            DemonController demon = c as DemonController;

            if (survivor != null) survivor.TeleportTo(position, yaw);
            else if (demon != null) demon.TeleportTo(position, Quaternion.Euler(0f, yaw, 0f));

            NetPuppet puppet = c.GetComponent<NetPuppet>();
            if (puppet != null) puppet.Snap(position, yaw);
        }

        /// <summary>Fin de partie (GameStateManager) : ecran de resultat, puis retour au lobby.</summary>
        public void EnterGameOver()
        {
            Phase = NetPhase.GameOver;
        }

        public void ReturnToLobby()
        {
            if (!Online)
            {
                Phase = NetPhase.Menu;
                return;
            }

            ClearOnlineState();
            Phase = NetPhase.Lobby;
        }

        /// <summary>Les personnages redeviennent locaux (solo) ; la scene sera rechargee a la prochaine partie.</summary>
        public void ClearOnlineState()
        {
            NetBridge.Online = false;
            MyIndex = -1;
            TimeOfDay.Stop();

            foreach (Component c in Characters)
            {
                if (c == null) continue;
                NetPuppet puppet = c.GetComponent<NetPuppet>();
                if (puppet != null) Destroy(puppet);
            }

            if (Switcher != null) Switcher.Unlock();
        }

        /// <summary>Appele par LobbyManager apres avoir quitte la session.</summary>
        public void OnLeftOnline(string reason)
        {
            ClearOnlineState();
            GameState.ResetLocal();
            Status = reason;
            Phase = NetPhase.Menu;
        }

        // ------------------------------------------------------------------
        // Etat des personnages (~20 fois par seconde)
        // ------------------------------------------------------------------

        private void SendMyState()
        {
            if (MyIndex < 0 || MyIndex >= Characters.Count) return;

            Component c = Characters[MyIndex];
            if (c == null) return;

            float pitch = 0f;
            byte flags = 0;

            PlayerLook look = c.GetComponent<PlayerLook>();
            if (look != null) pitch = look.Pitch;

            Flashlight flashlight = c.GetComponent<Flashlight>();
            if (flashlight != null && flashlight.IsOn) flags |= 1;

            DemonCry cry = c.GetComponent<DemonCry>();
            if (cry != null && cry.IsCrying) flags |= 2;

            using (FastBufferWriter w = new FastBufferWriter(32, Allocator.Temp))
            {
                WriteState(w, (byte)MyIndex, c.transform.position, c.transform.eulerAngles.y, pitch, flags);
                if (IsHost) SendToOthers(LocalId, StateMessage, w, NetworkDelivery.UnreliableSequenced);
                else SendToServer(StateMessage, w, NetworkDelivery.UnreliableSequenced);
            }
        }

        private static void WriteState(FastBufferWriter w, byte index, Vector3 position, float yaw, float pitch, byte flags)
        {
            w.WriteValueSafe(index);
            w.WriteValueSafe(position);
            w.WriteValueSafe(yaw);
            w.WriteValueSafe(pitch);
            w.WriteValueSafe(flags);
        }

        private void OnStateMessage(ulong sender, FastBufferReader r)
        {
            byte index;
            Vector3 position;
            float yaw;
            float pitch;
            byte flags;
            r.ReadValueSafe(out index);
            r.ReadValueSafe(out position);
            r.ReadValueSafe(out yaw);
            r.ReadValueSafe(out pitch);
            r.ReadValueSafe(out flags);

            if (IsHost)
            {
                // Autorite : on n'accepte que l'etat du personnage attribue a cet emetteur.
                if (GameState.OwnerOf(index) != sender) return;

                ApplyState(index, position, yaw, pitch, flags);

                using (FastBufferWriter w = new FastBufferWriter(32, Allocator.Temp))
                {
                    WriteState(w, index, position, yaw, pitch, flags);
                    SendToOthers(sender, StateMessage, w, NetworkDelivery.UnreliableSequenced);
                }

                return;
            }

            ApplyState(index, position, yaw, pitch, flags);
        }

        private void ApplyState(int index, Vector3 position, float yaw, float pitch, byte flags)
        {
            if (Phase != NetPhase.Playing && Phase != NetPhase.GameOver) return;
            if (index == MyIndex || index < 0 || index >= Characters.Count) return;

            Component c = Characters[index];
            if (c == null) return;

            NetPuppet puppet = c.GetComponent<NetPuppet>();
            if (puppet == null) puppet = c.gameObject.AddComponent<NetPuppet>();
            puppet.Receive(position, yaw, pitch, flags);
        }

        // ------------------------------------------------------------------
        // Actions du demon (NetBridge -> reseau)
        // ------------------------------------------------------------------

        public int IndexOf(Component c)
        {
            if (c == null) return -1;

            PlayerCharacter survivor = c.GetComponent<PlayerCharacter>();
            DemonController demon = c.GetComponent<DemonController>();
            Component key = survivor != null ? (Component)survivor : demon;
            return key != null ? Characters.IndexOf(key) : -1;
        }

        public T Get<T>(int index) where T : Component
        {
            if (index < 0 || index >= Characters.Count || Characters[index] == null) return null;
            return Characters[index].GetComponent<T>();
        }

        /// <summary>Nom affiche d'un personnage : le joueur qui le joue (si connu), sinon le personnage.</summary>
        public string NameOf(int index)
        {
            string player = GameState.PlayerNameOf(index);
            if (!string.IsNullOrEmpty(player)) return player;

            if (index < 0 || index >= Characters.Count) return "?";
            PlayerCharacter survivor = Characters[index] as PlayerCharacter;
            return survivor != null ? survivor.DisplayName : "Le Demon";
        }

        private void OnLocalKill(DemonKill demon, SurvivorDeath victim, bool jumpscare)
        {
            int d = IndexOf(demon);
            int v = IndexOf(victim);
            if (d < 0 || v < 0) return;

            // Le demon demande ; seul l'hote valide la mort (GameStateManager).
            GameState.RequestKill(d, v, jumpscare);
        }

        private void OnLocalScream(DemonPowers demon)
        {
            int d = IndexOf(demon);
            if (d < 0) return;
            SendEvent(EventScream, w => w.WriteValueSafe((byte)d));
        }

        private void OnLocalTeleport(DemonPowers demon, Vector3 from, Vector3 to)
        {
            int d = IndexOf(demon);
            if (d < 0) return;

            SendEvent(EventTeleport, w =>
            {
                w.WriteValueSafe((byte)d);
                w.WriteValueSafe(from);
                w.WriteValueSafe(to);
                w.WriteValueSafe(demon.transform.eulerAngles.y);
            });
        }

        private void OnLocalDisguise(DemonPowers demon, PlayerCharacter look, float duration)
        {
            int d = IndexOf(demon);
            int l = IndexOf(look);
            if (d < 0 || l < 0) return;

            SendEvent(EventDisguise, w =>
            {
                w.WriteValueSafe((byte)d);
                w.WriteValueSafe((byte)l);
                w.WriteValueSafe(duration);
            });
        }

        /// <summary>Action faite ici : client -> hote (qui relaie), hote -> tous les autres.</summary>
        private void SendEvent(byte type, Action<FastBufferWriter> write)
        {
            if (!Online) return;

            using (FastBufferWriter w = new FastBufferWriter(128, Allocator.Temp))
            {
                w.WriteValueSafe(type);
                write(w);

                if (IsHost) SendToOthers(LocalId, EventMessage, w);
                else SendToServer(EventMessage, w);
            }
        }

        /// <summary>Hote : envoie un evenement deja valide a tous les clients sauf 'except'.</summary>
        public void BroadcastEvent(ulong except, byte type, Action<FastBufferWriter> write)
        {
            using (FastBufferWriter w = new FastBufferWriter(128, Allocator.Temp))
            {
                w.WriteValueSafe(type);
                write(w);
                SendToOthers(except, EventMessage, w);
            }
        }

        public void SendEventTo(ulong clientId, byte type, Action<FastBufferWriter> write)
        {
            using (FastBufferWriter w = new FastBufferWriter(128, Allocator.Temp))
            {
                w.WriteValueSafe(type);
                write(w);
                SendTo(clientId, EventMessage, w);
            }
        }

        private void OnEventMessage(ulong sender, FastBufferReader r)
        {
            byte type;
            r.ReadValueSafe(out type);

            using (FastBufferWriter copy = new FastBufferWriter(128, Allocator.Temp))
            {
                copy.WriteValueSafe(type);

                switch (type)
                {
                    case EventKill:
                    {
                        // Toujours emis par l'hote (mort validee par GameStateManager).
                        byte d, v;
                        bool jumpscare;
                        r.ReadValueSafe(out d);
                        r.ReadValueSafe(out v);
                        r.ReadValueSafe(out jumpscare);
                        if (IsHost && sender != LocalId) return; // un client ne decide jamais d'une mort
                        ApplyKill(d, v, jumpscare);
                        return;
                    }
                    case EventRevive:
                    {
                        // Mort refusee par l'hote : on releve la victime tuee trop vite chez nous.
                        byte v;
                        r.ReadValueSafe(out v);
                        if (IsHost && sender != LocalId) return;
                        SurvivorDeath victim = Get<SurvivorDeath>(v);
                        if (victim != null) victim.Revive();
                        return;
                    }
                    case EventScream:
                    {
                        byte d;
                        r.ReadValueSafe(out d);
                        copy.WriteValueSafe(d);
                        if (IsHost && GameState.OwnerOf(d) != sender) return;
                        DemonPowers powers = Get<DemonPowers>(d);
                        if (powers != null) powers.PlayScreamSound();
                        break;
                    }
                    case EventTeleport:
                    {
                        byte d;
                        Vector3 from, to;
                        float yaw;
                        r.ReadValueSafe(out d);
                        r.ReadValueSafe(out from);
                        r.ReadValueSafe(out to);
                        r.ReadValueSafe(out yaw);
                        copy.WriteValueSafe(d);
                        copy.WriteValueSafe(from);
                        copy.WriteValueSafe(to);
                        copy.WriteValueSafe(yaw);
                        if (IsHost && GameState.OwnerOf(d) != sender) return;
                        DemonPowers powers = Get<DemonPowers>(d);
                        if (powers != null) powers.PlayTeleportSounds(from, to);
                        NetPuppet puppet = Get<NetPuppet>(d);
                        if (puppet != null) puppet.Snap(to, yaw);
                        break;
                    }
                    case EventDisguise:
                    {
                        byte d, l;
                        float duration;
                        r.ReadValueSafe(out d);
                        r.ReadValueSafe(out l);
                        r.ReadValueSafe(out duration);
                        copy.WriteValueSafe(d);
                        copy.WriteValueSafe(l);
                        copy.WriteValueSafe(duration);
                        if (IsHost && GameState.OwnerOf(d) != sender) return;
                        DemonPowers powers = Get<DemonPowers>(d);
                        PlayerCharacter look = Get<PlayerCharacter>(l);
                        if (powers != null && look != null) powers.ApplyDisguise(look, duration);
                        break;
                    }
                    default:
                        return;
                }

                // L'hote relaie l'action (validee) aux autres joueurs.
                if (IsHost) SendToOthers(sender, EventMessage, copy);
            }
        }

        /// <summary>Joue une mort validee par l'hote (animation, ragdoll, jumpscare chez la victime).</summary>
        public void ApplyKill(int demonIndex, int victimIndex, bool jumpscare)
        {
            DemonKill kill = Get<DemonKill>(demonIndex);
            SurvivorDeath victim = Get<SurvivorDeath>(victimIndex);
            if (kill == null || victim == null || victim.IsDead) return;

            kill.PlayRemoteKill(victim, jumpscare, victimIndex == MyIndex);
        }

        // ------------------------------------------------------------------
        // Divers
        // ------------------------------------------------------------------

        /// <summary>Entrees de gameplay de tous les personnages (menu ouvert = aucune).</summary>
        private void SetAllInput(bool enabled)
        {
            foreach (InputReader reader in _readers)
            {
                if (reader != null && reader.InputEnabled != enabled) reader.SetInputEnabled(enabled);
            }
        }

        private void OnGUI()
        {
            if (Phase != NetPhase.Playing) return;

            if (_smallStyle == null)
            {
                _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperRight };
                _smallStyle.normal.textColor = new Color(0.75f, 0.72f, 0.68f);
            }

            string code = string.IsNullOrEmpty(Lobby.JoinCode) ? "" : " · code " + Lobby.JoinCode;
            GUI.Label(new Rect(Screen.width - 520f, 8f, 510f, 22f), "En ligne" + code + " · TAB joueurs · F12 quitter", _smallStyle);
        }
    }
}
