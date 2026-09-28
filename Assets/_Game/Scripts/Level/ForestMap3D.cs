using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace HouseOfSilence.Level
{
    /// <summary>
    /// Carte 3D de la foret : une maquette du terrain (relief + canopee) posee sous
    /// le niveau, filmee par sa propre camera. M ouvre / ferme la carte.
    ///
    /// Carte ouverte : souris = tourner, molette = zoom, ZQSD / fleches = deplacer,
    /// F = recentrer sur le joueur. Le joueur ne bouge plus et sa camera est coupee
    /// (la foret complete n'est pas rendue pendant ce temps).
    ///
    /// La maquette est generee par ForestLandscapeBuilder (Editor).
    /// </summary>
    [DisallowMultipleComponent]
    public class ForestMap3D : MonoBehaviour
    {
        [Header("References (generees par le builder)")]
        [SerializeField] private Camera mapCamera;
        [SerializeField] private Transform diorama;
        [SerializeField] private Transform playerMarker;

        [Header("Echelle")]
        [Tooltip("Metres monde -> unites de maquette.")]
        [SerializeField] private float mapScale = 0.1f;

        [Tooltip("Exageration verticale du relief de la maquette.")]
        [SerializeField] private float heightExaggeration = 2f;

        [Tooltip("Hauteur du marqueur joueur au-dessus du sol de la maquette.")]
        [SerializeField] private float markerHover = 4f;

        [Header("Noms des lieux")]
        [Tooltip("Assets/_Game/Settings/ForestLocations.asset : les noms s'y modifient.")]
        [SerializeField] private ForestLocations locations;

        [SerializeField] private Color labelColor = new Color(0.95f, 0.9f, 0.78f, 1f);

        [Header("Teleportation (outil de test)")]
        [Tooltip("Carte ouverte : viseur au centre, clic gauche ou T = s'y teleporter, Tab = clairiere suivante.")]
        [SerializeField] private bool enableTeleport = true;

        [Tooltip("Collider de la maquette : seul collider teste par le viseur.")]
        [SerializeField] private Collider dioramaCollider;

        [Tooltip("Balise montrant le point d'arrivee sur la maquette.")]
        [SerializeField] private Transform teleportCursor;

        [Header("Controles")]
        [Tooltip("Binding Input System. #(M) = la touche qui affiche M (AZERTY comme QWERTY).")]
        [SerializeField] private string toggleBinding = "<Keyboard>/#(M)";
        [SerializeField] private float orbitSensitivity = 0.15f;
        [SerializeField] private float zoomSensitivity = 0.08f;
        [SerializeField] private float panSpeed = 0.9f;
        [SerializeField] private Vector2 distanceRange = new Vector2(12f, 260f);
        [SerializeField] private Vector2 pitchRange = new Vector2(20f, 88f);

        [Header("Joueurs reveles (pouvoir Vision du demon)")]
        [SerializeField] private Color revealColor = new Color(0.78f, 0.25f, 1f, 1f); // violet : distinct de la balise orange du joueur
        [SerializeField] private string revealLabel = "JOUEUR";

        private PlayerCharacter _player;
        private InputReader _input;
        private Camera _playerCamera;

        // Personnage controle a la place du survivant (mode demon) : marqueur,
        // camera coupee pendant la carte et teleportation portent sur lui.
        private Transform _controlledOverride;
        private Camera _cameraOverride;
        private Camera _disabledCamera;

        private class Revealed
        {
            public Transform target;
            public Transform marker;
            public float until;
            public Color color;
            public string label;
        }

        private readonly List<Revealed> _revealed = new List<Revealed>();
        private GUIStyle _revealStyle;

        private bool _open;
        private bool _savedFog;
        private Vector3 _focus;
        private float _yaw;
        private float _pitch = 55f;
        private float _distance = 90f;

        private GUIStyle _hintStyle;
        private GUIStyle _crosshairStyle;
        private GUIStyle _labelStyle;

        private bool _hasTarget;
        private Vector3 _targetOnMap;
        private int _clearingIndex = -1;

        private InputAction _toggleAction;
        private bool _togglePending;

        public bool IsOpen { get { return _open; } }

        private void Awake()
        {
            if (mapCamera != null)
            {
                mapCamera.enabled = false;
            }

            if (teleportCursor != null)
            {
                teleportCursor.gameObject.SetActive(false);
            }
        }

        private Transform Controlled
        {
            get { return _controlledOverride != null ? _controlledOverride : (_player != null ? _player.transform : null); }
        }

        /// <summary>
        /// Fait suivre la carte a un autre personnage (null = retour au survivant).
        /// Utilise par PlayableCharacterSwitcher quand on joue le demon.
        /// </summary>
        public void SetControlled(Transform body, Camera view)
        {
            if (_open)
            {
                Close();
            }

            _controlledOverride = body;
            _cameraOverride = body != null ? view : null;
        }

        /// <summary>Affiche la position exacte d'une cible sur la maquette pendant 'duration' secondes.</summary>
        public void Reveal(Transform target, float duration)
        {
            Reveal(target, duration, revealColor, null);
        }

        /// <summary>Idem, avec la couleur et le nom propres a la cible (code couleur des survivants).</summary>
        public void Reveal(Transform target, float duration, Color color, string label)
        {
            if (target == null || playerMarker == null)
            {
                return;
            }

            for (int i = 0; i < _revealed.Count; i++)
            {
                if (_revealed[i].target == target)
                {
                    _revealed[i].until = Mathf.Max(_revealed[i].until, Time.time + duration);
                    return;
                }
            }

            GameObject marker = Instantiate(playerMarker.gameObject, playerMarker.parent);
            marker.name = "Revealed_" + target.name;

            foreach (Renderer r in marker.GetComponentsInChildren<Renderer>())
            {
                Material m = r.material; // instance : la balise du joueur garde sa couleur
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                if (m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", color * 2f);
                }
            }

            _revealed.Add(new Revealed { target = target, marker = marker.transform, until = Time.time + duration, color = color, label = label });
        }

        private void UpdateRevealed()
        {
            for (int i = _revealed.Count - 1; i >= 0; i--)
            {
                Revealed r = _revealed[i];

                if (r.target == null || Time.time > r.until)
                {
                    if (r.marker != null) Destroy(r.marker.gameObject);
                    _revealed.RemoveAt(i);
                    continue;
                }

                r.marker.position = WorldToMap(r.target.position) + Vector3.up * markerHover;
                r.marker.rotation = Quaternion.Euler(0f, r.target.eulerAngles.y, 0f);
                r.marker.localScale = Vector3.one * (1.4f + Mathf.Sin(Time.unscaledTime * 9f) * 0.25f);
                r.marker.gameObject.SetActive(_open);
            }
        }

        private void Update()
        {
            UpdateRevealed();

            Keyboard keyboard = Keyboard.current;
            bool playing = GameManager.HasInstance && GameManager.Instance != null && GameManager.Instance.IsPlaying;

            // Pause, fin de partie... : la carte se ferme d'elle-meme.
            if (_open && !playing)
            {
                Close();
                return;
            }

            bool toggle = _togglePending;
            _togglePending = false;

            if (playing && toggle)
            {
                if (_open) Close();
                else Open();
            }

            if (!_open)
            {
                return;
            }

            UpdatePlayerMarker();
            HandleControls(keyboard, Mouse.current);
            PlaceCamera();

            if (enableTeleport)
            {
                HandleTeleport(keyboard, Mouse.current);
            }
        }

        // Une InputAction ne se declenche qu'une fois par appui : lire wasPressedThisFrame
        // dans Update peut compter deux fois le meme appui dans l'editeur (ouvrir + refermer).
        private void OnEnable()
        {
            _toggleAction = new InputAction("Map", InputActionType.Button, toggleBinding);
            _toggleAction.performed += OnTogglePerformed;
            _toggleAction.Enable();
        }

        private void OnTogglePerformed(InputAction.CallbackContext context)
        {
            _togglePending = true; // traite dans Update, avec le reste de l'etat de la carte
        }

        // ------------------------------------------------------------------

        private void Open()
        {
            if (mapCamera == null || diorama == null)
            {
                Debug.LogWarning("[ForestMap3D] Maquette non generee : relance Build Forest Landscape 2k.", this);
                return;
            }

            if (_player == null)
            {
                _player = FindAnyObjectByType<PlayerCharacter>();

                if (_player != null)
                {
                    _input = _player.GetComponent<InputReader>();
                    _playerCamera = _player.Camera;
                }
            }

            _open = true;

            if (_input != null) _input.SetInputEnabled(false);

            _disabledCamera = _cameraOverride != null ? _cameraOverride : _playerCamera;
            if (_disabledCamera != null) _disabledCamera.enabled = false;

            // Le brouillard de nuit noierait la maquette.
            _savedFog = RenderSettings.fog;
            RenderSettings.fog = false;

            mapCamera.enabled = true;

            UpdatePlayerMarker();
            Recenter();
            PlaceCamera();
        }

        private void Close()
        {
            _open = false;
            _hasTarget = false;

            if (teleportCursor != null)
            {
                teleportCursor.gameObject.SetActive(false);
            }

            RenderSettings.fog = _savedFog;

            if (mapCamera != null) mapCamera.enabled = false;
            if (_disabledCamera != null) _disabledCamera.enabled = true;
            _disabledCamera = null;

            bool playing = GameManager.HasInstance && GameManager.Instance != null && GameManager.Instance.IsPlaying;

            if (_input != null && playing)
            {
                _input.SetInputEnabled(true);
            }
        }

        private void OnDisable()
        {
            if (_toggleAction != null)
            {
                _toggleAction.performed -= OnTogglePerformed;
                _toggleAction.Disable();
                _toggleAction.Dispose();
                _toggleAction = null;
            }

            if (_open)
            {
                Close();
            }
        }

        // ------------------------------------------------------------------

        /// <summary>Position monde -> position maquette (au niveau du sol).</summary>
        private Vector3 WorldToMap(Vector3 world)
        {
            float ground = world.y;
            Terrain terrain = Terrain.activeTerrain;

            if (terrain != null)
            {
                ground = terrain.SampleHeight(world);
                world -= terrain.transform.position;
            }

            return diorama.position + new Vector3(world.x * mapScale, ground * mapScale * heightExaggeration, world.z * mapScale);
        }

        private void UpdatePlayerMarker()
        {
            Transform controlled = Controlled;

            if (controlled == null || playerMarker == null)
            {
                return;
            }

            playerMarker.position = WorldToMap(controlled.position) + Vector3.up * markerHover;
            playerMarker.rotation = Quaternion.Euler(0f, controlled.eulerAngles.y, 0f);

            // Leger battement pour reperer le joueur au premier coup d'oeil.
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.12f;
            playerMarker.localScale = Vector3.one * pulse;
        }

        private void Recenter()
        {
            _focus = playerMarker != null ? playerMarker.position : diorama.position;
            _yaw = Controlled != null ? Controlled.eulerAngles.y : 0f;
        }

        private void HandleControls(Keyboard keyboard, Mouse mouse)
        {
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * orbitSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * orbitSensitivity, pitchRange.x, pitchRange.y);

                float scroll = mouse.scroll.ReadValue().y;

                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _distance *= 1f - Mathf.Sign(scroll) * zoomSensitivity * Mathf.Min(3f, Mathf.Abs(scroll) / 120f + 1f);
                    _distance = Mathf.Clamp(_distance, distanceRange.x, distanceRange.y);
                }
            }

            if (keyboard == null)
            {
                return;
            }

            if (keyboard[Key.F].wasPressedThisFrame)
            {
                Recenter();
            }

            // Key.W / Key.A... sont des positions physiques : ZQSD en AZERTY.
            Vector2 pan = Vector2.zero;
            if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed) pan.y += 1f;
            if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed) pan.y -= 1f;
            if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed) pan.x += 1f;
            if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed) pan.x -= 1f;

            if (pan.sqrMagnitude > 0f)
            {
                Quaternion flat = Quaternion.Euler(0f, _yaw, 0f);
                Vector3 move = flat * new Vector3(pan.x, 0f, pan.y).normalized;
                _focus += move * panSpeed * _distance * Time.unscaledDeltaTime;

                // Reste au-dessus de la maquette.
                float half = 2000f * mapScale;
                _focus.x = Mathf.Clamp(_focus.x, diorama.position.x, diorama.position.x + half);
                _focus.z = Mathf.Clamp(_focus.z, diorama.position.z, diorama.position.z + half);
            }
        }

        // ------------------------------------------------------------------
        // Teleportation (outil de test)
        // ------------------------------------------------------------------

        private void HandleTeleport(Keyboard keyboard, Mouse mouse)
        {
            // Tab : la camera saute a la clairiere suivante (balises "Clairiere_XX" de la maquette).
            if (keyboard != null && keyboard[Key.Tab].wasPressedThisFrame)
            {
                FocusNextClearing();
            }

            _hasTarget = false;

            if (dioramaCollider != null)
            {
                Ray ray = mapCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                RaycastHit hit;

                // Collider.Raycast ne teste que la maquette : aucun autre objet ne peut gener.
                if (dioramaCollider.Raycast(ray, out hit, mapCamera.farClipPlane))
                {
                    _hasTarget = true;
                    _targetOnMap = hit.point;
                }
            }

            if (teleportCursor != null)
            {
                teleportCursor.gameObject.SetActive(_hasTarget);

                if (_hasTarget)
                {
                    teleportCursor.position = _targetOnMap;
                }
            }

            bool confirm = (mouse != null && mouse.leftButton.wasPressedThisFrame)
                        || (keyboard != null && keyboard[Key.T].wasPressedThisFrame);

            if (_hasTarget && confirm)
            {
                TeleportPlayer(MapToWorldXZ(_targetOnMap));
            }
        }

        private void FocusNextClearing()
        {
            int count = diorama.childCount;

            for (int step = 0; step < count; step++)
            {
                _clearingIndex = (_clearingIndex + 1) % count;
                Transform child = diorama.GetChild(_clearingIndex);

                if (child.name.StartsWith("Clairiere_"))
                {
                    _focus = child.position;
                    _distance = Mathf.Min(_distance, 45f);
                    return;
                }
            }
        }

        /// <summary>Point de la maquette -> position monde (x, z), y a determiner.</summary>
        private Vector3 MapToWorldXZ(Vector3 mapPoint)
        {
            Vector3 local = (mapPoint - diorama.position) / mapScale;
            Terrain terrain = Terrain.activeTerrain;

            if (terrain != null)
            {
                local += terrain.transform.position;
            }

            return new Vector3(local.x, 0f, local.z);
        }

        /// <summary>
        /// Pose le joueur sur la surface la plus basse ou il tient debout : le sol dehors,
        /// le dallage dans un batiment (jamais sur le toit ni dans le socle), puis ferme la carte.
        /// </summary>
        private void TeleportPlayer(Vector3 worldXZ)
        {
            Transform controlled = Controlled;

            if (controlled == null)
            {
                return;
            }

            Vector3 target = worldXZ;
            Terrain terrain = Terrain.activeTerrain;
            float ground = terrain != null ? terrain.SampleHeight(worldXZ) + terrain.transform.position.y : 0f;
            target.y = ground;

            RaycastHit[] hits = Physics.RaycastAll(new Vector3(worldXZ.x, ground + 300f, worldXZ.z), Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.point.y.CompareTo(b.point.y));

            foreach (RaycastHit h in hits)
            {
                if (IsIgnored(h.collider) || h.normal.y < 0.5f || !HasHeadroom(h.point))
                {
                    continue;
                }

                target.y = h.point.y;
                break;
            }

            Close();

            CharacterController controller = controlled.GetComponent<CharacterController>();

            if (controller != null) controller.enabled = false;
            controlled.position = target + Vector3.up * 0.1f;
            if (controller != null) controller.enabled = true;

            Debug.Log("[ForestMap3D] Teleportation en " + target.ToString("F0"));
        }

        private bool IsIgnored(Collider c)
        {
            Transform controlled = Controlled;
            return c == dioramaCollider || (controlled != null && c.transform.IsChildOf(controlled));
        }

        /// <summary>Place pour un joueur debout (capsule de 1.8 m) au-dessus du point.</summary>
        private bool HasHeadroom(Vector3 point)
        {
            Collider[] overlaps = Physics.OverlapCapsule(point + Vector3.up * 0.4f, point + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore);

            foreach (Collider c in overlaps)
            {
                if (!IsIgnored(c))
                {
                    return false;
                }
            }

            return true;
        }

        private void PlaceCamera()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            mapCamera.transform.position = _focus - rotation * Vector3.forward * _distance;
            mapCamera.transform.rotation = rotation;
        }

        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!_open)
            {
                return;
            }

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label);
                _hintStyle.fontSize = 16;
                _hintStyle.alignment = TextAnchor.LowerCenter;
                _hintStyle.normal.textColor = new Color(0.85f, 0.82f, 0.75f, 0.9f);
            }

            DrawLocationLabels();
            DrawRevealLabels();

            string hint = "CARTE    M fermer   ·   souris tourner   ·   molette zoom   ·   ZQSD deplacer   ·   F recentrer";

            if (enableTeleport)
            {
                hint += "\nclic gauche / T  se teleporter au viseur   ·   Tab  clairiere suivante";

                if (_crosshairStyle == null)
                {
                    _crosshairStyle = new GUIStyle(GUI.skin.label);
                    _crosshairStyle.fontSize = 28;
                    _crosshairStyle.alignment = TextAnchor.MiddleCenter;
                }

                _crosshairStyle.normal.textColor = _hasTarget ? new Color(0.4f, 0.95f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
                GUI.Label(new Rect(Screen.width * 0.5f - 20f, Screen.height * 0.5f - 20f, 40f, 40f), "+", _crosshairStyle);

                // Nom du lieu vise, sous le viseur.
                ForestLocations.Location aimed = _hasTarget && locations != null ? locations.At(MapToWorldXZ(_targetOnMap)) : null;

                if (aimed != null && aimed.showOnMap)
                {
                    DrawShadowLabel(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.5f + 16f, 400f, 26f), aimed.displayName, 17, new Color(0.4f, 0.95f, 1f, 0.95f));
                }
            }

            GUI.Label(new Rect(0f, Screen.height - 70f, Screen.width, 50f), hint, _hintStyle);
        }

        private void DrawRevealLabels()
        {
            if (_revealed.Count == 0 || mapCamera == null)
            {
                return;
            }

            if (_revealStyle == null)
            {
                _revealStyle = new GUIStyle(GUI.skin.label);
                _revealStyle.alignment = TextAnchor.LowerCenter;
                _revealStyle.fontStyle = FontStyle.Bold;
                _revealStyle.fontSize = 15;
            }

            foreach (Revealed r in _revealed)
            {
                if (r.marker == null) continue;
                Vector3 sp = mapCamera.WorldToScreenPoint(r.marker.position + Vector3.up * 3f);
                if (sp.z <= 0f) continue;

                // Au-dessus du nom de la clairiere (qui s'affiche juste sur la balise).
                Rect rect = new Rect(sp.x - 100f, Screen.height - sp.y - 52f, 200f, 24f);
                float blink = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 9f);
                string text = (string.IsNullOrEmpty(r.label) ? revealLabel : r.label) + "  " + Mathf.CeilToInt(r.until - Time.time) + " s";
                _revealStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _revealStyle);
                _revealStyle.normal.textColor = new Color(r.color.r, r.color.g, r.color.b, blink);
                GUI.Label(rect, text, _revealStyle);
            }
        }

        private readonly List<KeyValuePair<float, int>> _labelOrder = new List<KeyValuePair<float, int>>();
        private readonly List<Rect> _drawnLabels = new List<Rect>();

        /// <summary>
        /// Nom de chaque lieu au-dessus de sa balise ; plus petit et plus pale au loin.
        /// Les plus proches passent d'abord : un nom qui en chevaucherait un autre est masque.
        /// </summary>
        private void DrawLocationLabels()
        {
            if (locations == null || diorama == null)
            {
                return;
            }

            _labelOrder.Clear();

            for (int i = 0; i < diorama.childCount; i++)
            {
                Vector3 screen = mapCamera.WorldToScreenPoint(diorama.GetChild(i).position);

                if (screen.z > 0f)
                {
                    _labelOrder.Add(new KeyValuePair<float, int>(screen.z, i));
                }
            }

            _labelOrder.Sort((a, b) => a.Key.CompareTo(b.Key));
            _drawnLabels.Clear();

            foreach (KeyValuePair<float, int> entry in _labelOrder)
            {
                Transform pin = diorama.GetChild(entry.Value);
                ForestLocations.Location location = locations.Find(pin.name);

                if (location == null || !location.showOnMap || string.IsNullOrEmpty(location.displayName))
                {
                    continue;
                }

                Vector3 screen = mapCamera.WorldToScreenPoint(pin.position + Vector3.up * 6.6f);

                if (screen.z <= 0f)
                {
                    continue; // derriere la camera
                }

                float near = Mathf.InverseLerp(260f, 30f, screen.z);
                int size = Mathf.RoundToInt(Mathf.Lerp(11f, 19f, near));
                float width = location.displayName.Length * size * 0.55f + 10f;
                Rect rect = new Rect(screen.x - width * 0.5f, Screen.height - screen.y - 24f, width, 24f);

                bool overlaps = false;

                foreach (Rect drawn in _drawnLabels)
                {
                    if (drawn.Overlaps(rect))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps)
                {
                    continue;
                }

                _drawnLabels.Add(rect);

                Color color = labelColor;
                color.a = Mathf.Lerp(0.45f, 1f, near);
                DrawShadowLabel(rect, location.displayName, size, color);
            }
        }

        private void DrawShadowLabel(Rect rect, string text, int size, Color color)
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.alignment = TextAnchor.MiddleCenter;
                _labelStyle.fontStyle = FontStyle.Bold;
                _labelStyle.wordWrap = false;
                _labelStyle.clipping = TextClipping.Overflow;
            }

            _labelStyle.fontSize = size;

            _labelStyle.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.85f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, _labelStyle);

            _labelStyle.normal.textColor = color;
            GUI.Label(rect, text, _labelStyle);
        }
    }
}
