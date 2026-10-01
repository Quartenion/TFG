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

    [Header("Desintegración al derrumbarse (solo visual)")]
    [Tooltip("Al romperse el suelo (final de la Fase 2 / inicio de la 3) cada plataforma se deshace en BLOQUES que\n" +
             "caen girando y se desintegran. Sin colliders: no hacen daño.")]
    [SerializeField] private bool _shatterOnCollapse = true;
    [Tooltip("Tamaño (m) de cada bloque.")]
    [SerializeField] private float _shatterChunkSize = 5f;
    [Tooltip("Grosor (m) de la parte de arriba de la plataforma que se convierte en bloques (lo que se ve).")]
    [SerializeField] private float _shatterDepth = 10f;
    [Tooltip("Segundos que tarda cada bloque en desintegrarse (mín/máx).")]
    [SerializeField] private Vector2 _shatterLife = new Vector2(1.6f, 3.2f);
    [Tooltip("Retraso máximo (s) con el que se suelta cada bloque (se desmorona poco a poco).")]
    [SerializeField] private float _shatterMaxDelay = 0.6f;
    [Tooltip("Los bloques caen con la gravedad del jugador × esto (> 1 = MÁS RÁPIDO que el jugador y el jefe).")]
    [SerializeField] private float _shatterGravityMultiplier = 1.8f;
    [Tooltip("Tope de bloques en total (rendimiento).")]
    [SerializeField] private int _shatterMaxChunks = 900;

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
    // ── Derrumbe (transición a la Fase 3) ───────────────────────────────────────
    private bool   _collapsed;
    private double _shakeStart, _collapseStart, _collapseEnd;
    private float  _collapseDepth, _shakeAmplitude;
    private bool   _collidersOff;
    private readonly List<(Transform t, Vector3 basePos, float roll, Quaternion rot0)> _falling =
        new List<(Transform, Vector3, float, Quaternion)>();


    /// <summary>
    /// El suelo se ROMPE: deja de generar plataformas; primero TIEMBLA 'shakeBeats' (se desmorona, cada vez más)
    /// y después todas las plataformas se hunden en la lava en 'fallBeats' (cayendo cada vez más rápido y
    /// ladeándose) y se destruyen. Al empezar a caer se apagan sus colliders: el jugador (y el kayak) caen libres.
    /// </summary>
    public void Collapse(float fallBeats, float depth = 220f, float shakeBeats = 0f, float shakeAmplitude = 0.35f)
    {
        if (_collapsed) return;
        _collapsed = true;
        _activated = false;
        _rising.Clear();

        double beatDur = _beatManager != null ? _beatManager.BeatDuration : 0.5;
        _shakeStart     = AudioSettings.dspTime;
        _collapseStart  = _shakeStart + Mathf.Max(0f, shakeBeats) * beatDur;   // empieza a CAER tras temblar
        _collapseEnd    = _collapseStart + Mathf.Max(0.1f, fallBeats) * beatDur;
        _collapseDepth  = depth;
        _shakeAmplitude = shakeAmplitude;
        _collidersOff   = false;

        foreach (Transform t in _spawned)
            if (t != null) _falling.Add((t, t.position, Random.Range(-8f, 8f), t.rotation));
        _spawned.Clear();
    }

    /// <summary>
    /// Tras derrotar al jefe: el suelo VUELVE A SALIR de la lava. Las primeras plataformas (bajo el jugador y
    /// justo delante) emergen en 'firstRiseBeats' con su TOP a 'topY'; luego se sigue generando suelo por
    /// delante como siempre (auto-sostén).
    /// </summary>
    public void Resume(float topY, float firstRiseBeats, float firstRiseDepth = -1f, int firstCount = 3)
    {
        if (_player == null)
        {
            var pm = FindAnyObjectByType<PlayerMovement>();
            if (pm != null) { _player = pm.transform; _playerCC = pm.GetComponent<CharacterController>(); }
        }
        if (_beatManager == null) _beatManager = BeatManager.Instance;
        if (_platformPrefab == null || _player == null) return;

        _collapsed  = false;
        _firstSpawn = false;
        _falling.Clear();
        _rising.Clear();

        Vector3 c = _player.position;
        c.x = transform.position.x;
        c.y = topY - _platformHalfHeight;
        c  -= Forward * (_platformLength * 0.5f);   // el jugador cae cerca del inicio de la 1ª
        _nextCenter = c;

        float keepBeats = _riseBeats, keepH = _riseHeight;
        _riseBeats = Mathf.Max(0.1f, firstRiseBeats);
        if (firstRiseDepth > 0f) _riseHeight = firstRiseDepth;
        for (int i = 0; i < Mathf.Max(1, firstCount); i++) SpawnOne();
        _riseBeats  = keepBeats;
        _riseHeight = keepH;
        _activated = true;
    }

    /// <summary>Dirección de avance del suelo.</summary>
    public Vector3 ForwardAxis => Forward;

    private float _stopAlong = float.PositiveInfinity;

    /// <summary>No genera plataformas que EMPIECEN más allá de esta distancia (medida sobre el eje de avance).</summary>
    public void StopAfter(float alongForward) => _stopAlong = alongForward;

    private bool BeyondStop => Vector3.Dot(_nextCenter, Forward) - _platformLength * 0.5f > _stopAlong;

    public void SpawnNext()
    {
        if (_collapsed) return;   // tras el derrumbe no vuelve a generar suelo
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
        if (BeyondStop) return;
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
        UpdateShards();

        // Derrumbe: 1) tiemblan (se desmoronan, cada vez más)  2) se hunden (ease-in) ladeándose y se destruyen.
        if (_collapsed)
        {
            if (now < _collapseStart)
            {
                float s = _collapseStart > _shakeStart
                    ? Mathf.Clamp01((float)((now - _shakeStart) / (_collapseStart - _shakeStart)))
                    : 1f;
                float amp = _shakeAmplitude * s;
                foreach (var f in _falling)
                {
                    if (f.t == null) continue;
                    f.t.position = f.basePos + new Vector3(Random.Range(-amp, amp), Random.Range(-amp, amp) * 0.5f,
                                                           Random.Range(-amp, amp));
                }
                return;
            }

            // Al empezar a caer: fuera colliders → el jugador y el kayak caen libres a la lava.
            if (!_collidersOff)
            {
                _collidersOff = true;
                foreach (var f in _falling)
                    if (f.t != null)
                        foreach (Collider c in f.t.GetComponentsInChildren<Collider>()) c.enabled = false;
                if (_shatterOnCollapse) ShatterFalling();
            }

            float u = _collapseEnd > _collapseStart
                ? Mathf.Clamp01((float)((now - _collapseStart) / (_collapseEnd - _collapseStart)))
                : 1f;
            for (int i = _falling.Count - 1; i >= 0; i--)
            {
                var f = _falling[i];
                if (f.t == null) { _falling.RemoveAt(i); continue; }
                Vector3 p = f.basePos;
                p.y -= _collapseDepth * u * u;                                  // cae cada vez más rápido
                f.t.position = p;
                f.t.rotation = Quaternion.Euler(0f, 0f, f.roll * u) * f.rot0;   // ladeo progresivo
                if (u >= 1f) { Destroy(f.t.gameObject); _falling.RemoveAt(i); }
            }
            return;
        }

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
            while (!BeyondStop && Vector3.Dot(_nextCenter - _player.position, fwdA) < need && guard++ < 8)
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

    // ── Bloques del derrumbe (visual) ─────────────────────────────────────────

    private struct Shard
    {
        public Transform t; public Vector3 vel, spinAxis, scale0;
        public float spin, delay, life, age;
    }
    private readonly List<Shard> _shards = new List<Shard>();
    private float _shardGravity = 28f;

    /// <summary>Gravedad de los bloques (el jefe pone la MISMA que la del jugador al caer, para que caigan juntos).</summary>
    public void SetShardGravity(float g) { if (g > 0f) _shardGravity = g; }

    /// <summary>Convierte la parte de arriba de cada plataforma que cae en bloques sueltos y esconde la original.</summary>
    private void ShatterFalling()
    {
        int budget = Mathf.Max(0, _shatterMaxChunks);
        float cs = Mathf.Max(0.5f, _shatterChunkSize);

        foreach (var f in _falling)
        {
            if (f.t == null || budget <= 0) continue;
            Renderer[] rends = f.t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) continue;
            Bounds b = rends[0].bounds;
            foreach (Renderer r in rends) b.Encapsulate(r.bounds);
            Material mat = rends[0].sharedMaterial;
            foreach (Renderer r in rends) r.enabled = false;   // la plataforma "se rompe": solo quedan los bloques

            int nx = Mathf.Max(1, Mathf.RoundToInt(b.size.x / cs));
            int nz = Mathf.Max(1, Mathf.RoundToInt(b.size.z / cs));
            int ny = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(_shatterDepth, b.size.y) / cs));
            Vector3 cell = new Vector3(b.size.x / nx, Mathf.Min(_shatterDepth, b.size.y) / ny, b.size.z / nz);

            for (int y = 0; y < ny && budget > 0; y++)
                for (int x = 0; x < nx && budget > 0; x++)
                    for (int z = 0; z < nz && budget > 0; z++)
                    {
                        budget--;
                        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Destroy(c.GetComponent<Collider>());                       // solo visual: no hace daño
                        c.name = "PlatformShard";
                        var cr = c.GetComponent<Renderer>();
                        if (mat != null) cr.sharedMaterial = mat;
                        cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        c.transform.SetParent(_parent, true);
                        c.transform.position = new Vector3(b.min.x + (x + 0.5f) * cell.x,
                                                           b.max.y - (y + 0.5f) * cell.y,
                                                           b.min.z + (z + 0.5f) * cell.z);
                        Vector3 sc = cell * Random.Range(0.85f, 1f);
                        c.transform.localScale = sc;

                        Vector3 outDir = c.transform.position - b.center; outDir.y = 0f;
                        _shards.Add(new Shard
                        {
                            t        = c.transform,
                            vel      = outDir.normalized * Random.Range(0.5f, 2f),   // sin impulso hacia arriba: caen con el jugador
                            spinAxis = Random.onUnitSphere,
                            spin     = Random.Range(40f, 220f),
                            // La capa de arriba (la que pisa el jugador) cae YA, a la vez que él; las de abajo, poco a poco.
                            delay    = y == 0 ? Random.Range(0f, 0.05f) : Random.Range(0f, _shatterMaxDelay) + y * 0.08f,
                            life     = Random.Range(_shatterLife.x, _shatterLife.y),
                            scale0   = sc,
                            age      = 0f,
                        });
                    }
        }
    }

    /// <summary>Los bloques se sueltan, caen girando y se desintegran (se encogen hasta desaparecer).</summary>
    /// <summary>Los bloques se sueltan, caen girando (más rápido que el jugador) y se desintegran.</summary>
    private void UpdateShards()
    {
        if (_shards.Count == 0) return;
        float dt = Time.deltaTime;
        float g  = _shardGravity * Mathf.Max(0.1f, _shatterGravityMultiplier);
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            Shard sh = _shards[i];
            if (sh.t == null) { _shards.RemoveAt(i); continue; }
            sh.age += dt;
            float live = sh.age - sh.delay;
            if (live > 0f)
            {
                sh.vel += Vector3.down * g * dt;
                sh.t.position += sh.vel * dt;
                sh.t.rotation  = Quaternion.AngleAxis(sh.spin * dt, sh.spinAxis) * sh.t.rotation;
                float k = Mathf.Clamp01(live / sh.life);
                float shrink = k < 0.4f ? 1f : 1f - (k - 0.4f) / 0.6f;     // se desintegra en el último 60 %
                sh.t.localScale = sh.scale0 * Mathf.Max(0f, shrink);
                if (k >= 1f) { Destroy(sh.t.gameObject); _shards.RemoveAt(i); continue; }
            }
            else
            {
                // Aún aguanta: tiembla un poco antes de soltarse.
                sh.t.position += Random.insideUnitSphere * 0.03f;
            }
            _shards[i] = sh;
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
