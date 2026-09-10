using System;
using System.Collections.Generic;
using UnityEngine;

namespace HouseOfSilence.Core
{
    /// <summary>
    /// Bus d'evenements type, statique et sans allocation a la publication.
    /// Permet aux systemes (peur, IA, UI, audio...) de communiquer sans se referencer.
    ///
    /// Utilisation :
    ///     EventBus.Subscribe&lt;GameStateChangedEvent&gt;(OnStateChanged);   // OnEnable
    ///     EventBus.Unsubscribe&lt;GameStateChangedEvent&gt;(OnStateChanged); // OnDisable
    ///     EventBus.Publish(new GameStateChangedEvent(previous, current));
    ///
    /// Regle : TOUJOURS se desabonner (OnDisable / OnDestroy), sinon des objets
    /// detruits resteront references.
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> s_Handlers = new Dictionary<Type, Delegate>(32);

        /// <summary>Nombre de types d'evenements ayant au moins un abonne (debug).</summary>
        public static int SubscribedTypeCount => s_Handlers.Count;

        /// <summary>Remet le bus a zero au demarrage du Play Mode (utile si le Domain Reload est desactive).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Handlers.Clear();
        }

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null)
            {
                return;
            }

            Type type = typeof(T);

            if (s_Handlers.TryGetValue(type, out Delegate existing))
            {
                s_Handlers[type] = Delegate.Combine(existing, handler);
            }
            else
            {
                s_Handlers[type] = handler;
            }
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null)
            {
                return;
            }

            Type type = typeof(T);

            if (!s_Handlers.TryGetValue(type, out Delegate existing))
            {
                return;
            }

            Delegate result = Delegate.Remove(existing, handler);

            if (result == null)
            {
                s_Handlers.Remove(type);
            }
            else
            {
                s_Handlers[type] = result;
            }
        }

        /// <summary>
        /// Diffuse l'evenement a tous les abonnes. Une exception dans un abonne
        /// est loguee mais n'empeche pas les autres d'etre notifies.
        /// </summary>
        public static void Publish<T>(T evt) where T : struct
        {
            if (!s_Handlers.TryGetValue(typeof(T), out Delegate existing))
            {
                return;
            }

            if (!(existing is Action<T> callback))
            {
                return;
            }

            Delegate[] invocationList = callback.GetInvocationList();

            for (int i = 0; i < invocationList.Length; i++)
            {
                Action<T> target = invocationList[i] as Action<T>;

                if (target == null)
                {
                    continue;
                }

                try
                {
                    target.Invoke(evt);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        /// <summary>Nombre d'abonnes pour un type donne (debug / tests).</summary>
        public static int GetSubscriberCount<T>() where T : struct
        {
            if (!s_Handlers.TryGetValue(typeof(T), out Delegate existing) || existing == null)
            {
                return 0;
            }

            return existing.GetInvocationList().Length;
        }

        /// <summary>Supprime tous les abonnes d'un type (a n'utiliser que pour le debug / les tests).</summary>
        public static void ClearType<T>() where T : struct
        {
            s_Handlers.Remove(typeof(T));
        }

        /// <summary>Vide entierement le bus (a n'utiliser que pour le debug / les tests).</summary>
        public static void ClearAll()
        {
            s_Handlers.Clear();
        }
    }
}
