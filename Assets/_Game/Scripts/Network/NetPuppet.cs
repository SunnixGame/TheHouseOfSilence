using HouseOfSilence.Demon;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Network
{
    /// <summary>
    /// Personnage joue par un autre joueur : suit en douceur la position, l'orientation,
    /// le regard (lampe torche) et l'etat (lampe allumee, pleurs du demon) recus du reseau.
    /// Ajoute et retire par NetworkGameManager.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetPuppet : MonoBehaviour
    {
        private const float SnapDistance = 5f;
        private const float Sharpness = 14f;

        private PlayerCharacter _survivor;
        private Flashlight _flashlight;
        private DemonCry _cry;

        private bool _has;
        private Vector3 _position;
        private float _yaw;
        private float _pitch;
        private bool _light;
        private bool _crying;

        private void Awake()
        {
            _survivor = GetComponent<PlayerCharacter>();
            _flashlight = GetComponent<Flashlight>();
            _cry = GetComponent<DemonCry>();
        }

        /// <summary>Nouvel etat recu. 'flags' : bit 0 = lampe allumee, bit 1 = demon en pleurs.</summary>
        public void Receive(Vector3 position, float yaw, float pitch, byte flags)
        {
            bool first = !_has;
            _has = true;
            _position = position;
            _yaw = yaw;
            _pitch = pitch;
            _light = (flags & 1) != 0;
            _crying = (flags & 2) != 0;

            if (first) Snap(position, yaw);
        }

        /// <summary>Place immediatement (apparition, teleportation).</summary>
        public void Snap(Vector3 position, float yaw)
        {
            _position = position;
            _yaw = yaw;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        private void Update()
        {
            if (!_has) return;

            float t = 1f - Mathf.Exp(-Sharpness * Time.deltaTime);

            if ((transform.position - _position).sqrMagnitude > SnapDistance * SnapDistance)
            {
                transform.position = _position;
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, _position, t);
            }

            float yaw = Mathf.LerpAngle(transform.eulerAngles.y, _yaw, t);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (_survivor != null && _survivor.CameraPivot != null && _survivor.CameraPivot != _survivor.transform)
            {
                Quaternion pitch = Quaternion.Euler(_pitch, 0f, 0f);
                _survivor.CameraPivot.localRotation = Quaternion.Slerp(_survivor.CameraPivot.localRotation, pitch, t);
            }

            if (_flashlight != null && _flashlight.IsOn != _light) _flashlight.SetOn(_light);
            if (_cry != null && _cry.IsCrying != _crying) _cry.SetCrying(_crying);
        }
    }
}
