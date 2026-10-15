using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Herramienta de diseño de niveles. Dibuja en la escena una cuadrícula
/// de posiciones alineadas con el beat de la música, para que puedas
/// colocar obstáculos manualmente en las posiciones correctas.
///
/// La fórmula es automática:
///   cellSize = walkSpeed × (60 / BPM)
///
/// Al cambiar el BPM en BeatManager (distinto nivel/canción), la cuadrícula
/// se recalcula al instante sin tocar nada más.
///
/// SETUP:
///   1. Crea un GameObject vacío en la escena y añade este componente.
///   2. Coloca el GameObject en la posición donde empieza el jugador (beat 0).
///   3. Rota el GameObject para que su eje Z apunte hacia donde corre el jugador.
///   4. Asigna BeatManager y rellena walkSpeed con el valor del PlayerMovement.
///   5. Verás las marcas de beat en la escena. Coloca obstáculos sobre ellas.
///
/// Este componente no hace NADA en runtime: solo se ve en el editor.
/// </summary>
[ExecuteAlways]
public class BeatGridVisualizer : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("BeatManager de la escena. Proporciona el BPM.")]
    public BeatManager beatManager;

    [Header("Velocidad del jugador")]
    [Tooltip("Debe coincidir con PlayerMovement.walkSpeed. " +
             "Define cuántos metros recorre el jugador por segundo.")]
    public float walkSpeed = 6f;

    [Header("Canción (para calcular beatCount automáticamente)")]
    [Tooltip("Duración de la parte jugable de la canción en segundos (SIN contar la intro).\n" +
             "El número de beats se calcula como: ceil(songDuration × BPM / 60).")]
    public float songDuration = 0f;

    [Header("Cuadrícula")]
    [Tooltip("Solo se usa si songDuration = 0. Si songDuration > 0, se ignora y se calcula solo.")]
    public int beatCountFallback = 32;

    [Tooltip("Anchura de las marcas de beat en metros (solo visual).")]
    public float tickWidth = 3f;

    [Tooltip("Mostrar número de beat encima de cada marca.")]
    public bool showBeatNumbers = true;

    [Tooltip("Mostrar solo beats múltiplo de este valor (4 = solo marcas de compás). " +
             "0 o 1 = mostrar todos los beats.")]
    public int showEveryNBeats = 1;

    /// <summary>Distancia entre beats en metros. Depende del BPM y del walkSpeed.</summary>
    public float CellSize
    {
        get
        {
            float bpm = BeatManager.BpmOf(beatManager);
            return walkSpeed * (60f / bpm);
        }
    }

    /// <summary>
    /// Número de beats a renderizar. Se calcula a partir de songDuration y BPM si está definido;
    /// si no, usa beatCountFallback.
    /// </summary>
    public int BeatCount
    {
        get
        {
            if (songDuration > 0f)
            {
                float bpm = BeatManager.BpmOf(beatManager);
                return Mathf.CeilToInt(songDuration * bpm / 60f);
            }
            return beatCountFallback;
        }
    }

    /// <summary>Posición del origen de la cuadrícula (beat 0) en world space.</summary>
    public Vector3 Origin => transform.position;

    /// <summary>Dirección en la que avanza el nivel (normalizada).</summary>
    public Vector3 Forward => transform.forward;

    /// <summary>Distancia (m) de 'worldPos' al origen de la cuadrícula, medida a lo largo del nivel.</summary>
    public float DistanceAlong(Vector3 worldPos) => Vector3.Dot(worldPos - Origin, Forward);

    /// <summary>
    /// Segundos desde el inicio de la canción hasta que el jugador (que avanza a walkSpeed) llega a 'worldPos'.
    /// Sin cuadrícula se usa la Z y 6 m/s, avisando en consola ('context' = quien lo pide).
    /// </summary>
    public static double ArrivalOffset(BeatGridVisualizer grid, Vector3 worldPos, UnityEngine.Object context)
    {
        if (grid != null) return grid.DistanceAlong(worldPos) / grid.walkSpeed;
        Debug.LogWarning($"[{context.GetType().Name}] '{context.name}': BeatGridVisualizer no encontrado. " +
                         $"Usando Z={worldPos.z:F1} m y walkSpeed=6 m/s — el trigger puede ser incorrecto.", context);
        return worldPos.z / 6f;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        float cell    = CellSize;
        int   beats   = BeatCount;
        Vector3 fwd   = transform.forward;
        Vector3 right = transform.right;
        Vector3 up    = Vector3.up;
        int step      = Mathf.Max(1, showEveryNBeats);
        float bpm     = BeatManager.BpmOf(beatManager);

        GUIStyle style = new GUIStyle();
        style.fontSize          = 11;
        style.fontStyle         = FontStyle.Bold;
        style.normal.textColor  = Color.black;

        GUIStyle barStyle = new GUIStyle(style);
        barStyle.fontSize         = 13;
        barStyle.normal.textColor = new Color(0.6f, 0f, 0f);

        for (int i = 0; i <= beats; i += step)
        {
            bool isBar = (i % 4 == 0);   // cada 4 beats = 1 compás (asume 4/4)

            Vector3 center = transform.position + fwd * (i * cell);
            center.y = transform.position.y;    // mantener altura del visualizador

            // Línea perpendicular al avance del jugador
            float halfW = isBar ? tickWidth * 1.4f : tickWidth;
            Gizmos.color = isBar
                ? new Color(0.9f, 0.1f, 0.1f, 0.9f)   // rojo = inicio de compás
                : new Color(0.2f, 0.8f, 0.2f, 0.7f);  // verde = beat normal

            Gizmos.DrawLine(center - right * halfW, center + right * halfW);

            // Puntito en el centro
            Gizmos.DrawSphere(center, isBar ? 0.12f : 0.07f);

            // Etiqueta con número de beat
            if (showBeatNumbers)
            {
                string label = isBar
                    ? $"■ Beat {i}\n({i * cell:F1} m)"
                    : $"Beat {i}";
                Handles.Label(center + up * 0.25f, label, isBar ? barStyle : style);
            }
        }

        // Panel de info en el origen
        string info = $"BPM: {bpm:F0}   walkSpeed: {walkSpeed:F1} m/s\n" +
                      $"cellSize: {cell:F3} m/beat   beats: {beats}";
        GUIStyle infoStyle = new GUIStyle();
        infoStyle.fontSize         = 12;
        infoStyle.fontStyle        = FontStyle.Bold;
        infoStyle.normal.textColor = Color.black;
        Handles.Label(transform.position + up * 0.6f, info, infoStyle);
    }
#endif
}
