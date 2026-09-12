using System.Collections;
using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Core.Debugging;
using HouseOfSilence.Doors;
using HouseOfSilence.Lights;
using HouseOfSilence.Objectives;
using HouseOfSilence.Player;
using HouseOfSilence.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Horror
{
    /// <summary>
    /// Metteur en scene de la maison. Choisit et declenche les evenements
    /// horrifiques en fonction de la peur, du temps de jeu, de la progression
    /// des objectifs et de l'etat de la creature.
    ///
    /// Regles anti-spam :
    /// - rien avant First Event Delay (les premieres minutes sont calmes, cf. §49) ;
    /// - un intervalle global entre deux evenements, qui se resserre avec la
    ///   progression (Interval Early -> Interval Late) ;
    /// - un cooldown propre a chaque evenement et un plafond d'occurrences.
    ///
    /// Le manager ne connait aucune scene : il cherche portes, lampes, props
    /// et spots au moment de l'execution, autour du joueur cible.
    ///
    /// Debug : F8 remet les cooldowns a zero, H declenche un evenement immediatement.
    /// </summary>
    [DisallowMultipleComponent]
    public class HorrorEventManager : MonoSingleton<HorrorEventManager>
    {
        [Header("Catalogue")]
        [Tooltip("Evenements possibles. Glisser les assets HorrorEventData ici.")]
        [SerializeField] private List<HorrorEventData> events = new List<HorrorEventData>();

        [Header("Rythme")]
        [SerializeField] private bool eventsEnabled = true;

        [Tooltip("Aucun evenement avant ce temps de jeu, en secondes (minute 1-2 : premier evenement subtil).")]
        [SerializeField, Min(0f)] private float firstEventDelay = 75f;

        [Tooltip("Intervalle moyen entre deux evenements en debut de partie.")]
        [SerializeField, Min(1f)] private float intervalEarly = 45f;

        [Tooltip("Intervalle moyen quand tous les objectifs sont presque faits.")]
        [SerializeField, Min(1f)] private float intervalLate = 15f;

        [Tooltip("Intervalle en mode panique (Phase 30).")]
        [SerializeField, Min(1f)] private float intervalPanic = 7f;

        [Tooltip("Variation aleatoire autour de l'intervalle (0.3 = de -30 % a +30 %).")]
        [SerializeField, Range(0f, 0.9f)] private float intervalJitter = 0.35f;

        [Header("Difficulte (Phase 45)")]
        [SerializeField, Min(0.1f)] private float frequencyMultiplier = 1f;

        [Header("Debug")]
        [SerializeField] private bool registerDebugCommands = true;
        [SerializeField] private Key resetKey = Key.F8;
        [SerializeField] private Key triggerNowKey = Key.H;
        [SerializeField] private bool logToConsole = true;

        // ------------------------------------------------------------------

        private readonly Dictionary<HorrorEventData, float> _lastTriggerTime = new Dictionary<HorrorEventData, float>(16);
        private readonly Dictionary<HorrorEventData, int> _occurrences = new Dictionary<HorrorEventData, int>(16);
        private readonly List<HorrorEventData> _eligible = new List<HorrorEventData>(16);
        private readonly List<DoorBase> _doors = new List<DoorBase>(32);
        private readonly List<LightController> _lightBuffer = new List<LightController>(16);

        private float _timer;
        private float _doorRefreshTimer;
        private bool _panicMode;
        private bool _monsterActive;
        private int _totalTriggered;
        private string _lastEventName = "-";
        private Coroutine _powerCutRoutine;

        /// <summary>Le manager vit dans la scene de jeu.</summary>
        protected override bool IsPersistent { get { return false; } }

        public bool EventsEnabled
        {
            get { return eventsEnabled; }
            set { eventsEnabled = value; }
        }

        public bool IsPanicMode { get { return _panicMode; } }
        public int TotalTriggered { get { return _totalTriggered; } }

        /// <summary>Secondes avant la prochaine tentative.</summary>
        public float TimeToNext { get { return Mathf.Max(0f, _timer); } }

        // ------------------------------------------------------------------
        // Cycle de vie
        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            _timer = 0f;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<GameStartedEvent>(OnGameStarted);

            if (registerDebugCommands)
            {
                DebugManager.Register(resetKey, "Evenements : reset cooldowns", ResetCooldowns);
                DebugManager.Register(triggerNowKey, "Evenements : declencher maintenant", TriggerNow);
                DebugManager.RegisterInfo("Evenements", BuildDebugInfo);
            }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<GameStartedEvent>(OnGameStarted);

            if (registerDebugCommands)
            {
                DebugManager.Unregister(resetKey);
                DebugManager.Unregister(triggerNowKey);
                DebugManager.UnregisterInfo("Evenements");
            }
        }

        private void OnGameStarted(GameStartedEvent evt)
        {
            ResetCooldowns();
        }

        private void Update()
        {
            if (!eventsEnabled)
            {
                return;
            }

            GameManager game = GameManager.HasInstance ? GameManager.Instance : null;

            if (game == null || !game.IsPlaying)
            {
                return;
            }

            // Autorite : en coop, seul le host met en scene (Phase 18).
            if (!game.HasAuthority)
            {
                return;
            }

            if (game.PlayTime < firstEventDelay)
            {
                _timer = 0f;
                return;
            }

            _doorRefreshTimer -= Time.deltaTime;

            if (_doorRefreshTimer <= 0f)
            {
                RefreshDoors();
            }

            _timer -= Time.deltaTime;

            if (_timer > 0f)
            {
                return;
            }

            if (TryTriggerRandomEvent(false))
            {
                _timer = ComputeInterval();
            }
            else
            {
                // Rien d'eligible pour l'instant : on reessaie bientot.
                _timer = 4f;
            }
        }

        private float ComputeInterval()
        {
            float interval;

            if (_panicMode)
            {
                interval = intervalPanic;
            }
            else
            {
                float progress = 0f;

                if (ObjectiveManager.HasInstance && ObjectiveManager.Instance != null && ObjectiveManager.Instance.TotalCount > 0)
                {
                    progress = (float)ObjectiveManager.Instance.CompletedCount / ObjectiveManager.Instance.TotalCount;
                }

                interval = Mathf.Lerp(intervalEarly, intervalLate, progress);
            }

            interval /= Mathf.Max(0.1f, frequencyMultiplier);

            return interval * Random.Range(1f - intervalJitter, 1f + intervalJitter);
        }

        // ------------------------------------------------------------------
        // Selection
        // ------------------------------------------------------------------

        private bool TryTriggerRandomEvent(bool ignoreTimeGates)
        {
            PlayerCharacter target = PickTargetPlayer();

            if (target == null)
            {
                return false;
            }

            FearSystem fear = target.GetComponent<FearSystem>();
            float fearValue = fear != null ? fear.Current : 0f;
            float gameTime = GameManager.Instance != null ? GameManager.Instance.PlayTime : 0f;

            int objectiveIndex = 0;

            if (ObjectiveManager.HasInstance && ObjectiveManager.Instance != null)
            {
                objectiveIndex = Mathf.Max(0, ObjectiveManager.Instance.CurrentIndex);
            }

            bool powered = !LightManager.HasInstance || LightManager.Instance == null || LightManager.Instance.IsPowered;

            _eligible.Clear();
            float totalWeight = 0f;

            for (int i = 0; i < events.Count; i++)
            {
                HorrorEventData data = events[i];

                if (data == null || !IsEligible(data, fearValue, gameTime, objectiveIndex, powered, target, ignoreTimeGates))
                {
                    continue;
                }

                _eligible.Add(data);
                totalWeight += data.Weight;
            }

            if (_eligible.Count == 0)
            {
                return false;
            }

            // Tirage pondere.
            float roll = Random.value * totalWeight;
            HorrorEventData chosen = _eligible[_eligible.Count - 1];

            for (int i = 0; i < _eligible.Count; i++)
            {
                roll -= _eligible[i].Weight;

                if (roll <= 0f)
                {
                    chosen = _eligible[i];
                    break;
                }
            }

            return Execute(chosen, target);
        }

        private bool IsEligible(HorrorEventData data, float fear, float gameTime, int objectiveIndex, bool powered, PlayerCharacter target, bool ignoreTimeGates)
        {
            if (fear < data.MinFear || fear > data.MaxFear)
            {
                return false;
            }

            if (!ignoreTimeGates && gameTime < data.MinGameTime)
            {
                return false;
            }

            if (objectiveIndex < data.MinObjectiveIndex)
            {
                return false;
            }

            if (data.MaxObjectiveIndex >= 0 && objectiveIndex > data.MaxObjectiveIndex)
            {
                return false;
            }

            if (data.MonsterRequirement == MonsterRequirement.MonsterActive && !_monsterActive)
            {
                return false;
            }

            if (data.MonsterRequirement == MonsterRequirement.MonsterDormant && _monsterActive)
            {
                return false;
            }

            if (data.RequiresPower && !powered)
            {
                return false;
            }

            int count;

            if (data.MaxOccurrences > 0 && _occurrences.TryGetValue(data, out count) && count >= data.MaxOccurrences)
            {
                return false;
            }

            float last;

            if (_lastTriggerTime.TryGetValue(data, out last) && Time.time - last < data.Cooldown)
            {
                return false;
            }

            // Pre-verification : y a-t-il une cible pour ce genre d'evenement ?
            return HasTarget(data, target);
        }

        private static PlayerCharacter PickTargetPlayer()
        {
            if (!PlayerManager.HasInstance)
            {
                return null;
            }

            PlayerManager players = PlayerManager.Instance;

            if (players == null || players.PlayerCount == 0)
            {
                return null;
            }

            // En coop : un joueur vivant au hasard, pour repartir les frayeurs.
            int alive = players.AlivePlayerCount;

            if (alive == 0)
            {
                return null;
            }

            int pick = Random.Range(0, alive);

            for (int i = 0; i < players.Players.Count; i++)
            {
                PlayerCharacter p = players.Players[i];

                if (p == null || !p.IsAlive)
                {
                    continue;
                }

                if (pick == 0)
                {
                    return p;
                }

                pick--;
            }

            return players.LocalPlayer;
        }

        // ------------------------------------------------------------------
        // Execution
        // ------------------------------------------------------------------

        private bool HasTarget(HorrorEventData data, PlayerCharacter player)
        {
            switch (data.Kind)
            {
                case HorrorEventKind.DoorSlam:
                    return FindDoor(player, data, true) != null;

                case HorrorEventKind.DoorOpen:
                    return FindDoor(player, data, false) != null;

                case HorrorEventKind.LightFlicker:
                case HorrorEventKind.LightBreak:
                    return FindLights(player, data, data.Kind == HorrorEventKind.LightBreak) > 0;

                case HorrorEventKind.PowerCut:
                    return _powerCutRoutine == null;

                case HorrorEventKind.ObjectFall:
                    return FindProp(player, data) != null;

                case HorrorEventKind.Sound:
                case HorrorEventKind.Apparition:
                {
                    Vector3 position;
                    return TryResolvePosition(data, player, out position);
                }

                default:
                    return true;
            }
        }

        private bool Execute(HorrorEventData data, PlayerCharacter player)
        {
            Vector3 position = player.FeetPosition;
            bool done = false;

            switch (data.Kind)
            {
                case HorrorEventKind.DoorSlam:
                {
                    DoorBase door = FindDoor(player, data, true);

                    if (door != null)
                    {
                        position = door.transform.position;
                        door.Slam(DoorActor.HorrorEvent);
                        done = true;
                    }

                    break;
                }

                case HorrorEventKind.DoorOpen:
                {
                    DoorBase door = FindDoor(player, data, false);

                    if (door != null)
                    {
                        position = door.transform.position;
                        door.Open(DoorActor.HorrorEvent);
                        done = true;
                    }

                    break;
                }

                case HorrorEventKind.LightFlicker:
                {
                    int count = FindLights(player, data, false);

                    if (count > 0)
                    {
                        int toFlicker = Mathf.Min(count, Random.Range(1, 4));

                        for (int i = 0; i < toFlicker; i++)
                        {
                            _lightBuffer[i].StartFlicker(data.Duration > 0f ? data.Duration : -1f, false, false);
                        }

                        position = _lightBuffer[0].transform.position;
                        done = true;
                    }

                    break;
                }

                case HorrorEventKind.LightBreak:
                {
                    if (FindLights(player, data, true) > 0)
                    {
                        LightController light = _lightBuffer[Random.Range(0, _lightBuffer.Count)];
                        position = light.transform.position;
                        light.StartFlicker(Mathf.Max(0.5f, data.Duration), true, true);
                        done = true;
                    }

                    break;
                }

                case HorrorEventKind.PowerCut:
                {
                    if (_powerCutRoutine == null && LightManager.HasInstance && LightManager.Instance != null)
                    {
                        _powerCutRoutine = StartCoroutine(PowerCutRoutine(Mathf.Max(1f, data.Duration)));
                        done = true;
                    }

                    break;
                }

                case HorrorEventKind.ObjectFall:
                {
                    HorrorProp prop = FindProp(player, data);

                    if (prop != null)
                    {
                        position = prop.transform.position;
                        prop.Nudge();
                        done = true;
                    }

                    break;
                }

                case HorrorEventKind.Sound:
                {
                    if (TryResolvePosition(data, player, out position))
                    {
                        PlaySound(data, position);
                        done = true;
                    }

                    break;
                }

                case HorrorEventKind.Apparition:
                {
                    if (TryResolvePosition(data, player, out position))
                    {
                        PlaySound(data, position);
                        SpawnVisual(data, position, player);
                        done = true;
                    }

                    break;
                }
            }

            if (!done)
            {
                return false;
            }

            Commit(data, position);
            return true;
        }

        private void Commit(HorrorEventData data, Vector3 position)
        {
            _lastTriggerTime[data] = Time.time;

            int count;
            _occurrences.TryGetValue(data, out count);
            _occurrences[data] = count + 1;

            _totalTriggered++;
            _lastEventName = data.DisplayName;

            if (data.NoiseRadius > 0f)
            {
                Noise.Emit(position, data.NoiseRadius, NoiseType.Event, gameObject);
            }

            ApplyFear(data, position);

            if (logToConsole)
            {
                Debug.Log("[Evenement] " + data.DisplayName + "  (" + data.Kind + ") en " + position.ToString("F1"));
            }

            EventBus.Publish(new HorrorEventTriggeredEvent(data, position));
        }

        private static void ApplyFear(HorrorEventData data, Vector3 position)
        {
            if (data.FearImpact <= 0f || !PlayerManager.HasInstance)
            {
                return;
            }

            PlayerManager players = PlayerManager.Instance;

            if (players == null)
            {
                return;
            }

            for (int i = 0; i < players.Players.Count; i++)
            {
                PlayerCharacter p = players.Players[i];

                if (p == null || !p.IsAlive)
                {
                    continue;
                }

                float distance = Vector3.Distance(p.FeetPosition, position);

                if (distance > data.FearRadius)
                {
                    continue;
                }

                FearSystem fear = p.GetComponent<FearSystem>();

                if (fear == null)
                {
                    continue;
                }

                // Pleine dose a bout portant, moitie a la limite du rayon.
                float falloff = 1f - 0.5f * Mathf.Clamp01(distance / Mathf.Max(0.1f, data.FearRadius));
                fear.AddFear(data.FearImpact * falloff, data.DisplayName);
            }
        }

        // ------------------------------------------------------------------
        // Cibles
        // ------------------------------------------------------------------

        private void RefreshDoors()
        {
            _doorRefreshTimer = 10f;
            _doors.Clear();

            DoorBase[] found = FindObjectsByType<DoorBase>(FindObjectsInactive.Exclude);

            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                {
                    _doors.Add(found[i]);
                }
            }
        }

        private DoorBase FindDoor(PlayerCharacter player, HorrorEventData data, bool wantOpen)
        {
            if (_doors.Count == 0)
            {
                RefreshDoors();
            }

            DoorBase best = null;
            float bestScore = float.MaxValue;
            Vector3 origin = player.FeetPosition;

            for (int i = 0; i < _doors.Count; i++)
            {
                DoorBase door = _doors[i];

                if (door == null || door.State == DoorState.Broken)
                {
                    continue;
                }

                if (wantOpen)
                {
                    if (door.State != DoorState.Open)
                    {
                        continue;
                    }
                }
                else
                {
                    if (door.State != DoorState.Closed || door.IsLocked)
                    {
                        continue;
                    }
                }

                float distance = Vector3.Distance(door.transform.position, origin);

                if (distance < data.MinDistance || distance > data.MaxDistance)
                {
                    continue;
                }

                // On prefere une porte proche, avec une part de hasard pour ne pas etre previsible.
                float score = distance + Random.Range(0f, 4f);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = door;
                }
            }

            return best;
        }

        private int FindLights(PlayerCharacter player, HorrorEventData data, bool mustBeLit)
        {
            _lightBuffer.Clear();

            if (!LightManager.HasInstance || LightManager.Instance == null)
            {
                return 0;
            }

            IReadOnlyList<LightController> all = LightManager.Instance.Lights;
            Vector3 origin = player.FeetPosition;

            for (int i = 0; i < all.Count; i++)
            {
                LightController light = all[i];

                if (light == null || light.IsBroken)
                {
                    continue;
                }

                if (mustBeLit && !light.IsLit)
                {
                    continue;
                }

                if (!mustBeLit && light.State != LightState.On)
                {
                    continue;
                }

                float distance = Vector3.Distance(light.transform.position, origin);

                if (distance < data.MinDistance || distance > data.MaxDistance)
                {
                    continue;
                }

                _lightBuffer.Add(light);
            }

            // Melange leger pour varier les lampes choisies.
            for (int i = _lightBuffer.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                LightController tmp = _lightBuffer[i];
                _lightBuffer[i] = _lightBuffer[j];
                _lightBuffer[j] = tmp;
            }

            return _lightBuffer.Count;
        }

        private static HorrorProp FindProp(PlayerCharacter player, HorrorEventData data)
        {
            IReadOnlyList<HorrorProp> all = HorrorProp.All;
            Vector3 origin = player.FeetPosition;

            HorrorProp best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < all.Count; i++)
            {
                HorrorProp prop = all[i];

                if (prop == null || !prop.IsAvailable)
                {
                    continue;
                }

                float distance = Vector3.Distance(prop.transform.position, origin);

                if (distance < data.MinDistance || distance > data.MaxDistance)
                {
                    continue;
                }

                float score = distance + Random.Range(0f, 5f);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = prop;
                }
            }

            return best;
        }

        private static bool TryResolvePosition(HorrorEventData data, PlayerCharacter player, out Vector3 position)
        {
            Vector3 origin = player.FeetPosition;
            Transform cameraTransform = player.Camera != null ? player.Camera.transform : player.transform;
            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = player.transform.forward;
            }

            forward.Normalize();
            float distance = Random.Range(data.MinDistance, data.MaxDistance);

            switch (data.PositionMode)
            {
                case HorrorPositionMode.BehindPlayer:
                    position = origin - forward * distance + Vector3.up * 1.2f;
                    return true;

                case HorrorPositionMode.InFrontOfPlayer:
                    position = origin + forward * distance + Vector3.up * 1.2f;
                    return true;

                case HorrorPositionMode.AroundPlayer:
                {
                    Vector2 circle = Random.insideUnitCircle.normalized;
                    position = origin + new Vector3(circle.x, 0f, circle.y) * distance + Vector3.up * 1.2f;
                    return true;
                }

                case HorrorPositionMode.NearestSpot:
                case HorrorPositionMode.RandomSpot:
                {
                    IReadOnlyList<HorrorSpot> spots = HorrorSpot.All;
                    HorrorSpot chosen = null;
                    float bestDistance = float.MaxValue;
                    int matching = 0;

                    for (int i = 0; i < spots.Count; i++)
                    {
                        HorrorSpot spot = spots[i];

                        if (spot == null || !spot.Matches(data.SpotTag))
                        {
                            continue;
                        }

                        float d = Vector3.Distance(spot.transform.position, origin);

                        if (d < data.MinDistance || d > data.MaxDistance)
                        {
                            continue;
                        }

                        matching++;

                        if (data.PositionMode == HorrorPositionMode.NearestSpot)
                        {
                            if (d < bestDistance)
                            {
                                bestDistance = d;
                                chosen = spot;
                            }
                        }
                        else if (Random.Range(0, matching) == 0)
                        {
                            // Reservoir sampling : choix uniforme sans liste intermediaire.
                            chosen = spot;
                        }
                    }

                    if (chosen == null)
                    {
                        position = origin;
                        return false;
                    }

                    position = chosen.transform.position;
                    return true;
                }
            }

            position = origin;
            return false;
        }

        // ------------------------------------------------------------------
        // Effets
        // ------------------------------------------------------------------

        private static void PlaySound(HorrorEventData data, Vector3 position)
        {
            if (data.Sound == null)
            {
                return;
            }

            AudioSource.PlayClipAtPoint(data.Sound, position, data.SoundVolume);
        }

        private void SpawnVisual(HorrorEventData data, Vector3 position, PlayerCharacter player)
        {
            if (data.VisualPrefab == null)
            {
                return;
            }

            Vector3 toPlayer = player.FeetPosition - position;
            toPlayer.y = 0f;

            Quaternion rotation = toPlayer.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(toPlayer.normalized, Vector3.up)
                : Quaternion.identity;

            GameObject instance = Instantiate(data.VisualPrefab, position, rotation);
            Destroy(instance, Mathf.Max(0.2f, data.Duration));
        }

        private IEnumerator PowerCutRoutine(float duration)
        {
            LightManager lights = LightManager.Instance;

            if (lights == null)
            {
                _powerCutRoutine = null;
                yield break;
            }

            lights.FlickerAll(0.8f);
            yield return new WaitForSeconds(0.8f);

            lights.SetPowered(false);
            yield return new WaitForSeconds(duration);

            lights.SetPowered(true);
            lights.FlickerAll(1.2f);

            _powerCutRoutine = null;
        }

        // ------------------------------------------------------------------
        // API publique
        // ------------------------------------------------------------------

        /// <summary>Declenche un evenement precis, en ignorant conditions et cooldowns (scenario, debug).</summary>
        public bool ForceTrigger(HorrorEventData data)
        {
            if (data == null)
            {
                return false;
            }

            PlayerCharacter target = PickTargetPlayer();

            if (target == null)
            {
                return false;
            }

            return Execute(data, target);
        }

        /// <summary>
        /// Declenche un evenement eligible tout de suite (touche H). Les delais
        /// de temps de jeu sont ignores pour pouvoir tester des le debut ;
        /// les conditions de peur, de cibles et les cooldowns restent respectes.
        /// </summary>
        public void TriggerNow()
        {
            if (!TryTriggerRandomEvent(true))
            {
                Debug.Log("[Evenement] Aucun evenement eligible pour l'instant (peur, cibles a portee ou cooldowns). F8 remet les cooldowns a zero.");
                return;
            }

            _timer = ComputeInterval();
        }

        /// <summary>Remet les cooldowns et les compteurs a zero (touche F8, nouvelle partie).</summary>
        public void ResetCooldowns()
        {
            _lastTriggerTime.Clear();
            _occurrences.Clear();
            _totalTriggered = 0;
            _lastEventName = "-";
            _timer = 0f;

            if (logToConsole)
            {
                Debug.Log("[Evenement] Cooldowns et compteurs remis a zero.");
            }
        }

        /// <summary>Mode panique (Phase 30) : evenements rapproches.</summary>
        public void SetPanicMode(bool enabled)
        {
            _panicMode = enabled;

            if (enabled)
            {
                _timer = Mathf.Min(_timer, 2f);
            }
        }

        /// <summary>La creature est-elle eveillee ? Renseigne par la Phase 11.</summary>
        public void SetMonsterActive(bool active)
        {
            _monsterActive = active;
        }

        // ------------------------------------------------------------------

        private string BuildDebugInfo()
        {
            GameManager game = GameManager.HasInstance ? GameManager.Instance : null;
            float playTime = game != null ? game.PlayTime : 0f;

            string state;

            if (!eventsEnabled)
            {
                state = "desactives";
            }
            else if (playTime < firstEventDelay)
            {
                state = "calme initial (" + Mathf.CeilToInt(firstEventDelay - playTime) + " s)";
            }
            else
            {
                state = "prochain dans " + Mathf.CeilToInt(TimeToNext) + " s";
            }

            return "  Etat     : " + state + (_panicMode ? "   PANIQUE" : "") + (_monsterActive ? "   creature eveillee" : "")
                + "\n  Declenches : " + _totalTriggered + "   dernier : " + _lastEventName
                + "\n  Catalogue : " + events.Count + " evenement(s), " + HorrorProp.All.Count + " prop(s), " + HorrorSpot.All.Count + " spot(s)";
        }
    }
}
