using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace HouseOfSilence.Level
{
    /// <summary>
    /// Metadonnees d'un objet du manoir, copiees depuis les proprietes personnalisees
    /// "hos_*" du FBX exporte par Blender (manor_builder.py) : type d'element
    /// (mur, sol, porte, escalier...), niveau, piece, et pour les portes le sens
    /// d'ouverture et la piece desservie.
    ///
    /// Posees par ManorAssetPostprocessor a l'import ; lues par ManorSceneBuilder
    /// pour poser colliders, flags statiques, scripts de porte et volumes de pieces.
    /// Restent sur les objets en jeu : utiles a l'IA (type de piece) et au debug.
    /// </summary>
    public class ManorMeta : MonoBehaviour
    {
        [SerializeField] private string[] keys = new string[0];
        [SerializeField] private string[] values = new string[0];

        public string Type { get { return Get("hos_type"); } }
        public string Level { get { return Get("hos_level"); } }
        public string Room { get { return Get("hos_room"); } }
        public string DoorKind { get { return Get("hos_door"); } }

        public void Set(IList<string> newKeys, IList<string> newValues)
        {
            keys = new List<string>(newKeys).ToArray();
            values = new List<string>(newValues).ToArray();
        }

        public bool Has(string key)
        {
            return System.Array.IndexOf(keys, key) >= 0;
        }

        public string Get(string key, string fallback = "")
        {
            int i = System.Array.IndexOf(keys, key);
            return i >= 0 ? values[i] : fallback;
        }

        public float GetFloat(string key, float fallback = 0f)
        {
            float v;
            return float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public int GetInt(string key, int fallback = 0)
        {
            return Mathf.RoundToInt(GetFloat(key, fallback));
        }

        public IEnumerable<KeyValuePair<string, string>> All()
        {
            for (int i = 0; i < keys.Length; i++)
            {
                yield return new KeyValuePair<string, string>(keys[i], values[i]);
            }
        }
    }
}
