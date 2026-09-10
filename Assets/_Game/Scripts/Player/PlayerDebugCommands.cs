using System.Text;
using HouseOfSilence.Core.Debugging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Player
{
    /// <summary>
    /// Branche le joueur sur le DebugManager :
    /// - F7  : tuer le joueur (touche prevue au cahier des charges) ;
    /// - F6  : basculer le mode invincible ;
    /// - bloc d'information "Player" dans l'overlay F1.
    ///
    /// Ce script est purement optionnel : le supprimer n'impacte aucun systeme.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerDebugCommands : MonoBehaviour
    {
        private const string InfoId = "Player";

        [SerializeField] private bool registerCommands = true;
        [SerializeField] private bool showOverlayInfo = true;

        private PlayerCharacter _player;
        private readonly StringBuilder _builder = new StringBuilder(256);

        private void Awake()
        {
            _player = GetComponent<PlayerCharacter>();
        }

        private void OnEnable()
        {
            if (registerCommands)
            {
                DebugManager.Register(Key.F7, "Tuer le joueur", KillPlayer);
                DebugManager.Register(Key.F6, "Mode invincible on/off", ToggleGodMode);
            }

            if (showOverlayInfo)
            {
                DebugManager.RegisterInfo(InfoId, BuildInfo);
            }
        }

        private void OnDisable()
        {
            if (registerCommands)
            {
                DebugManager.Unregister(Key.F7);
                DebugManager.Unregister(Key.F6);
            }

            if (showOverlayInfo)
            {
                DebugManager.UnregisterInfo(InfoId);
            }
        }

        private void KillPlayer()
        {
            if (_player == null || _player.Health == null)
            {
                return;
            }

            _player.Health.GodMode = false;
            _player.Health.Kill("Debug");
        }

        private void ToggleGodMode()
        {
            if (_player == null || _player.Health == null)
            {
                return;
            }

            _player.Health.GodMode = !_player.Health.GodMode;
            Debug.Log("[Debug] Mode invincible : " + (_player.Health.GodMode ? "ON" : "OFF"));
        }

        private string BuildInfo()
        {
            if (_player == null)
            {
                return null;
            }

            _builder.Length = 0;

            PlayerMotor motor = _player.Motor;
            PlayerHealth health = _player.Health;
            PlayerStamina stamina = _player.Stamina;

            if (motor != null)
            {
                _builder.Append("  Etat     : ").Append(motor.MovementState);
                _builder.Append(motor.IsGrounded ? "" : "  (en l'air)").Append('\n');
                _builder.Append("  Vitesse  : ").Append(motor.CurrentSpeed.ToString("F2")).Append(" m/s");
                _builder.Append("   Bruit : ").Append(motor.NoiseLevel.ToString("F2")).Append('\n');
            }

            if (stamina != null)
            {
                _builder.Append("  Stamina  : ").Append(Mathf.RoundToInt(stamina.Current)).Append(" / ").Append(Mathf.RoundToInt(stamina.Max));

                if (stamina.IsExhausted)
                {
                    _builder.Append("  [EPUISE]");
                }

                _builder.Append('\n');
            }

            if (health != null)
            {
                _builder.Append("  Vie      : ").Append(Mathf.RoundToInt(health.Current)).Append(" / ").Append(Mathf.RoundToInt(health.Max));

                if (health.GodMode)
                {
                    _builder.Append("  [GOD]");
                }

                if (!health.IsAlive)
                {
                    _builder.Append("  [MORT]");
                }

                _builder.Append('\n');
            }

            Vector3 position = _player.FeetPosition;
            _builder.Append("  Position : ")
                .Append(position.x.ToString("F1")).Append(" / ")
                .Append(position.y.ToString("F1")).Append(" / ")
                .Append(position.z.ToString("F1"));

            return _builder.ToString();
        }
    }
}

