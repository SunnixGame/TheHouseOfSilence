using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Horror
{
    /// <summary>
    /// Zone dans laquelle la peur redescend vite : piece eclairee et fermee,
    /// point de depart, cachette scriptee.
    ///
    /// A poser sur un GameObject avec un collider en Is Trigger.
    /// Menu : Tools > House of Silence > Create Safe Zone.
    /// </summary>
    [DisallowMultipleComponent]
    public class SafeZone : MonoBehaviour
    {
        [Tooltip("Une zone desactivee ne rassure plus (la creature y est entree, la lumiere a saute...).")]
        [SerializeField] private bool zoneActive = true;

        /// <summary>Active ou coupe la zone a chaud. Les joueurs deja dedans sont mis a jour.</summary>
        public bool ZoneActive
        {
            get { return zoneActive; }
            set
            {
                if (zoneActive == value)
                {
                    return;
                }

                zoneActive = value;

                // Les joueurs presents au moment du changement doivent etre resynchronises.
                PlayerCharacter[] inside = GetPlayersInside();

                for (int i = 0; i < inside.Length; i++)
                {
                    FearSystem fear = inside[i].GetComponent<FearSystem>();

                    if (fear == null)
                    {
                        continue;
                    }

                    if (zoneActive)
                    {
                        fear.EnterSafeZone();
                    }
                    else
                    {
                        fear.ExitSafeZone();
                    }
                }
            }
        }

        private readonly System.Collections.Generic.List<PlayerCharacter> _inside = new System.Collections.Generic.List<PlayerCharacter>(4);

        private void OnTriggerEnter(Collider other)
        {
            PlayerCharacter player = other != null ? other.GetComponentInParent<PlayerCharacter>() : null;

            if (player == null || _inside.Contains(player))
            {
                return;
            }

            _inside.Add(player);

            if (!zoneActive)
            {
                return;
            }

            FearSystem fear = player.GetComponent<FearSystem>();

            if (fear != null)
            {
                fear.EnterSafeZone();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerCharacter player = other != null ? other.GetComponentInParent<PlayerCharacter>() : null;

            if (player == null || !_inside.Remove(player))
            {
                return;
            }

            if (!zoneActive)
            {
                return;
            }

            FearSystem fear = player.GetComponent<FearSystem>();

            if (fear != null)
            {
                fear.ExitSafeZone();
            }
        }

        private void OnDisable()
        {
            // Si la zone disparait, on ne laisse personne "rassure" pour toujours.
            for (int i = 0; i < _inside.Count; i++)
            {
                if (_inside[i] == null || !zoneActive)
                {
                    continue;
                }

                FearSystem fear = _inside[i].GetComponent<FearSystem>();

                if (fear != null)
                {
                    fear.ExitSafeZone();
                }
            }

            _inside.Clear();
        }

        private PlayerCharacter[] GetPlayersInside()
        {
            return _inside.ToArray();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Collider collider = GetComponent<Collider>();

            if (collider == null)
            {
                return;
            }

            Gizmos.color = zoneActive ? new Color(0.5f, 0.8f, 1f, 0.2f) : new Color(1f, 0.4f, 0.4f, 0.15f);
            Gizmos.matrix = transform.localToWorldMatrix;

            BoxCollider box = collider as BoxCollider;

            if (box != null)
            {
                Gizmos.DrawCube(box.center, box.size);
            }
        }
#endif
    }
}
