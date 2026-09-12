using System.Collections.Generic;
using UnityEngine;

namespace HouseOfSilence.Core
{
    /// <summary>
    /// Volume d'une piece de la maison. Sert a savoir dans quelle piece se
    /// trouve un joueur ou la creature (evenements cibles, IA, occlusion,
    /// musique par zone).
    ///
    /// A poser sur un GameObject avec un BoxCollider en Is Trigger.
    /// Genere automatiquement par le constructeur de niveau.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class RoomVolume : MonoBehaviour
    {
        private static readonly List<RoomVolume> s_All = new List<RoomVolume>(64);

        [SerializeField] private string roomName = "Piece";

        [Tooltip("-1 = sous-sol, 0 = rez-de-chaussee, 1 = premier etage...")]
        [SerializeField] private int floorIndex = 0;

        private BoxCollider _box;

        public string RoomName { get { return roomName; } }
        public int FloorIndex { get { return floorIndex; } }

        public static IReadOnlyList<RoomVolume> All { get { return s_All; } }

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;
        }

        private void OnEnable()
        {
            if (!s_All.Contains(this))
            {
                s_All.Add(this);
            }
        }

        private void OnDisable()
        {
            s_All.Remove(this);
        }

        /// <summary>Vrai si le point est dans la piece.</summary>
        public bool Contains(Vector3 worldPosition)
        {
            if (_box == null)
            {
                _box = GetComponent<BoxCollider>();
            }

            Vector3 local = transform.InverseTransformPoint(worldPosition) - _box.center;
            Vector3 half = _box.size * 0.5f;

            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>Piece contenant le point, ou null (exterieur).</summary>
        public static RoomVolume GetRoomAt(Vector3 worldPosition)
        {
            for (int i = 0; i < s_All.Count; i++)
            {
                if (s_All[i] != null && s_All[i].Contains(worldPosition))
                {
                    return s_All[i];
                }
            }

            return null;
        }

        /// <summary>Configure la piece (utilise par les outils d'edition).</summary>
        public void Configure(string name, int floor)
        {
            roomName = name;
            floorIndex = floor;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            BoxCollider box = GetComponent<BoxCollider>();

            if (box == null)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.15f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
#endif
    }
}
