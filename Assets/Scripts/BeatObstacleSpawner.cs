using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Genera obstáculos a distancias exactamente alineadas con el beat de la música.
///
/// FORMULA CLAVE:
///   cellSize = walkSpeed × beatDuration = walkSpeed × (60 / BPM)
///
/// Al cambiar el BPM en BeatManager (distintos niveles/canciones), el espaciado
/// entre obstáculos se recalcula automáticamente. No hace falta tocar nada más.
///
/// Ejemplos:
///   120 BPM, walkSpeed 6 → cellSize = 3.0 m por beat
///    90 BPM, walkSpeed 6 → cellSize = 4.0 m por beat
///   180 BPM, walkSpeed 6 → cellSize = 2.0 m por beat
///
/// SETUP:
///   1. Crea un GameObject vacío en la escena y añade este componente.
///   2. Asigna BeatManager y PlayerMovement en el Inspector.
///   3. Arrastra uno o varios prefabs de obstáculos al array.
///   4. Ajusta el patrón y la anticipación.
///   5. (Opcional) Los prefabs pueden tener BeatApproachIndicator para feedback visual.
/// </summary>
public class BeatObstacleSpawner : MonoBehaviour
{
    [Header("Referencias")]
    public BeatManager    beatManager;
    public PlayerMovement playerMovement;

    [Header("Prefabs de obstáculos")]
    [Tooltip("Se elige uno aleatoriamente para cada celda que tenga obstáculo.")]
    public GameObject[] obstaclePrefabs;

    [Header("Anticipación")]
    [Tooltip("Cuántos beats por delante se generan los obstáculos. " +
             "A 120 BPM y walkSpeed 6: 8 beats = 24 m de anticipación.")]
    public int beatsAhead = 8;

    [Tooltip("Cuántos beats por detrás del jugador se destruyen los obstáculos.")]
    public int beatsBehindCleanup = 2;

    [Header("Patrón de generación (cíclico)")]
    [Tooltip("Define qué beats del ciclo tienen obstáculo (true) o están vacíos (false).\n" +
             "Ejemplos:\n" +
             "  [true, false]           → cada 2 beats\n" +
             "  [true, false, false]    → cada 3 beats\n" +
             "  [true, true, false, false] → dos seguidos, dos de descanso")]
    public bool[] spawnPattern = { true, false, true, false };

    [Tooltip("Probabilidad de spawn cuando el patrón indica un obstáculo. " +
             "1 = siempre, 0.7 = 30% de celdas vacías para variedad.")]
    [Range(0f, 1f)]
    public float spawnChance = 0.8f;

    [Tooltip("Spread lateral aleatorio máximo (metros). " +
             "0 = siempre centrado en el eje del jugador.")]
    public float lateralSpread = 1.5f;

    /// <summary>
    /// Distancia entre beats en metros.
    /// Se recalcula automáticamente cada vez que cambia el BPM o el walkSpeed.
    /// </summary>
    public float CellSize
    {
        get
        {
            if (beatManager == null || playerMovement == null) return 3f;
            return playerMovement.walkSpeed * beatManager.BeatDuration;
        }
    }

    // beat index → GameObject (null = celda vacía deliberada, no repetir intento)
    private readonly Dictionary<int, GameObject> _spawnedBeats
        = new Dictionary<int, GameObject>();

    private int _lastCheckedBeat = -1;

    private void Update()
    {
        if (beatManager == null || playerMovement == null) return;
        if (obstaclePrefabs == null || obstaclePrefabs.Length == 0) return;

        int currentBeat = beatManager.CurrentBeatIndex;
        if (currentBeat == _lastCheckedBeat) return;
        _lastCheckedBeat = currentBeat;

        // Generar beats por delante
        for (int i = 1; i <= beatsAhead; i++)
            TrySpawnAtBeat(currentBeat + i);

        // Destruir obstáculos pasados
        var toRemove = new List<int>();
        foreach (var kvp in _spawnedBeats)
        {
            if (kvp.Key < currentBeat - beatsBehindCleanup)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (int k in toRemove) _spawnedBeats.Remove(k);
    }

    private void TrySpawnAtBeat(int beatIndex)
    {
        if (_spawnedBeats.ContainsKey(beatIndex)) return;

        // Patrón cíclico
        int patternLen = spawnPattern != null ? spawnPattern.Length : 0;
        if (patternLen == 0) { _spawnedBeats[beatIndex] = null; return; }

        int patIdx = beatIndex % patternLen;
        if (patIdx < 0) patIdx += patternLen;

        if (!spawnPattern[patIdx] || Random.value > spawnChance)
        {
            _spawnedBeats[beatIndex] = null;
            return;
        }

        // Calcular posición world alineada al beat
        Vector3 pos = GetBeatWorldPosition(beatIndex);

        // Spread lateral
        if (lateralSpread > 0f)
        {
            Transform pt    = playerMovement.transform;
            Vector3   right = new Vector3(pt.right.x, 0f, pt.right.z).normalized;
            pos += right * Random.Range(-lateralSpread, lateralSpread);
        }

        GameObject prefab   = obstaclePrefabs[Random.Range(0, obstaclePrefabs.Length)];
        GameObject obstacle = Instantiate(prefab, pos, Quaternion.identity);

        // Pasar referencias al indicador visual si el prefab lo tiene
        var indicator = obstacle.GetComponent<BeatApproachIndicator>();
        if (indicator != null)
            indicator.Init(beatManager, playerMovement);

        _spawnedBeats[beatIndex] = obstacle;
    }

    /// <summary>
    /// Posición world exacta del beat <paramref name="beatIndex"/>.
    /// Tiene en cuenta el progreso sub-beat (BeatProgress) para precisión de centésimas.
    /// </summary>
    private Vector3 GetBeatWorldPosition(int beatIndex)
    {
        Transform pt      = playerMovement.transform;
        Vector3   forward = new Vector3(pt.forward.x, 0f, pt.forward.z).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        // Beats completos + fracción del beat actual hasta ese índice
        float beatsFromNow = (beatIndex - beatManager.CurrentBeatIndex)
                             - beatManager.BeatProgress;
        float distAhead = beatsFromNow * CellSize;

        // Origen en el suelo a la posición horizontal del jugador
        Vector3 groundOrigin = new Vector3(pt.position.x, 0f, pt.position.z);
        return groundOrigin + forward * distAhead;
        // Y = 0: el prefab posiciona su pivote a la altura que necesite
    }

    // =========================================================================
    // GIZMOS — visualización de la cuadrícula de beats en el editor
    // =========================================================================
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (playerMovement == null || beatManager == null) return;

        float     cell    = CellSize;
        Transform pt      = playerMovement.transform;
        Vector3   fwd     = new Vector3(pt.forward.x, 0f, pt.forward.z).normalized;
        Vector3   origin  = new Vector3(pt.position.x, 0.05f, pt.position.z);

        for (int i = 1; i <= beatsAhead; i++)
        {
            float t   = (float)i / beatsAhead;
            Vector3 p = origin + fwd * (i * cell);

            Gizmos.color = Color.Lerp(Color.green, Color.red, t);
            Gizmos.DrawWireCube(p, new Vector3(1.5f, 0.1f, 0.3f));

            GUIStyle labelStyle = new GUIStyle();
            labelStyle.normal.textColor = Color.black;
            labelStyle.fontStyle = FontStyle.Bold;
            UnityEditor.Handles.Label(
                p + Vector3.up * 0.4f,
                $"Beat +{i}  ({cell * i:F1} m)\nBPM:{beatManager.Bpm:F0}  cell:{cell:F2} m",
                labelStyle);
        }
    }
#endif
}
