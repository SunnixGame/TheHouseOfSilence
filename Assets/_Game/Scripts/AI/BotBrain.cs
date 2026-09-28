using System.Collections.Generic;
using HouseOfSilence.Demon;
using HouseOfSilence.Network;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.AI
{
    /// <summary>
    /// IA d'un bot (partie en ligne, sur la machine de l'hote uniquement ; chez les autres
    /// joueurs le personnage suit le reseau comme un joueur humain).
    ///
    /// Survivant : se promene d'un point a l'autre, fuit en courant quand le demon approche,
    /// lampe allumee. Demon : traque le survivant vivant le plus proche, court a portee,
    /// crie de temps en temps, se teleporte (et se deguise) s'il est tres loin, et tue au
    /// contact ; la mort passe par l'hote comme pour un joueur (GameStateManager).
    ///
    /// Deplacement direct par le CharacterController (pas de NavMesh dans la foret) :
    /// evitement des obstacles devant soi et deblocage si le bot reste coince.
    /// </summary>
    [DisallowMultipleComponent]
    public class BotBrain : MonoBehaviour
    {
        [Header("Survivant")]
        [SerializeField, Min(1f)] private float fleeRadius = 22f;
        [SerializeField, Min(5f)] private float wanderRadius = 60f;

        [Header("Demon")]
        [SerializeField, Min(0.5f)] private float killRange = 1.1f;
        [SerializeField, Min(0f)] private float killChargeSeconds = 1f;
        [SerializeField, Min(5f)] private float runRange = 30f;
        [SerializeField, Min(20f)] private float teleportRange = 90f;
        [SerializeField, Min(5f)] private float teleportCooldown = 45f;
        [SerializeField, Min(5f)] private float screamCooldown = 35f;

        [Header("Deplacement")]
        [SerializeField] private float gravity = -20f;
        [SerializeField, Min(0f)] private float turnSpeed = 360f;

        private bool _isDemon;
        private CharacterController _controller;
        private SurvivorDeath _death;
        private DemonController _demon;
        private DemonKill _kill;
        private DemonPowers _powers;
        private Flashlight _flashlight;

        private float _walkSpeed = 2.4f;
        private float _runSpeed = 4.6f;
        private Vector3 _destination;
        private float _nextDecision;
        private float _pauseUntil;
        private float _verticalVelocity;
        private float _speed;

        private Vector3 _stuckCheckPosition;
        private float _stuckCheckAt;
        private Vector3 _avoidDirection;
        private float _avoidUntil;

        private float _zigzagSeed;
        private float _killCharge;
        private float _nextTeleport;
        private float _nextScream;

        public void Setup(bool isDemon)
        {
            _isDemon = isDemon;
            _controller = GetComponent<CharacterController>();
            _death = GetComponent<SurvivorDeath>();
            _demon = GetComponent<DemonController>();
            _kill = GetComponent<DemonKill>();
            _powers = GetComponent<DemonPowers>();
            _flashlight = GetComponent<Flashlight>();

            if (_demon != null)
            {
                _walkSpeed = _demon.WalkSpeed;
                _runSpeed = _demon.RunSpeed;
            }
            else
            {
                PlayerMotor motor = GetComponent<PlayerMotor>();
                if (motor != null)
                {
                    _walkSpeed = motor.WalkSpeed;
                    _runSpeed = motor.RunSpeed;
                }
            }

            if (_flashlight != null) _flashlight.SetOn(true);

            _destination = transform.position;
            _zigzagSeed = Random.Range(0f, 100f); // chaque bot zigzague a sa facon
            _nextTeleport = Time.time + teleportCooldown * 0.5f;
            _nextScream = Time.time + Random.Range(10f, screamCooldown);
            _stuckCheckAt = Time.time + 1.5f;
            _stuckCheckPosition = transform.position;
        }

        private void Update()
        {
            if (_controller == null || !_controller.enabled) return;
            if (_death != null && _death.IsDead) return;
            if (_kill != null && _kill.IsExecuting) return;

            Vector3 wish;
            bool run;

            if (_isDemon) ThinkDemon(out wish, out run);
            else ThinkSurvivor(out wish, out run);

            Move(wish, run);
        }

        // ------------------------------------------------------------------
        // Survivant
        // ------------------------------------------------------------------

        private void ThinkSurvivor(out Vector3 wish, out bool run)
        {
            NetworkGameManager game = NetworkGameManager.Instance;
            DemonController demon = null;

            if (game != null)
            {
                foreach (Component c in game.Characters)
                {
                    if (c is DemonController) demon = (DemonController)c;
                }
            }

            if (demon != null)
            {
                Vector3 away = transform.position - demon.transform.position;
                away.y = 0f;

                if (away.magnitude < fleeRadius)
                {
                    // Fuite : a l'oppose du demon, en zigzag leger.
                    float zig = Mathf.Sin(Time.time * 0.8f + _zigzagSeed) * 35f;
                    wish = Quaternion.Euler(0f, zig, 0f) * away.normalized;
                    run = true;
                    _pauseUntil = 0f;
                    return;
                }
            }

            Wander(out wish);
            run = false;
        }

        private void Wander(out Vector3 wish)
        {
            Vector3 toDestination = _destination - transform.position;
            toDestination.y = 0f;

            if (toDestination.magnitude < 1.5f || Time.time >= _nextDecision)
            {
                // Petite pause, puis un nouveau point.
                if (_pauseUntil <= 0f) _pauseUntil = Time.time + Random.Range(1f, 4f);

                if (Time.time >= _pauseUntil)
                {
                    Vector2 r = Random.insideUnitCircle * wanderRadius;
                    _destination = transform.position + new Vector3(r.x, 0f, r.y);
                    _nextDecision = Time.time + Random.Range(12f, 25f);
                    _pauseUntil = 0f;
                }

                wish = Vector3.zero;
                return;
            }

            wish = toDestination.normalized;
        }

        // ------------------------------------------------------------------
        // Demon
        // ------------------------------------------------------------------

        private void ThinkDemon(out Vector3 wish, out bool run)
        {
            NetworkGameManager game = NetworkGameManager.Instance;
            PlayerCharacter target = null;
            float best = float.MaxValue;
            List<PlayerCharacter> alive = new List<PlayerCharacter>();

            if (game != null && game.IsHost)
            {
                foreach (int index in game.GameState.AliveSurvivorCharacters())
                {
                    PlayerCharacter p = game.Get<PlayerCharacter>(index);
                    if (p == null) continue;

                    alive.Add(p);
                    float d = Flat(p.transform.position - transform.position).magnitude;
                    if (d < best)
                    {
                        best = d;
                        target = p;
                    }
                }
            }

            if (target == null)
            {
                Wander(out wish);
                run = false;
                _killCharge = 0f;
                return;
            }

            // Tres loin : teleportation pres de la cible, sous l'apparence d'un autre survivant.
            if (best > teleportRange && Time.time >= _nextTeleport)
            {
                _nextTeleport = Time.time + teleportCooldown;
                TeleportNear(target, alive);
            }

            if (Time.time >= _nextScream && best < 80f)
            {
                _nextScream = Time.time + screamCooldown;
                if (_powers != null)
                {
                    _powers.PlayScreamSound();
                    NetBridge.RaiseDemonScreamed(_powers);
                }
            }

            // Au contact : la jauge se charge, puis la mise a mort (validee par l'hote).
            if (best <= killRange)
            {
                _killCharge += Time.deltaTime;
                if (_killCharge >= killChargeSeconds)
                {
                    _killCharge = 0f;
                    game.GameState.ServerBotKill(game.IndexOf(_demon), game.IndexOf(target), true);
                }

                wish = Vector3.zero;
                Face(target.transform.position);
                run = false;
                return;
            }

            _killCharge = Mathf.Max(0f, _killCharge - Time.deltaTime);
            wish = Flat(target.transform.position - transform.position).normalized;
            run = best < runRange;
        }

        private void TeleportNear(PlayerCharacter target, List<PlayerCharacter> alive)
        {
            if (_demon == null) return;

            Vector3 from = transform.position;
            Vector2 r = Random.insideUnitCircle.normalized * 10f;
            Vector3 point = target.transform.position + new Vector3(r.x, 0f, r.y);

            // Au sol (terrain) sous le point choisi.
            RaycastHit hit;
            if (Physics.Raycast(point + Vector3.up * 30f, Vector3.down, out hit, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
            }

            Vector3 look = Flat(target.transform.position - point);
            _demon.TeleportTo(point, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : transform.rotation);

            if (_powers != null)
            {
                _powers.PlayTeleportSounds(from, point);
                NetBridge.RaiseDemonTeleported(_powers, from, point);

                // Deguisement : un survivant autre que la cible.
                List<PlayerCharacter> others = alive.FindAll(p => p != target);
                if (others.Count > 0)
                {
                    PlayerCharacter disguise = others[Random.Range(0, others.Count)];
                    if (_powers.ApplyDisguise(disguise, 10f)) NetBridge.RaiseDemonDisguised(_powers, disguise, 10f);
                }
            }
        }

        // ------------------------------------------------------------------
        // Deplacement
        // ------------------------------------------------------------------

        private void Move(Vector3 wish, bool run)
        {
            float dt = Time.deltaTime;
            float targetSpeed = wish.sqrMagnitude > 0.01f ? (run ? _runSpeed : _walkSpeed) : 0f;
            _speed = Mathf.MoveTowards(_speed, targetSpeed, 10f * dt);

            if (wish.sqrMagnitude > 0.01f)
            {
                wish = Avoid(wish);
                Face(transform.position + wish);
            }

            // Coince (arbre, rocher) : on part de cote un moment.
            if (Time.time >= _stuckCheckAt)
            {
                bool stuck = targetSpeed > 0.5f && Flat(transform.position - _stuckCheckPosition).magnitude < 0.4f;
                if (stuck)
                {
                    _avoidDirection = Quaternion.Euler(0f, Random.Range(90f, 270f), 0f) * transform.forward;
                    _avoidUntil = Time.time + 1.5f;
                }

                _stuckCheckAt = Time.time + 1.2f;
                _stuckCheckPosition = transform.position;
            }

            Vector3 direction = Time.time < _avoidUntil ? Flat(_avoidDirection).normalized : Flat(transform.forward);

            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * dt;

            _controller.Move((direction * _speed + Vector3.up * _verticalVelocity) * dt);
        }

        /// <summary>Obstacle devant : on contourne par le cote le plus libre.</summary>
        private Vector3 Avoid(Vector3 wish)
        {
            if (!Blocked(wish)) return wish;

            foreach (float angle in new[] { 45f, -45f, 90f, -90f, 135f, -135f })
            {
                Vector3 side = Quaternion.Euler(0f, angle, 0f) * wish;
                if (!Blocked(side)) return side;
            }

            return -wish;
        }

        private bool Blocked(Vector3 direction)
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            RaycastHit[] hits = Physics.SphereCastAll(origin, 0.3f, direction, 1.6f, ~0, QueryTriggerInteraction.Ignore);

            foreach (RaycastHit h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.normal.y > 0.6f) continue; // sol en pente douce
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue; // cadavres
                return true;
            }

            return false;
        }

        private void Face(Vector3 point)
        {
            Vector3 look = Flat(point - transform.position);
            if (look.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(look);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
