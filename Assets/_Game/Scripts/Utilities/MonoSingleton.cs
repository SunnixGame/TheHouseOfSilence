using UnityEngine;

namespace HouseOfSilence.Utilities
{
    /// <summary>
    /// Etat statique partage par tous les singletons (non generique pour que
    /// RuntimeInitializeOnLoadMethod fonctionne, meme si le Domain Reload est desactive).
    /// </summary>
    internal static class SingletonRuntime
    {
        internal static bool IsQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsQuitting = false;
        }
    }

    /// <summary>
    /// Classe de base pour les managers uniques (GameManager, SceneLoader, ...).
    /// - Trouve automatiquement une instance deja presente dans la scene.
    /// - En cree une a la volee si aucune n'existe (aucun setup obligatoire).
    /// - Detruit les doublons.
    /// - Optionnellement persistante entre les scenes (DontDestroyOnLoad).
    /// </summary>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T s_Instance;

        /// <summary>Vrai si une instance existe deja (ne declenche PAS la creation automatique).</summary>
        public static bool HasInstance => s_Instance != null;

        /// <summary>
        /// Accesseur principal. Cree l'objet si besoin (sauf pendant la fermeture du jeu).
        /// Peut renvoyer null : toujours tester le resultat.
        /// </summary>
        public static T Instance
        {
            get
            {
                if (SingletonRuntime.IsQuitting)
                {
                    return null;
                }

                if (s_Instance != null)
                {
                    return s_Instance;
                }

                s_Instance = FindAnyObjectByType<T>(FindObjectsInactive.Exclude);

                if (s_Instance == null && Application.isPlaying)
                {
                    GameObject go = new GameObject("[" + typeof(T).Name + "]");
                    s_Instance = go.AddComponent<T>();
                }

                return s_Instance;
            }
        }

        /// <summary>Redefinir a false pour un singleton lie a une seule scene.</summary>
        protected virtual bool IsPersistent => true;

        protected virtual void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Debug.LogWarning("[" + typeof(T).Name + "] Doublon detecte sur '" + name + "'. Destruction de l'objet en trop.", this);
                Destroy(gameObject);
                return;
            }

            s_Instance = (T)this;

            if (IsPersistent)
            {
                if (transform.parent != null)
                {
                    // DontDestroyOnLoad n'accepte que des objets racine.
                    transform.SetParent(null, true);
                }

                DontDestroyOnLoad(gameObject);
            }

            OnSingletonAwake();
        }

        /// <summary>Appele une seule fois, uniquement sur l'instance valide.</summary>
        protected virtual void OnSingletonAwake() { }

        protected virtual void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        protected virtual void OnApplicationQuit()
        {
            SingletonRuntime.IsQuitting = true;
        }
    }
}
