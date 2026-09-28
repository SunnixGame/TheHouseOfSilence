using System.Collections.Generic;
using HouseOfSilence.Demon;
using HouseOfSilence.Player;
using UnityEngine;

namespace HouseOfSilence.Level
{
    /// <summary>
    /// Apparition aleatoire en debut de partie : chaque survivant et chaque demon est
    /// pose dans une clairiere tiree au hasard (ForestLocations), a un point libre
    /// (sol pas trop pentu, rien autour ni au-dessus : ni mur, ni toit, ni rocher).
    ///
    /// Deux personnages ne partagent jamais la meme clairiere, et le demon apparait
    /// a au moins 'minDemonDistance' metres des survivants (distance reduite petit a
    /// petit si la carte ne le permet pas). Les objets de 'followSurvivor' (les PNJ du
    /// depart) suivent le premier survivant, dans la meme disposition autour de lui.
    /// </summary>
    [DefaultExecutionOrder(-100)] // avant les Start() qui lisent la position du joueur
    [DisallowMultipleComponent]
    public class RandomSpawner : MonoBehaviour
    {
        [Header("Zones")]
        [Tooltip("Assets/_Game/Settings/ForestLocations.asset : une clairiere = une zone d'apparition.")]
        [SerializeField] private ForestLocations locations;
        [Tooltip("Clairieres interdites (clearingId, ex. Clairiere_04).")]
        [SerializeField] private List<string> excludedClearings = new List<string>();
        [Tooltip("Part du rayon de la clairiere utilisable (loin de la lisiere).")]
        [SerializeField, Range(0.1f, 1f)] private float radiusUsage = 0.6f;

        [Header("Qui")]
        [SerializeField] private bool spawnSurvivors = true;
        [SerializeField] private bool spawnDemons = true;

        [Header("Regles")]
        [SerializeField, Min(0f)] private float minDemonDistance = 300f;
        [SerializeField, Range(5f, 60f)] private float maxSlope = 30f;
        [SerializeField, Min(1)] private int attemptsPerCharacter = 40;

        [Header("Suivi")]
        [Tooltip("Deplaces avec le premier survivant (PNJ du depart).")]
        [SerializeField] private List<Transform> followSurvivor = new List<Transform>();

        private readonly List<ForestLocations.Location> _usedClearings = new List<ForestLocations.Location>();
        private readonly List<Vector3> _survivorSpawns = new List<Vector3>();
        private readonly List<Transform> _spawned = new List<Transform>();

        private void Start()
        {
            if (locations == null || locations.All.Count == 0)
            {
                Debug.LogWarning("[RandomSpawner] Aucune clairiere (ForestLocations) : apparition inchangee.", this);
                return;
            }

            PlayerCharacter[] survivors = FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.InstanceID);
            DemonController[] demons = FindObjectsByType<DemonController>(FindObjectsSortMode.InstanceID);

            foreach (PlayerCharacter s in survivors) _spawned.Add(s.transform);
            foreach (DemonController d in demons) _spawned.Add(d.transform);

            if (spawnSurvivors)
            {
                for (int i = 0; i < survivors.Length; i++)
                {
                    Vector3 point;
                    float yaw;
                    if (!FindSpawn(0f, out point, out yaw)) continue;

                    Vector3 oldPosition = survivors[i].transform.position;
                    float oldYaw = survivors[i].transform.eulerAngles.y;
                    survivors[i].TeleportTo(point, yaw);
                    _survivorSpawns.Add(point);

                    if (i == 0) MoveFollowers(oldPosition, oldYaw, point, yaw);
                }
            }

            if (spawnDemons)
            {
                foreach (DemonController demon in demons)
                {
                    Vector3 point;
                    float yaw;

                    // Loin des survivants si possible, sinon on se rapproche par paliers (100 %, 75 %... 0 %).
                    for (int step = 0; step <= 4; step++)
                    {
                        float distance = minDemonDistance * (1f - step / 4f);
                        if (!FindSpawn(distance, out point, out yaw)) continue;

                        demon.TeleportTo(point, Quaternion.Euler(0f, yaw, 0f));
                        break;
                    }
                }
            }
        }

        /// <summary>Point libre dans une clairiere inutilisee, a 'minDistance' m au moins des survivants.</summary>
        private bool FindSpawn(float minDistance, out Vector3 point, out float yaw)
        {
            IReadOnlyList<ForestLocations.Location> all = locations.All;

            for (int attempt = 0; attempt < attemptsPerCharacter; attempt++)
            {
                ForestLocations.Location clearing = all[Random.Range(0, all.Count)];

                if (excludedClearings.Contains(clearing.clearingId) || _usedClearings.Contains(clearing)) continue;
                if (!FarFromSurvivors(clearing.position, minDistance)) continue;

                // Plusieurs essais dans la meme clairiere avant d'en changer.
                for (int inner = 0; inner < 6; inner++)
                {
                    Vector2 offset = Random.insideUnitCircle * clearing.radius * radiusUsage;
                    Vector3 candidate = new Vector3(clearing.position.x + offset.x, 0f, clearing.position.z + offset.y);

                    if (!GroundAt(candidate, out candidate) || !IsFree(candidate)) continue;

                    _usedClearings.Add(clearing);
                    point = candidate;
                    // Regard vers le centre de la clairiere (on voit l'espace degage).
                    Vector3 toCenter = clearing.position - candidate;
                    yaw = toCenter.sqrMagnitude > 1f ? Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg : Random.Range(0f, 360f);
                    return true;
                }
            }

            point = Vector3.zero;
            yaw = 0f;
            return false;
        }

        private bool FarFromSurvivors(Vector3 position, float minDistance)
        {
            foreach (Vector3 s in _survivorSpawns)
            {
                if (Vector2.Distance(new Vector2(s.x, s.z), new Vector2(position.x, position.z)) < minDistance) return false;
            }

            return true;
        }

        /// <summary>Hauteur du terrain sous le point, et pente acceptable.</summary>
        private bool GroundAt(Vector3 xz, out Vector3 ground)
        {
            ground = xz;

            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                TerrainData data = terrain.terrainData;
                Vector3 local = xz - terrain.transform.position;

                if (local.x < 0f || local.z < 0f || local.x > data.size.x || local.z > data.size.z) continue;

                float u = local.x / data.size.x;
                float v = local.z / data.size.z;
                if (data.GetSteepness(u, v) > maxSlope) return false;

                ground.y = terrain.SampleHeight(xz) + terrain.transform.position.y;
                return true;
            }

            return false;
        }

        /// <summary>Rien dans le volume du personnage ni au-dessus de sa tete (murs, toits, rochers).</summary>
        private bool IsFree(Vector3 feet)
        {
            Collider[] hits = Physics.OverlapCapsule(feet + Vector3.up * 0.5f, feet + Vector3.up * 1.7f, 0.45f, ~0, QueryTriggerInteraction.Ignore);

            foreach (Collider c in hits)
            {
                if (c is TerrainCollider || IsSpawnedCharacter(c.transform)) continue;
                return false;
            }

            RaycastHit roof;
            if (Physics.Raycast(feet + Vector3.up * 1.8f, Vector3.up, out roof, 40f, ~0, QueryTriggerInteraction.Ignore)
                && !IsSpawnedCharacter(roof.transform))
            {
                return false;
            }

            return true;
        }

        private bool IsSpawnedCharacter(Transform t)
        {
            foreach (Transform s in _spawned)
            {
                if (t.IsChildOf(s)) return true;
            }

            return false;
        }

        /// <summary>Deplace les objets suivis en gardant leur place autour du survivant.</summary>
        private void MoveFollowers(Vector3 oldPosition, float oldYaw, Vector3 newPosition, float newYaw)
        {
            Quaternion turn = Quaternion.Euler(0f, newYaw - oldYaw, 0f);

            foreach (Transform follower in followSurvivor)
            {
                if (follower == null) continue;

                Vector3 offset = follower.position - oldPosition;
                follower.SetPositionAndRotation(newPosition + turn * offset, turn * follower.rotation);
            }
        }
    }
}
