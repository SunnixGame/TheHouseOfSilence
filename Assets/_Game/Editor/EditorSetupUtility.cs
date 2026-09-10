using UnityEditor;
using UnityEngine;

namespace HouseOfSilence.EditorTools
{
    /// <summary>
    /// Petites aides partagees par tous les outils du menu Tools > House of Silence.
    /// </summary>
    public static class EditorSetupUtility
    {
        /// <summary>
        /// Assigne un champ prive marque [SerializeField] sur un composant.
        /// Permet aux outils de cabler les references sans exposer les champs en public.
        /// </summary>
        public static bool SetObjectField(Object target, string fieldName, Object value)
        {
            if (target == null)
            {
                return false;
            }

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning("[Setup] Champ '" + fieldName + "' introuvable sur " + target.GetType().Name + ".");
                return false;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>Ajoute un composant seulement s'il n'est pas deja present.</summary>
        public static T EnsureComponent<T>(GameObject target) where T : Component
        {
            if (target == null)
            {
                return null;
            }

            T component = target.GetComponent<T>();

            if (component == null)
            {
                component = Undo.AddComponent<T>(target);
            }

            return component;
        }

        /// <summary>Cree un cube de test avec un nom, une position et une taille donnes.</summary>
        public static GameObject CreateBox(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;

            if (parent != null)
            {
                box.transform.SetParent(parent, false);
            }

            box.transform.localPosition = localPosition;
            box.transform.localScale = localScale;
            return box;
        }
    }
}
