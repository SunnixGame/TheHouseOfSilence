using System.Collections.Generic;
using HouseOfSilence.Core;
using HouseOfSilence.Core.Debugging;
using HouseOfSilence.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Lights
{
    /// <summary>
    /// Registre de toutes les lampes de la maison et etat du courant general.
    ///
    /// - Courant : les lampes avec Requires Power s'eteignent quand il est coupe.
    ///   Le fusible (FuseBox) puis le disjoncteur (PowerSwitch) le retablissent :
    ///   c'est la chaine des objectifs 3 et 4.
    /// - Creature : la Phase 18 appelle SetMonsterPosition() chaque frame ; le
    ///   manager convertit la distance en proximite pour chaque lampe.
    /// - Panique (Phase 30) : PanicMode fait scintiller toute la maison.
    ///
    /// Debug : F5 bascule toutes les lampes.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightManager : MonoSingleton<LightManager>
    {
        [Header("Courant general")]
        [Tooltip("Etat du courant au demarrage de la partie.")]
        [SerializeField] private bool startPowered = true;

        [Tooltip("Le fusible est-il deja en place au demarrage ?")]
        [SerializeField] private bool startWithFuse = true;

        [Header("Creature")]
        [Tooltip("Distance a partir de laquelle la creature commence a perturber une lampe.")]
        [SerializeField, Min(1f)] private float monsterInfluenceRadius = 9f;

        [Header("Panique")]
        [Tooltip("Intervalle moyen entre deux scintillements par lampe en mode panique.")]
        [SerializeField, Min(0.2f)] private float panicFlickerInterval = 3f;

        [Header("Debug")]
        [SerializeField] private bool registerDebugCommand = true;
        [SerializeField] private Key toggleAllKey = Key.F5;

        // ------------------------------------------------------------------

        private readonly List<LightController> _lights = new List<LightController>(64);

        private bool _powered;
        private bool _fuseInstalled;
        private bool _panicMode;
        private bool _hasMonster;
        private Vector3 _monsterPosition;
        private float _panicTimer;

        /// <summary>Le manager vit dans la scene de jeu.</summary>
        protected override bool IsPersistent { get { return false; } }

        /// <summary>Courant general.</summary>
        public bool IsPowered { get { return _powered; } }

        /// <summary>Fusible en place dans le tableau.</summary>
        public bool IsFuseInstalled { get { return _fuseInstalled; } }

        public bool IsPanicMode { get { return _panicMode; } }

        public int LightCount { get { return _lights.Count; } }

        public IReadOnlyList<LightController> Lights { get { return _lights; } }

        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            _powered = startPowered;
            _fuseInstalled = startWithFuse;
        }

        private void OnEnable()
        {
            if (registerDebugCommand)
            {
                DebugManager.Register(toggleAllKey, "Lumieres : tout basculer", ToggleAll);
                DebugManager.RegisterInfo("Lumieres", BuildDebugInfo);
            }
        }

        private void OnDisable()
        {
            if (registerDebugCommand)
            {
                DebugManager.Unregister(toggleAllKey);
                DebugManager.UnregisterInfo("Lumieres");
            }
        }

        private void Update()
        {
            if (_hasMonster)
            {
                UpdateMonsterInfluence();
            }

            if (_panicMode)
            {
                UpdatePanic();
            }
        }

        // ------------------------------------------------------------------
        // Registre
        // ------------------------------------------------------------------

        public void Register(LightController light)
        {
            if (light == null || _lights.Contains(light))
            {
                return;
            }

            _lights.Add(light);
            light.SetPowered(_powered);
            LightLevelSensor.MarkLightsDirty();
        }

        public void Unregister(LightController light)
        {
            if (light != null && _lights.Remove(light))
            {
                LightLevelSensor.MarkLightsDirty();
            }
        }

        /// <summary>Lampes dans un rayon autour d'un point (interrupteur sans cibles explicites).</summary>
        public void GetLightsWithin(Vector3 position, float radius, List<LightController> results)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();
            float radiusSqr = radius * radius;

            for (int i = 0; i < _lights.Count; i++)
            {
                LightController light = _lights[i];

                if (light != null && (light.transform.position - position).sqrMagnitude <= radiusSqr)
                {
                    results.Add(light);
                }
            }
        }

        // ------------------------------------------------------------------
        // Courant
        // ------------------------------------------------------------------

        /// <summary>Retablit ou coupe le courant general.</summary>
        public void SetPowered(bool powered)
        {
            if (_powered == powered)
            {
                return;
            }

            _powered = powered;

            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] != null)
                {
                    _lights[i].SetPowered(powered);
                }
            }

            Debug.Log("[Lumieres] Courant general : " + (powered ? "RETABLI" : "COUPE"));
            EventBus.Publish(new PowerStateChangedEvent(powered));
        }

        /// <summary>Marque le fusible comme installe (FuseBox).</summary>
        public void SetFuseInstalled(bool installed, Vector3 position)
        {
            if (_fuseInstalled == installed)
            {
                return;
            }

            _fuseInstalled = installed;

            if (installed)
            {
                Debug.Log("[Lumieres] Fusible installe.");
                EventBus.Publish(new FuseInstalledEvent(position));
            }
        }

        // ------------------------------------------------------------------
        // Actions globales
        // ------------------------------------------------------------------

        public void ToggleAll()
        {
            bool anyLit = false;

            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] != null && _lights[i].IsLit)
                {
                    anyLit = true;
                    break;
                }
            }

            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] == null)
                {
                    continue;
                }

                if (anyLit)
                {
                    _lights[i].TurnOff();
                }
                else
                {
                    _lights[i].TurnOn();
                }
            }
        }

        public void AllOn()
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] != null)
                {
                    _lights[i].TurnOn();
                }
            }
        }

        public void AllOff()
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] != null)
                {
                    _lights[i].TurnOff();
                }
            }
        }

        /// <summary>Fait scintiller toutes les lampes (coupure electrique, evenement paranormal).</summary>
        public void FlickerAll(float duration = -1f)
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] != null)
                {
                    _lights[i].StartFlicker(duration, false, false);
                }
            }
        }

        /// <summary>Casse un nombre donne de lampes au hasard (evenement horrifique).</summary>
        public void BreakRandom(int count)
        {
            List<LightController> candidates = new List<LightController>(_lights.Count);

            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] != null && !_lights[i].IsBroken)
                {
                    candidates.Add(_lights[i]);
                }
            }

            for (int n = 0; n < count && candidates.Count > 0; n++)
            {
                int index = Random.Range(0, candidates.Count);
                candidates[index].StartFlicker(Random.Range(0.8f, 2f), true);
                candidates.RemoveAt(index);
            }
        }

        /// <summary>Repare toutes les lampes cassees.</summary>
        public void RepairAll()
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] != null)
                {
                    _lights[i].Repair();
                }
            }
        }

        /// <summary>Mode panique (Phase 30) : la maison entiere scintille par vagues.</summary>
        public void SetPanicMode(bool enabled)
        {
            _panicMode = enabled;
            _panicTimer = 0f;
        }

        private void UpdatePanic()
        {
            _panicTimer -= Time.deltaTime;

            if (_panicTimer > 0f)
            {
                return;
            }

            _panicTimer = Random.Range(panicFlickerInterval * 0.5f, panicFlickerInterval * 1.5f);

            if (_lights.Count == 0)
            {
                return;
            }

            LightController light = _lights[Random.Range(0, _lights.Count)];

            if (light != null)
            {
                light.StartFlicker(Random.Range(0.5f, 2f), false, true);
            }
        }

        // ------------------------------------------------------------------
        // Creature
        // ------------------------------------------------------------------

        /// <summary>Position de la creature, mise a jour chaque frame par la Phase 18.</summary>
        public void SetMonsterPosition(Vector3 position)
        {
            _monsterPosition = position;
            _hasMonster = true;
        }

        /// <summary>Plus aucune creature active.</summary>
        public void ClearMonsterPosition()
        {
            if (!_hasMonster)
            {
                return;
            }

            _hasMonster = false;

            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] != null)
                {
                    _lights[i].SetMonsterProximity(0f);
                }
            }
        }

        private void UpdateMonsterInfluence()
        {
            float radiusSqr = monsterInfluenceRadius * monsterInfluenceRadius;

            for (int i = 0; i < _lights.Count; i++)
            {
                LightController light = _lights[i];

                if (light == null)
                {
                    continue;
                }

                float sqr = (light.transform.position - _monsterPosition).sqrMagnitude;

                if (sqr >= radiusSqr)
                {
                    light.SetMonsterProximity(0f);
                    continue;
                }

                float proximity = 1f - Mathf.Sqrt(sqr) / monsterInfluenceRadius;
                light.SetMonsterProximity(proximity);
            }
        }

        // ------------------------------------------------------------------

        private string BuildDebugInfo()
        {
            int lit = 0;
            int broken = 0;

            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] == null)
                {
                    continue;
                }

                if (_lights[i].IsLit)
                {
                    lit++;
                }

                if (_lights[i].IsBroken)
                {
                    broken++;
                }
            }

            return "  Lampes   : " + lit + " allumee(s) / " + _lights.Count + "   cassees : " + broken
                + "\n  Courant  : " + (_powered ? "OUI" : "NON") + "   Fusible : " + (_fuseInstalled ? "OUI" : "NON")
                + (_panicMode ? "   PANIQUE" : "")
                + (_hasMonster ? "   creature suivie" : "");
        }
    }
}
