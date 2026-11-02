using UnityEngine;

/// <summary>
/// Marca una SECCIÓN del tutorial colocado en la escena (Tools/Curso: Colocar el tutorial en la escena). Al jugar,
/// CourseBuilder las esconde y las hace subir del suelo al acercarse. La sección (0, 1, 2…) de los suelos y obstáculos
/// se calcula por su posición Z, así que se pueden mover a mano (en múltiplos de 28,8 m).
/// </summary>
[DisallowMultipleComponent]
public class CourseSection : MonoBehaviour
{
    public enum Kind { Floor, Obstacle, End }

    public Kind kind;
    [Tooltip("0 = carril del tutorial (x = 0), 1 = carril del recorrido de estudio (x = 250; al jugar se mueve al centro).")]
    public int lane;
    [Tooltip("Índice del obstáculo en la lista de CourseBuilder (−1 = tramo de suelo).")]
    public int obstacle = -1;
    [Tooltip("Versión en espejo (dash a la derecha).")]
    public bool mirrored;
    [Tooltip("Tramo de suelo con checkpoint al principio.")]
    public bool checkpoint;
    [Tooltip("Tramo de suelo donde termina el recorrido.")]
    public bool end;
    [Tooltip("Sala final: sección en la que empieza (su posición Z no es múltiplo de la sección).")]
    public int seg;
    [Tooltip("Acciones de esta sección (tecla y beat desde su principio): salen como pistas en el monitor del corazón. " +
             "Las pone la herramienta copiándolas del obstáculo; vacío = las del obstáculo en CourseBuilder.")]
    public CourseAction[] actions = new CourseAction[0];

    private void OnDrawGizmos()
    {
        if (kind != Kind.Floor || (!checkpoint && !end)) return;
        Gizmos.color = end ? new Color(1f, 0.3f, 0.3f, 0.6f) : new Color(0.2f, 1f, 0.4f, 0.6f);
        Vector3 p = transform.position - Vector3.forward * 14.4f;   // (principio de la sección a 100 BPM y 12 m/s)
        Gizmos.DrawCube(p + Vector3.up * 1.5f, new Vector3(18f, 3f, 0.2f));
    }
}
