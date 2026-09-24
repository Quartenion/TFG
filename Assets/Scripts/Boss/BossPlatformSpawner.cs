using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Genera el suelo del boss fight: cada 4 beats SUBE una plataforma nueva (40×150×41.14) encadenada
/// justo al final de la anterior en el eje de avance. Como una plataforma mide 4 beats de recorrido
/// (41.14 m a 12 m/s · 70 BPM), spawnear una cada 4 beats mantiene el suelo siempre por delante.
///
/// USO:
///   1. Coloca este componente en un objeto vacío en el CENTRO donde quieres la 1ª plataforma.
///   2. Asigna el prefab del bloque (40×150×41.14, pivote al centro).
///   3. En el BossController, engancha el evento OnPlatformBeat → este SpawnNext().
///
/// La plataforma aparece por DEBAJO y sube a su sitio en 'Rise Beats' (efecto de emerger del vacío).
/// Las que quedan muy atrás se destruyen solas.
/// </summary>
[AddComponentMenu("Boss/Boss Platform Spawner")]
public class BossPlatformSpawner : MonoBehaviour
{
    [Header("Plataforma")]
    [Tooltip("Prefab del bloque (X:40 · Y:150 · Z:41.14286, pivote al centro).")]
    [SerializeField] private GameObject _platformPrefab;
    [Tooltip("Longitud de la plataforma en el eje de avance (Z). Debe coincidir con el prefab.")]
    [SerializeField] private float _platformLength = 41.14286f;
    [Tooltip("Padre opcional para las plataformas generadas (orden en la Hierarchy).")]
    [SerializeField] private Transform _parent;

    [Header("Eje de avance")]
    [Tooltip("BeatGridVisualizer para tomar la dirección de avance. Vacío = usar Forward Axis.")]
    [SerializeField] private BeatGridVisualizer _grid;
    [Tooltip("Dirección de avance si no hay grid (normalmente +Z).")]
    [SerializeField] private Vector3 _forwardAxis = Vector3.forward;

    [Header("Subida (emerger)")]
    [Tooltip("Metros que sube desde abajo hasta su sitio.")]
    [SerializeField] private float _riseHeight = 20f;
    [Tooltip("Beats que tarda en subir del todo.")]
    [SerializeField] private float _riseBeats = 2f;
    [Tooltip("BeatManager (se usa BeatManager.Instance si se deja vacío).")]
    [SerializeField] private BeatManager _beatManager;

    [Header("Limpieza")]
    [Tooltip("Destruir plataformas que queden a más de estos metros POR DETRÁS del jugador.")]
    [SerializeField] private float _cleanupBehind = 60f;

    [Header("Arranque robusto")]
    [Tooltip("Ancla la 1ª plataforma justo bajo el jugador (esté donde esté). Robusto para debug y para\n" +
             "el flujo normal. Desactívalo solo si quieres que el suelo empiece en la posición fija del spawner.")]
    [SerializeField] private bool _anchorToPlayerIfFar = true;
    [Tooltip("Media altura de la plataforma (para poner su TOP a los pies del jugador al re-anclar). 40×150×41 → 75.")]
    [SerializeField] private float _platformHalfHeight = 75f;
    [Tooltip("Plataformas que suelta de golpe en el primer spawn (buffer para no caerte).")]
    [SerializeField] private int _prewarmCount = 2;
    [Tooltip("Auto-sostén: mantiene SIEMPRE esta cantidad de plataformas por delante del jugador (no depende\n" +
             "del beat, así el suelo nunca se para ni te caes). 0 = desactivado (solo suelta por evento).")]
    [SerializeField] private int _lookaheadPlatforms = 3;

    // ── Estado ────────────────────────────────────────────────────────────────
    private struct Rising { public Transform t; public float y0, y1; public double dsp0, dsp1; }

    private readonly List<Rising>    _rising  = new List<Rising>();
    private readonly List<Transform> _spawned = new List<Transform>();
    private Vector3   _nextCenter;
    private Transform _player;
    private CharacterController _playerCC;
    private bool      _started;
    private bool      _firstSpawn = true;
    private bool      _activated;

    private Vector3 Forward => _grid != null ? _grid.Forward
                                             : (_forwardAxis.sqrMagnitude > 1e-4f ? _forwardAxis.normalized : Vector3.forward);

    private void Start()
    {
        if (_beatManager == null) _beatManager = BeatManager.Instance;
        var pm = FindAnyObjectByType<PlayerMovement>();
        if (pm != null) { _player = pm.transform; _playerCC = pm.GetComponent<CharacterController>(); }
        _nextCenter = transform.position;   // la 1ª plataforma se centra aquí (salvo re-anclaje)
        _started = true;
    }

    /// <summary>Sube una plataforma nueva al final de la anterior. Engánchalo a BossController.OnPlatformBeat.</summary>
    public void SpawnNext()
    {
        if (!_started) { if (_beatManager == null) _beatManager = BeatManager.Instance; _nextCenter = transform.position; _started = true; }
        if (_platformPrefab == null) return;

        if (_firstSpawn)
        {
            _firstSpawn = false;
            _activated  = true;
            ReanchorIfPlayerFar();
            for (int i = 0; i < Mathf.Max(1, _prewarmCount); i++) SpawnOne(instant: true);   // buffer YA (sin subida)
            return;
        }
        SpawnOne();
    }

    /// <summary>Coloca la 1ª plataforma justo bajo el jugador (esté donde esté). Robusto para debug y normal.</summary>
    private void ReanchorIfPlayerFar()
    {
        if (!_anchorToPlayerIfFar || _player == null) return;

        float feetY = _playerCC != null ? _playerCC.bounds.min.y : _player.position.y;
        Vector3 c = _player.position;
        c.x  = transform.position.x;                    // conservar la X del spawner (centro)
        c.y  = feetY - _platformHalfHeight;             // TOP de la plataforma a los pies del jugador
        c   -= Forward * (_platformLength * 0.5f);      // el jugador queda cerca del inicio de la 1ª
        _nextCenter = c;
    }

    private void SpawnOne(bool instant = false)
    {
        Vector3 finalPos = _nextCenter;
        Vector3 startPos = instant ? finalPos : finalPos - Vector3.up * _riseHeight;

        GameObject go = Instantiate(_platformPrefab, startPos, _platformPrefab.transform.rotation, _parent);

        if (!instant)
        {
            double beatDur = _beatManager != null ? _beatManager.BeatDuration : (60f / 120f);
            double now = AudioSettings.dspTime;
            _rising.Add(new Rising { t = go.transform, y0 = startPos.y, y1 = finalPos.y,
                                     dsp0 = now, dsp1 = now + _riseBeats * beatDur });
        }
        _spawned.Add(go.transform);

        _nextCenter += Forward * _platformLength;
    }

    private void Update()
    {
        double now = AudioSettings.dspTime;

        // Animar las que están subiendo (ease-out).
        for (int i = _rising.Count - 1; i >= 0; i--)
        {
            Rising r = _rising[i];
            if (r.t == null) { _rising.RemoveAt(i); continue; }

            float u = r.dsp1 > r.dsp0 ? Mathf.Clamp01((float)((now - r.dsp0) / (r.dsp1 - r.dsp0))) : 1f;
            float eased = 1f - (1f - u) * (1f - u);
            Vector3 p = r.t.position;
            p.y = Mathf.Lerp(r.y0, r.y1, eased);
            r.t.position = p;

            if (u >= 1f) _rising.RemoveAt(i);
        }

        // Auto-sostén: garantizar suelo por delante del jugador (inmune al beat / al cableado del evento).
        if (_activated && _player != null && _lookaheadPlatforms > 0 && _platformPrefab != null)
        {
            Vector3 fwdA = Forward;
            float need = _platformLength * _lookaheadPlatforms;
            int guard = 0;
            while (Vector3.Dot(_nextCenter - _player.position, fwdA) < need && guard++ < 8)
                SpawnOne();   // con subida (van muy por delante, terminan de subir antes de que llegues)
        }

        // Limpiar las que quedan muy atrás del jugador.
        if (_player != null)
        {
            Vector3 fwd = Forward;
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] == null) { _spawned.RemoveAt(i); continue; }
                float behind = Vector3.Dot(_player.position - _spawned[i].position, fwd);
                if (behind > _cleanupBehind)
                {
                    Destroy(_spawned[i].gameObject);
                    _spawned.RemoveAt(i);
                }
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Previsualiza las próximas plataformas.
        Vector3 c = Application.isPlaying ? _nextCenter : transform.position;
        Vector3 f = Forward;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        for (int i = 0; i < 4; i++)
        {
            Gizmos.DrawWireCube(c + f * (_platformLength * i), new Vector3(40f, 1f, _platformLength));
        }
    }
#endif
}
