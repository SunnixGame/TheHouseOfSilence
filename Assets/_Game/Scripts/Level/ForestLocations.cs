using System;
using System.Collections.Generic;
using UnityEngine;

namespace HouseOfSilence.Level
{
    /// <summary>
    /// Noms des lieux de la foret (une entree par clairiere), affiches sur la carte 3D.
    ///
    /// C'est un asset a part (Assets/_Game/Settings/ForestLocations.asset) : renomme les
    /// lieux ici dans l'Inspector. Build Forest Landscape 2k n'ecrase jamais un nom : il
    /// ajoute seulement les clairieres manquantes et met a jour position / rayon.
    /// </summary>
    [CreateAssetMenu(menuName = "House of Silence/Forest Locations", fileName = "ForestLocations")]
    public class ForestLocations : ScriptableObject
    {
        [Serializable]
        public class Location
        {
            [Tooltip("Identifiant de la clairiere (ne pas modifier).")]
            public string clearingId;

            [Tooltip("Nom affiche sur la carte. A modifier librement.")]
            public string displayName;

            [Tooltip("Decoche pour cacher ce lieu sur la carte.")]
            public bool showOnMap = true;

            [Header("Auto (mis a jour par le builder)")]
            public Vector3 position;
            public float radius;
        }

        [SerializeField] private List<Location> locations = new List<Location>();

        public IReadOnlyList<Location> All { get { return locations; } }

        public Location Find(string clearingId)
        {
            for (int i = 0; i < locations.Count; i++)
            {
                if (locations[i].clearingId == clearingId)
                {
                    return locations[i];
                }
            }

            return null;
        }

        /// <summary>Lieu dont la clairiere contient ce point (x, z monde), ou null.</summary>
        public Location At(Vector3 world)
        {
            Location best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < locations.Count; i++)
            {
                Location l = locations[i];
                float d = Vector2.Distance(new Vector2(world.x, world.z), new Vector2(l.position.x, l.position.z));

                if (d <= l.radius && d < bestDistance)
                {
                    best = l;
                    bestDistance = d;
                }
            }

            return best;
        }

#if UNITY_EDITOR
        /// <summary>Ajoute la clairiere si elle manque, met a jour sa position (garde le nom).</summary>
        public void EditorEnsure(string clearingId, string defaultName, Vector3 position, float radius)
        {
            Location l = Find(clearingId);

            if (l == null)
            {
                l = new Location { clearingId = clearingId, displayName = defaultName };
                locations.Add(l);
            }

            l.position = position;
            l.radius = radius;
        }
#endif
    }
}
