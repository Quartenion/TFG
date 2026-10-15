using UnityEngine;
using UnityEditor;

/// <summary>
/// Inspector de StairFallingObstacle con un botón "Mover X aleatorio" que coloca el bloque
/// en una X (mundo) al azar dentro del rango [Random X Min, Random X Max].
/// Funciona con varios objetos seleccionados a la vez y soporta Undo (Ctrl+Z).
/// </summary>
[CustomEditor(typeof(StairFallingObstacle))]
[CanEditMultipleObjects]
public class StairFallingObstacleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);

        int count = targets.Length;
        if (GUILayout.Button($"🎲  Mover X aleatorio ({count} objeto{(count != 1 ? "s" : "")})", GUILayout.Height(30)))
        {
            foreach (Object t in targets)
            {
                var obs = t as StairFallingObstacle;
                if (obs == null) continue;

                Undo.RecordObject(obs.transform, "Mover X aleatorio");
                obs.RandomizeXPosition();
                EditorUtility.SetDirty(obs.transform);

                if (!Application.isPlaying)
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(obs.gameObject.scene);
            }
        }

        EditorGUILayout.HelpBox("Coloca el bloque en una X aleatoria dentro del rango de arriba.", MessageType.None);
    }
}
