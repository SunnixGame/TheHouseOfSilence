using System;
using System.Collections.Generic;
using System.Text;
using HouseOfSilence.Demon;
using HouseOfSilence.Level;
using HouseOfSilence.Player;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Network
{
    /// <summary>
    /// Multijoueur en ligne : Unity Relay (Multiplayer Services, sessions) + Netcode for
    /// GameObjects, en hote / clients (5 joueurs : 4 survivants et le demon).
    ///
    /// Menu au lancement de la Foret : "Creer / rejoindre" une partie par son nom (le
    /// premier arrive l'heberge), ou "Jouer en solo" (comme avant). L'hote lance la partie :
    /// un joueur tire au hasard devient le demon, les autres des survivants, et chacun
    /// apparait dans une clairiere (positions tirees par l'hote).
    ///
    /// Synchronisation par messages nommes (pas de NetworkObject a preparer) : chaque joueur
    /// envoie ~20 fois par seconde l'etat de son personnage (position, orientation, regard,
    /// lampe, pleurs) ; l'hote le relaie aux autres. Les actions du demon (mise a mort et
    /// jumpscare, cri, teleportation, deguisement) passent par NetBridge et sont rejouees
    /// chez tout le monde.
    ///
    /// Avant la premiere partie : lier le projet a Unity Cloud (Edit > Project Settings >
    /// Services) et activer Relay dans le tableau de bord Unity Cloud.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetGame : MonoBehaviour
    {
        private const string StateMessage = "HOS_State";
        private const string EventMessage = "HOS_Event";
        private const string StartMessage = "HOS_Start";
        private const string LobbyMessage = "HOS_Lobby";

        private const byte EventKill = 1;
        private const byte EventScream = 2;
        private const byte EventTeleport = 3;
        private const byte EventDisguise = 4;

        private enum Phase { Menu, Connecting, Lobby, Playing, Solo }

        [Header("Session")]
        [SerializeField] private string defaultSessionName = "partie";
        [SerializeField, Range(2, 5)] private int maxPlayers = 5;
        [SerializeField] private bool showMenuAtStart = true;
        [Tooltip("Touche : quitter la partie en ligne / rouvrir le menu multijoueur en solo.")]
        [SerializeField] private string menuBinding = "<Keyboard>/f12";

        [Header("Synchronisation")]
        [SerializeField, Range(5f, 60f)] private float statesPerSecond = 20f;

        [Header("References (trouvees si vides)")]
        [SerializeField] private PlayableCharacterSwitcher switcher;
        [SerializeField] private RandomSpawner spawner;

        private Phase _phase = Phase.Solo;
        private string _sessionName;
        private string _status = "";
        private ISession _session;
        private bool _handlersRegistered;
        private InputReader[] _readers = new InputReader[0];
        private InputAction _menuAction;
        private bool _menuPressed;

        private List<Component> _characters = new List<Component>();
        private int _myIndex = -1;
        private readonly Dictionary<ulong, int> _assignments = new Dictionary<ulong, int>();
        private float _nextState;
        private float _nextLobby;
        private int _lobbyCount;

        private GUIStyle _titleStyle;
        private GUIStyle _textStyle;
        private GUIStyle _smallStyle;

        private static NetworkManager Net { get { return NetworkManager.Singleton; } }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (switcher == null) switcher = FindAnyObjectByType<PlayableCharacterSwitcher>();
            if (spawner == null) spawner = FindAnyObjectByType<RandomSpawner>();
            _sessionName = defaultSessionName;
        }

        private void Start()
        {
            // Tous les survivants (PNJ compris, crees avant) : le demon suit le lecteur du survivant d'origine.
            _readers = FindObjectsByType<InputReader>();

            if (showMenuAtStart) _phase = Phase.Menu;
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
            NetBridge.Online = false;
            ShutdownNetwork();
        }

        private void Update()
        {
            bool menuPressed = _menuPressed;
            _menuPressed = false;

            bool inMenu = _phase == Phase.Menu || _phase == Phase.Connecting || _phase == Phase.Lobby;

            if (inMenu)
            {
                // Menu ouvert : pas de deplacement, souris libre.
                SetAllInput(false);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (menuPressed)
            {
                if (_phase == Phase.Solo) _phase = Phase.Menu;
                else if (_phase == Phase.Playing) Leave("Vous avez quitte la partie.");
            }

            if (Net == null || !Net.IsListening) return;

            if (_phase == Phase.Lobby && Net.IsServer && Time.unscaledTime >= _nextLobby)
            {
                _nextLobby = Time.unscaledTime + 1f;
                SendLobbyInfo();
            }

            if (_phase == Phase.Playing && _myIndex >= 0 && Time.unscaledTime >= _nextState)
            {
                _nextState = Time.unscaledTime + 1f / statesPerSecond;
                SendMyState();
            }
        }

        // ------------------------------------------------------------------
        // Connexion (Unity Services + Relay)
        // ------------------------------------------------------------------

        private async void Connect()
        {
            _phase = Phase.Connecting;
            _status = "Connexion aux services Unity...";

            try
            {
                EnsureNetworkManager();

                if (UnityServices.Instance == null || UnityServices.Instance.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    // Profil propre a cette fenetre : deux instances sur le meme PC = deux joueurs.
                    AuthenticationService.Instance.SwitchProfile("hos" + UnityEngine.Random.Range(0, 1000000));
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                string id = SessionId(_sessionName);
                _status = "Recherche de la partie \"" + id + "\"...";

                SessionOptions options = new SessionOptions
                {
                    Name = id,
                    MaxPlayers = maxPlayers
                }.WithRelayNetwork();

                ISession session = await MultiplayerService.Instance.CreateOrJoinSessionAsync(id, options);

                if (_phase != Phase.Connecting)
                {
                    // Annule pendant la connexion : on ressort aussitot.
                    await session.LeaveAsync();
                    ShutdownNetwork();
                    return;
                }

                _session = session;

                _phase = Phase.Lobby;
                _status = "";
                _lobbyCount = 1;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _status = "Echec de la connexion : " + e.Message;
                _session = null;
                ShutdownNetwork();
                _phase = Phase.Menu;
            }
        }

        /// <summary>Nom de partie -> identifiant de session (lettres, chiffres, - et _).</summary>
        private static string SessionId(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ') sb.Append('-');
                if (sb.Length >= 30) break;
            }

            return sb.Length > 0 ? "hos-" + sb : "hos-partie";
        }

        /// <summary>NetworkManager cree par code (aucun objet a preparer dans la scene).</summary>
        private void EnsureNetworkManager()
        {
            if (Net == null)
            {
                GameObject go = new GameObject("NetworkManager");
                UnityTransport transport = go.AddComponent<UnityTransport>();
                NetworkManager manager = go.AddComponent<NetworkManager>();

                if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
                manager.NetworkConfig.NetworkTransport = transport;
                manager.NetworkConfig.EnableSceneManagement = false; // meme scene chez tous, rien a charger
                manager.NetworkConfig.ConnectionApproval = false;
                manager.NetworkConfig.PlayerPrefab = null;
            }

            Net.OnServerStarted -= RegisterHandlers;
            Net.OnClientStarted -= RegisterHandlers;
            Net.OnServerStarted += RegisterHandlers;
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
            CustomMessagingManager messages = Net.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(StateMessage, OnStateMessage);
            messages.RegisterNamedMessageHandler(EventMessage, OnEventMessage);
            messages.RegisterNamedMessageHandler(StartMessage, OnStartMessage);
            messages.RegisterNamedMessageHandler(LobbyMessage, OnLobbyMessage);
        }

        private async void Leave(string reason)
        {
            ISession session = _session;
            _session = null;
            ResetToMenu(reason);

            try
            {
                if (session != null) await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NetGame] " + e.Message);
            }

            ShutdownNetwork();
        }

        private void ShutdownNetwork()
        {
            NetworkManager manager = Net;
            if (manager == null) return;

            if (_handlersRegistered && manager.CustomMessagingManager != null)
            {
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(EventMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(StartMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyMessage);
            }

            _handlersRegistered = false;
            manager.OnServerStarted -= RegisterHandlers;
            manager.OnClientStarted -= RegisterHandlers;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;

            if (manager.IsListening) manager.Shutdown();
            Destroy(manager.gameObject);
        }

        /// <summary>Retour au menu : les personnages redeviennent locaux (solo), rien n'est recharge.</summary>
        private void ResetToMenu(string reason)
        {
            NetBridge.Online = false;
            _myIndex = -1;
            _assignments.Clear();

            foreach (Component c in _characters)
            {
                if (c == null) continue;
                NetPuppet puppet = c.GetComponent<NetPuppet>();
                if (puppet != null) Destroy(puppet);
            }

            if (switcher != null) switcher.Unlock();
            _status = reason;
            _phase = Phase.Menu;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (Net == null || !Net.IsServer) return;

            // Arrive en cours de partie : un personnage libre, s'il en reste.
            if (_phase == Phase.Playing && clientId != Net.LocalClientId && !_assignments.ContainsKey(clientId))
            {
                int free = FreeCharacter();
                if (free >= 0)
                {
                    _assignments[clientId] = free;
                    SendStart(clientId, free);
                }
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (Net == null) return;

            if (Net.IsServer)
            {
                // Son personnage reste fige la ou il etait.
                _assignments.Remove(clientId);
                return;
            }

            if (clientId == Net.LocalClientId || clientId == NetworkManager.ServerClientId)
            {
                Leave("Connexion a l'hote perdue.");
            }
        }

        // ------------------------------------------------------------------
        // Lancement de la partie (hote)
        // ------------------------------------------------------------------

        private void HostStartGame()
        {
            _characters = switcher != null ? switcher.Characters() : new List<Component>();
            if (_characters.Count == 0) return;

            List<ulong> clients = new List<ulong>(Net.ConnectedClientsIds);
            Shuffle(clients);

            List<int> free = new List<int>();
            for (int i = 0; i < _characters.Count; i++) free.Add(i);

            _assignments.Clear();
            int demonIndex = _characters.FindIndex(c => c is DemonController);
            int first = 0;

            // Au moins deux joueurs : l'un d'eux, tire au hasard, est le demon.
            if (clients.Count >= 2 && demonIndex >= 0)
            {
                _assignments[clients[0]] = demonIndex;
                free.Remove(demonIndex);
                first = 1;
            }

            Shuffle(free);
            for (int i = first; i < clients.Count && free.Count > 0; i++)
            {
                _assignments[clients[i]] = free[0];
                free.RemoveAt(0);
            }

            // Nouvelles clairieres pour tout le monde (tirees ici, puis envoyees).
            if (spawner != null) spawner.SpawnAll();

            foreach (KeyValuePair<ulong, int> a in _assignments) SendStart(a.Key, a.Value);
        }

        private int FreeCharacter()
        {
            for (int i = 0; i < _characters.Count; i++)
            {
                if (!_assignments.ContainsValue(i)) return i;
            }

            return -1;
        }

        /// <summary>Le personnage attribue au joueur, et la position de chacun.</summary>
        private void SendStart(ulong clientId, int characterIndex)
        {
            using (FastBufferWriter w = new FastBufferWriter(64 + _characters.Count * 20, Allocator.Temp))
            {
                w.WriteValueSafe((byte)characterIndex);
                w.WriteValueSafe((byte)_characters.Count);

                foreach (Component c in _characters)
                {
                    w.WriteValueSafe(c.transform.position);
                    w.WriteValueSafe(c.transform.eulerAngles.y);
                }

                Net.CustomMessagingManager.SendNamedMessage(StartMessage, clientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnStartMessage(ulong sender, FastBufferReader r)
        {
            _characters = switcher != null ? switcher.Characters() : new List<Component>();

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

                if (i < _characters.Count) Place(_characters[i], position, yaw);
            }

            if (mine >= _characters.Count) return;

            _myIndex = mine;
            NetBridge.Online = true;

            // Les autres personnages suivent le reseau ; le notre est joue ici.
            for (int i = 0; i < _characters.Count; i++)
            {
                Component c = _characters[i];
                NetPuppet puppet = c.GetComponent<NetPuppet>();

                if (i == _myIndex)
                {
                    if (puppet != null) Destroy(puppet);
                }
                else if (puppet == null)
                {
                    c.gameObject.AddComponent<NetPuppet>();
                }
            }

            if (switcher != null) switcher.ControlOnly(_characters[_myIndex]);

            SetAllInput(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _status = "";
            _phase = Phase.Playing;
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

        private void SendLobbyInfo()
        {
            using (FastBufferWriter w = new FastBufferWriter(8, Allocator.Temp))
            {
                w.WriteValueSafe((byte)Net.ConnectedClientsIds.Count);
                Net.CustomMessagingManager.SendNamedMessageToAll(LobbyMessage, w, NetworkDelivery.Reliable);
            }
        }

        private void OnLobbyMessage(ulong sender, FastBufferReader r)
        {
            byte count;
            r.ReadValueSafe(out count);
            _lobbyCount = count;
        }

        // ------------------------------------------------------------------
        // Etat des personnages (~20 fois par seconde)
        // ------------------------------------------------------------------

        private void SendMyState()
        {
            if (_myIndex < 0 || _myIndex >= _characters.Count) return;

            Component c = _characters[_myIndex];
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
                WriteState(w, (byte)_myIndex, c.transform.position, c.transform.eulerAngles.y, pitch, flags);
                SendToOthers(StateMessage, w, NetworkDelivery.UnreliableSequenced, NetworkManager.ServerClientId);
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

            ApplyState(index, position, yaw, pitch, flags);

            // L'hote relaie aux autres joueurs.
            if (Net.IsServer)
            {
                using (FastBufferWriter w = new FastBufferWriter(32, Allocator.Temp))
                {
                    WriteState(w, index, position, yaw, pitch, flags);
                    SendToOthers(StateMessage, w, NetworkDelivery.UnreliableSequenced, sender);
                }
            }
        }

        private void ApplyState(int index, Vector3 position, float yaw, float pitch, byte flags)
        {
            if (_phase != Phase.Playing || index == _myIndex || index < 0 || index >= _characters.Count) return;

            Component c = _characters[index];
            if (c == null) return;

            NetPuppet puppet = c.GetComponent<NetPuppet>();
            if (puppet == null) puppet = c.gameObject.AddComponent<NetPuppet>();
            puppet.Receive(position, yaw, pitch, flags);
        }

        /// <summary>
        /// Client : a l'hote seulement. Hote : a tous les clients sauf lui-meme et 'except'
        /// (l'auteur du message relaye).
        /// </summary>
        private void SendToOthers(string message, FastBufferWriter w, NetworkDelivery delivery, ulong except)
        {
            CustomMessagingManager messages = Net.CustomMessagingManager;

            if (!Net.IsServer)
            {
                messages.SendNamedMessage(message, NetworkManager.ServerClientId, w, delivery);
                return;
            }

            List<ulong> targets = new List<ulong>();
            foreach (ulong id in Net.ConnectedClientsIds)
            {
                if (id != Net.LocalClientId && id != except) targets.Add(id);
            }

            if (targets.Count > 0) messages.SendNamedMessage(message, targets, w, delivery);
        }

        // ------------------------------------------------------------------
        // Actions du demon (NetBridge -> reseau -> autres joueurs)
        // ------------------------------------------------------------------

        private int IndexOf(Component c)
        {
            if (c == null) return -1;

            PlayerCharacter survivor = c.GetComponent<PlayerCharacter>();
            DemonController demon = c.GetComponent<DemonController>();
            Component key = survivor != null ? (Component)survivor : demon;
            return key != null ? _characters.IndexOf(key) : -1;
        }

        private void OnLocalKill(DemonKill demon, SurvivorDeath victim, bool jumpscare)
        {
            int d = IndexOf(demon);
            int v = IndexOf(victim);
            if (d < 0 || v < 0) return;

            SendEvent(EventKill, w =>
            {
                w.WriteValueSafe((byte)d);
                w.WriteValueSafe((byte)v);
                w.WriteValueSafe(jumpscare);
            });
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

        private void SendEvent(byte type, Action<FastBufferWriter> write)
        {
            if (Net == null || !Net.IsListening || Net.CustomMessagingManager == null) return;

            using (FastBufferWriter w = new FastBufferWriter(128, Allocator.Temp))
            {
                w.WriteValueSafe(type);
                write(w);
                SendToOthers(EventMessage, w, NetworkDelivery.ReliableSequenced, NetworkManager.ServerClientId);
            }
        }

        private void OnEventMessage(ulong sender, FastBufferReader r)
        {
            byte type;
            r.ReadValueSafe(out type);

            // Lu une fois, applique ici, puis relaye tel quel par l'hote.
            using (FastBufferWriter copy = new FastBufferWriter(128, Allocator.Temp))
            {
                copy.WriteValueSafe(type);

                switch (type)
                {
                    case EventKill:
                    {
                        byte d, v;
                        bool jumpscare;
                        r.ReadValueSafe(out d);
                        r.ReadValueSafe(out v);
                        r.ReadValueSafe(out jumpscare);
                        copy.WriteValueSafe(d);
                        copy.WriteValueSafe(v);
                        copy.WriteValueSafe(jumpscare);
                        ApplyKill(d, v, jumpscare);
                        break;
                    }
                    case EventScream:
                    {
                        byte d;
                        r.ReadValueSafe(out d);
                        copy.WriteValueSafe(d);
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
                        DemonPowers powers = Get<DemonPowers>(d);
                        PlayerCharacter look = Get<PlayerCharacter>(l);
                        if (powers != null && look != null) powers.ApplyDisguise(look, duration);
                        break;
                    }
                    default:
                        return;
                }

                if (Net.IsServer) SendToOthers(EventMessage, copy, NetworkDelivery.ReliableSequenced, sender);
            }
        }

        private void ApplyKill(int demonIndex, int victimIndex, bool jumpscare)
        {
            DemonKill kill = Get<DemonKill>(demonIndex);
            SurvivorDeath victim = Get<SurvivorDeath>(victimIndex);
            if (kill == null || victim == null) return;

            kill.PlayRemoteKill(victim, jumpscare, victimIndex == _myIndex);
        }

        private T Get<T>(int index) where T : Component
        {
            if (index < 0 || index >= _characters.Count || _characters[index] == null) return null;
            return _characters[index].GetComponent<T>();
        }

        // ------------------------------------------------------------------
        // Menu
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            EnsureStyles();

            if (_phase == Phase.Solo) return;

            if (_phase == Phase.Playing)
            {
                string who = _myIndex >= 0 && _myIndex < _characters.Count ? Describe(_characters[_myIndex]) : "?";
                GUI.Label(new Rect(Screen.width - 420f, 10f, 410f, 22f), "En ligne (" + _sessionName + ") · " + who + " · F12 quitter", _smallStyle);
                return;
            }

            float w = 460f;
            float h = 290f;
            Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUILayout.BeginArea(new Rect(box.x + 20f, box.y + 16f, w - 40f, h - 32f));
            GUILayout.Label("THE HOUSE OF SILENCE", _titleStyle);
            GUILayout.Space(8f);

            switch (_phase)
            {
                case Phase.Menu:
                    GUILayout.Label("Nom de la partie (le meme pour tous les joueurs) :", _textStyle);
                    _sessionName = GUILayout.TextField(_sessionName ?? "", 30);
                    GUILayout.Space(8f);
                    if (GUILayout.Button("Creer / rejoindre la partie en ligne", GUILayout.Height(34f))) Connect();
                    if (GUILayout.Button("Jouer en solo", GUILayout.Height(28f))) PlaySolo();
                    break;

                case Phase.Connecting:
                    GUILayout.Label("Connexion...", _textStyle);
                    if (GUILayout.Button("Annuler", GUILayout.Height(28f))) Leave("");
                    break;

                case Phase.Lobby:
                    bool host = Net != null && Net.IsServer;
                    int count = host ? Net.ConnectedClientsIds.Count : _lobbyCount;
                    GUILayout.Label("Partie : " + SessionId(_sessionName), _textStyle);
                    GUILayout.Label("Joueurs : " + count + " / " + maxPlayers + (host ? "   (vous etes l'hote)" : ""), _textStyle);
                    GUILayout.Label("Au lancement : un joueur tire au hasard devient le demon, les autres des survivants.", _smallStyle);
                    GUILayout.Space(8f);
                    if (host)
                    {
                        if (GUILayout.Button("Lancer la partie", GUILayout.Height(34f))) HostStartGame();
                    }
                    else
                    {
                        GUILayout.Label("En attente du lancement par l'hote...", _textStyle);
                    }
                    if (GUILayout.Button("Quitter", GUILayout.Height(26f))) Leave("");
                    break;
            }

            if (!string.IsNullOrEmpty(_status))
            {
                GUILayout.Space(6f);
                GUILayout.Label(_status, _smallStyle);
            }

            GUILayout.EndArea();
        }

        private void PlaySolo()
        {
            _phase = Phase.Solo;
            _status = "";
            SetAllInput(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Entrees de gameplay de tous les personnages (menu ouvert = aucune).</summary>
        private void SetAllInput(bool enabled)
        {
            foreach (InputReader reader in _readers)
            {
                if (reader != null && reader.InputEnabled != enabled) reader.SetInputEnabled(enabled);
            }
        }

        private static string Describe(Component c)
        {
            PlayerCharacter survivor = c as PlayerCharacter;
            return survivor != null ? survivor.DisplayName : "Le Demon";
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;

            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _titleStyle.normal.textColor = new Color(0.85f, 0.2f, 0.15f);
            _textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _textStyle.normal.textColor = new Color(0.9f, 0.88f, 0.84f);
            _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _smallStyle.normal.textColor = new Color(0.75f, 0.72f, 0.68f);
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
