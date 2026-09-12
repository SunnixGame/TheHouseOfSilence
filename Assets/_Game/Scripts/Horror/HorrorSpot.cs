using System.Collections.Generic;
using UnityEngine;

namespace HouseOfSilence.Horror
{
    /// <summary>
    /// Emplacement prevu par le level designer pour un evenement :
    /// le telephone, la radio, le bout du couloir ou une silhouette
    /// doit apparaitre, la fenetre qu'on entend cogner...
    ///
    /// L'etiquette (Spot Tag) permet aux evenements de choisir leur type
    /// d'emplacement : "phone", "radio", "silhouette", "window"... Vide = generique.
    /// </summary>
    [DisallowMultipleComponent]
    public class HorrorSpot : MonoBehaviour
    {
        private static readonly List<HorrorSpot> s_All = new List<HorrorSpot>(32);

        [Tooltip("Etiquette libre. Un evenement avec Spot Tag = 'phone' ne choisira que les spots 'phone'.")]
        [SerializeField] private string spotTag = "";

        [Tooltip("L'objet place ici regarde-t-il vers le joueur ? (silhouettes)")]
        [SerializeField] private bool facePlayer = true;

        public string SpotTag { get { return spotTag; } }
        public bool FacePlayer { get { return facePlayer; } }

        public static IReadOnlyList<HorrorSpot> All { get { return s_All; } }

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

        /// <summary>Vrai si ce spot correspond a l'etiquette demandee (vide = tout accepte).</summary>
        public bool Matches(string requestedTag)
        {
            if (string.IsNullOrEmpty(requestedTag))
            {
                return true;
            }

            return string.Equals(spotTag, requestedTag, System.StringComparison.OrdinalIgnoreCase);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.9f, 0.4f, 0.9f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.25f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.6f);
        }
#endif
    }
}
