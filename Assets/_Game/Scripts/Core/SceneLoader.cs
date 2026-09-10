using System;
using System.Collections;
using HouseOfSilence.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HouseOfSilence.Core
{
    /// <summary>
    /// Chargement asynchrone des scenes, avec progression et duree minimale
    /// (evite les flashs d'ecran de chargement).
    ///
    /// IMPORTANT : toute scene chargee par son nom doit etre presente dans
    /// File > Build Profiles > Scene List (anciennement Build Settings).
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneLoader : MonoSingleton<SceneLoader>
    {
        [Header("Chargement")]
        [Tooltip("Duree minimale d'un chargement, en secondes (temps non affecte par le timeScale).")]
        [SerializeField, Min(0f)] private float minimumLoadTime = 0.4f;

        [Tooltip("Log detaille dans la console.")]
        [SerializeField] private bool verboseLogs = true;

        private Coroutine _loadRoutine;

        /// <summary>Vrai pendant toute la duree d'un chargement.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>Progression 0 -> 1 du chargement en cours.</summary>
        public float Progress { get; private set; }

        /// <summary>Nom de la scene en cours de chargement (vide si aucun).</summary>
        public string LoadingSceneName { get; private set; } = string.Empty;

        /// <summary>Nom de la scene active.</summary>
        public static string ActiveSceneName
        {
            get { return SceneManager.GetActiveScene().name; }
        }

        /// <summary>
        /// Charge une scene en mode Single (remplace la scene courante).
        /// </summary>
        /// <param name="sceneName">Nom exact de la scene (sans extension).</param>
        /// <param name="onComplete">Callback appele une fois la scene active.</param>
        public void LoadScene(string sceneName, Action onComplete = null)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[SceneLoader] Nom de scene vide.", this);
                if (onComplete != null) { onComplete.Invoke(); }
                return;
            }

            if (IsLoading)
            {
                Debug.LogWarning("[SceneLoader] Chargement de " + sceneName + " ignore : " + LoadingSceneName + " est deja en cours.", this);
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError("[SceneLoader] Scene introuvable : " + sceneName + ". Ajoute-la dans File > Build Profiles > Scene List.", this);
                EventBus.Publish(new SceneLoadCompletedEvent(sceneName, false));
                if (onComplete != null) { onComplete.Invoke(); }
                return;
            }

            _loadRoutine = StartCoroutine(LoadRoutine(sceneName, onComplete));
        }

        /// <summary>Recharge la scene active.</summary>
        public void ReloadActiveScene(Action onComplete = null)
        {
            LoadScene(ActiveSceneName, onComplete);
        }

        /// <summary>
        /// Charge une scene en additif (pieces de la maison, zones...).
        /// </summary>
        public void LoadSceneAdditive(string sceneName, Action onComplete = null)
        {
            if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError("[SceneLoader] Scene additive introuvable : " + sceneName, this);
                if (onComplete != null) { onComplete.Invoke(); }
                return;
            }

            if (SceneManager.GetSceneByName(sceneName).isLoaded)
            {
                if (onComplete != null) { onComplete.Invoke(); }
                return;
            }

            StartCoroutine(LoadAdditiveRoutine(sceneName, onComplete));
        }

        /// <summary>Decharge une scene chargee en additif.</summary>
        public void UnloadSceneAdditive(string sceneName, Action onComplete = null)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);

            if (!scene.isLoaded)
            {
                if (onComplete != null) { onComplete.Invoke(); }
                return;
            }

            StartCoroutine(UnloadAdditiveRoutine(sceneName, onComplete));
        }

        private IEnumerator LoadRoutine(string sceneName, Action onComplete)
        {
            IsLoading = true;
            Progress = 0f;
            LoadingSceneName = sceneName;

            // Un chargement ne doit jamais rester bloque par une pause.
            Time.timeScale = 1f;
            AudioListener.pause = false;

            if (verboseLogs)
            {
                Debug.Log("[SceneLoader] Chargement de " + sceneName + "...");
            }

            EventBus.Publish(new SceneLoadStartedEvent(sceneName));

            // Une frame pour laisser l'ecran de chargement s'afficher.
            yield return null;

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            if (operation == null)
            {
                Debug.LogError("[SceneLoader] LoadSceneAsync a renvoye null pour " + sceneName, this);
                IsLoading = false;
                LoadingSceneName = string.Empty;
                EventBus.Publish(new SceneLoadCompletedEvent(sceneName, false));
                if (onComplete != null) { onComplete.Invoke(); }
                yield break;
            }

            // Empeche l'activation immediate pour respecter la duree minimale.
            operation.allowSceneActivation = false;

            float elapsed = 0f;

            // Unity plafonne progress a 0.9 tant que allowSceneActivation est false.
            while (operation.progress < 0.9f || elapsed < minimumLoadTime)
            {
                elapsed += Time.unscaledDeltaTime;

                float loadPart = Mathf.Clamp01(operation.progress / 0.9f);
                float timePart = minimumLoadTime > 0f ? Mathf.Clamp01(elapsed / minimumLoadTime) : 1f;

                Progress = Mathf.Min(loadPart, timePart);
                EventBus.Publish(new SceneLoadProgressEvent(sceneName, Progress));

                yield return null;
            }

            Progress = 1f;
            EventBus.Publish(new SceneLoadProgressEvent(sceneName, 1f));

            operation.allowSceneActivation = true;

            while (!operation.isDone)
            {
                yield return null;
            }

            // Une frame de plus : tous les Awake/Start de la nouvelle scene ont tourne.
            yield return null;

            IsLoading = false;
            LoadingSceneName = string.Empty;
            _loadRoutine = null;

            if (verboseLogs)
            {
                Debug.Log("[SceneLoader] Scene chargee : " + sceneName);
            }

            EventBus.Publish(new SceneLoadCompletedEvent(sceneName, true));

            if (onComplete != null)
            {
                onComplete.Invoke();
            }
        }

        private IEnumerator LoadAdditiveRoutine(string sceneName, Action onComplete)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            if (operation != null)
            {
                while (!operation.isDone)
                {
                    yield return null;
                }
            }

            if (onComplete != null)
            {
                onComplete.Invoke();
            }
        }

        private IEnumerator UnloadAdditiveRoutine(string sceneName, Action onComplete)
        {
            AsyncOperation operation = SceneManager.UnloadSceneAsync(sceneName);

            if (operation != null)
            {
                while (!operation.isDone)
                {
                    yield return null;
                }
            }

            if (onComplete != null)
            {
                onComplete.Invoke();
            }
        }

        protected override void OnDestroy()
        {
            if (_loadRoutine != null)
            {
                StopCoroutine(_loadRoutine);
                _loadRoutine = null;
            }

            base.OnDestroy();
        }
    }
}
