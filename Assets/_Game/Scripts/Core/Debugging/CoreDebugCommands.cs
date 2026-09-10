using UnityEngine;
using UnityEngine.InputSystem;

namespace HouseOfSilence.Core.Debugging
{
    /// <summary>
    /// Enregistre les commandes de debug du Core aupres du DebugManager.
    ///
    /// Les touches F2 a F8 sont reservees aux systemes de gameplay des phases
    /// suivantes (spawn monstre, objectifs, lumieres, peur...) : ce script
    /// n'utilise donc que F9, F10 et F11.
    /// </summary>
    [DisallowMultipleComponent]
    public class CoreDebugCommands : MonoBehaviour
    {
        [SerializeField] private bool registerCommands = true;

        private void OnEnable()
        {
            if (!registerCommands)
            {
                return;
            }

            DebugManager.Register(Key.F9, "Forcer VICTOIRE", ForceVictory);
            DebugManager.Register(Key.F10, "Forcer GAME OVER", ForceDefeat);
            DebugManager.Register(Key.F11, "Recharger la scene", ReloadScene);
        }

        private void OnDisable()
        {
            DebugManager.Unregister(Key.F9);
            DebugManager.Unregister(Key.F10);
            DebugManager.Unregister(Key.F11);
        }

        private static void ForceVictory()
        {
            GameManager game = GameManager.Instance;

            if (game == null)
            {
                return;
            }

            game.EndGame(GameResult.Victory);
        }

        private static void ForceDefeat()
        {
            GameManager game = GameManager.Instance;

            if (game == null)
            {
                return;
            }

            game.EndGame(GameResult.Defeat);
        }

        private static void ReloadScene()
        {
            GameManager game = GameManager.Instance;

            if (game == null)
            {
                return;
            }

            game.RestartSession();
        }
    }
}
