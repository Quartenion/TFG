using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Interruptor de MIRADA: se activa cuando el jugador lo mira (crosshair encima, sin paredes de
/// por medio) durante <see cref="_holdDuration"/> segundos. Al completarse dispara
/// <see cref="OnActivated"/>, que puedes conectar a lo que quieras — por ejemplo, a
/// <c>BeatOscillator.Trigger()</c> para abrir una puerta.
///
/// SETUP:
///   1. Pon este componente en el objeto-interruptor (necesita un Collider en él o en un hijo).
///   2. Asigna la cámara del jugador (o se coge Camera.main).
///   3. Conecta OnActivated → (tu puerta) BeatOscillator.Trigger(), o cualquier método.
///   4. (Opcional) Conecta OnProgress a una barra/aro de carga, o asigna Fill Scale / Glow Renderer
///      para un feedback integrado.
///
/// NOTA (auto-runner): como el jugador avanza solo, coloca el interruptor MÁS ADELANTE en su
/// camino: así lo mantiene a la vista mientras se acerca y la carga se completa durante la
/// aproximación. Ajusta Hold Duration a ese tiempo de acercamiento.
/// </summary>
[AddComponentMenu("Level/Gaze Switch")]
public class GazeSwitch : MonoBehaviour
{
    [Header("Detección de mirada")]
    [Tooltip("Cámara del jugador. Si se deja vacío se usa Camera.main.")]
    [SerializeField] private Camera _playerCamera;

    [Tooltip("Distancia máxima a la que se puede activar mirándolo.")]
    [SerializeField] private float _maxDistance = 40f;
    /// <summary>Distancia máxima a la que se le puede mirar (BloodSucker no chupa más lejos de esto).</summary>
    public float MaxDistance => _maxDistance;

    [Tooltip("Capas que BLOQUEAN la mirada (paredes, obstáculos) además del propio interruptor.\n" +
             "Deja 'Everything' para que cualquier pared entre medias corte la mirada.")]
    [SerializeField] private LayerMask _obstruction = ~0;

    [Header("Carga")]
    [Tooltip("Segundos mirándolo para activarlo.")]
    [Min(0.05f)]
    [SerializeField] private float _holdDuration = 1.5f;

    [Tooltip("Si está activo, la carga baja al dejar de mirarlo; si no, se queda donde iba.")]
    [SerializeField] private bool _decayWhenAway = true;

    [Tooltip("Segundos en descargarse del todo al dejar de mirarlo (si Decay When Away está activo).")]
    [Min(0.05f)]
    [SerializeField] private float _releaseDuration = 1f;

    [Tooltip("Si está activo, una vez activado se queda para siempre. Si no, se puede desactivar\n" +
             "descargándolo (útil para mantener algo abierto solo mientras miras).")]
    [SerializeField] private bool _oneShot = true;

    [Header("Feedback visual (opcional)")]
    [Tooltip("Transform que escala de 0 a 1 con la carga (indicador de progreso).")]
    [SerializeField] private Transform _fillScale;

    [Tooltip("Renderer cuya emisión se enciende con la carga.")]
    [SerializeField] private Renderer _glowRenderer;
    [SerializeField] private Color _glowColor = new Color(0.2f, 0.9f, 1f, 1f);

    [Header("Eventos")]
    [Tooltip("Se dispara al completar la carga. Conéctalo p. ej. a BeatOscillator.Trigger().")]
    public UnityEvent OnActivated;
    [Tooltip("Solo si One Shot está desactivado: al descargarse tras haber estado activo.")]
    public UnityEvent OnDeactivated;
    [Tooltip("Progreso de carga 0..1 cada frame que cambia. Para barras/aros de carga.")]
    public UnityEvent<float> OnProgress;
    public UnityEvent OnGazeStart;
    public UnityEvent OnGazeStop;

    // ── Estado ──────────────────────────────────────────────────────────────────

    private float _progress;
    private bool  _activated;
    private bool  _wasGazed;
    private MaterialPropertyBlock _mpb;
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    /// <summary>Interruptores activos en la escena (los que se crean al jugar también: los lee GazeChargeUI).</summary>
    public static readonly System.Collections.Generic.List<GazeSwitch> All = new System.Collections.Generic.List<GazeSwitch>();

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        GazeChargeUI.Ensure();   // (escenas sin aro de carga en el HUD: se crea uno)
    }

    private void OnDisable() => All.Remove(this);

    private void Awake()
    {
        if (_playerCamera == null) _playerCamera = Camera.main;
        if (_glowRenderer != null) _mpb = new MaterialPropertyBlock();

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning($"[GazeSwitch] '{name}' no tiene Collider: la mirada no podrá detectarlo.", this);

        ApplyVisual(0f);
    }

    private void Update()
    {
        if (_playerCamera == null || !_playerCamera.isActiveAndEnabled)
        {
            Camera main = Camera.main;               // la cámara pudo cambiar (p. ej. al subir al kayak)
            if (main != null) _playerCamera = main;
        }
        if (_playerCamera == null) return;
        if (_oneShot && _activated) return;

        bool gazed = IsGazedAt();

        if (gazed && !_wasGazed) OnGazeStart?.Invoke();
        if (!gazed && _wasGazed) OnGazeStop?.Invoke();
        _wasGazed = gazed;

        float prev = _progress;
        if (gazed)
            _progress += Time.deltaTime / _holdDuration;
        else if (_decayWhenAway)
            _progress -= Time.deltaTime / _releaseDuration;
        _progress = Mathf.Clamp01(_progress);

        if (!Mathf.Approximately(prev, _progress))
        {
            OnProgress?.Invoke(_progress);
            ApplyVisual(_progress);
        }

        if (_progress >= 1f && !_activated)
        {
            _activated = true;
            OnActivated?.Invoke();
        }
        else if (_progress <= 0f && _activated && !_oneShot)
        {
            _activated = false;
            OnDeactivated?.Invoke();
        }
    }

    // ── Detección ───────────────────────────────────────────────────────────────

    private bool IsGazedAt()
    {
        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        // Con raíces ignoradas (p. ej. el jugador y su kayak): el primer collider que NO sea de ellas.
        if (_ignoredRoots != null && _ignoredRoots.Length > 0)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, _maxDistance, _obstruction, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (IsIgnored(h.collider.transform)) continue;
                LastHitName = h.collider.name;
                return h.collider.transform == transform || h.collider.transform.IsChildOf(transform);
            }
            LastHitName = "(nada)";
            return false;
        }

        // El PRIMER collider que encuentra el rayo desde el centro de la cámara. Si es este
        // interruptor (o un hijo suyo), lo estás mirando directamente y sin pared de por medio.
        if (Physics.Raycast(ray, out RaycastHit hit, _maxDistance, _obstruction, QueryTriggerInteraction.Ignore))
        {
            Transform ht = hit.collider.transform;
            LastHitName = hit.collider.name;
            return ht == transform || ht.IsChildOf(transform);
        }
        LastHitName = "(nada)";
        return false;
    }

    private Transform[] _ignoredRoots;

    private bool IsIgnored(Transform t)
    {
        foreach (Transform r in _ignoredRoots)
            if (r != null && (t == r || t.IsChildOf(r))) return true;
        return false;
    }

    /// <summary>Colliders de estas raíces (y sus hijos) NO tapan la mirada (p. ej. el jugador y su kayak).</summary>
    public void SetIgnoredRoots(params Transform[] roots) => _ignoredRoots = roots;

    /// <summary>Cámara desde la que se mira (si Camera.main no es la del jugador).</summary>
    public void SetCamera(Camera cam) { if (cam != null) _playerCamera = cam; }

    /// <summary>Nombre del primer collider que tocó el rayo de mirada en el último frame (para depurar).</summary>
    public string LastHitName { get; private set; } = "(sin comprobar)";

    private void ApplyVisual(float p)
    {
        if (_fillScale != null)
            _fillScale.localScale = Vector3.one * Mathf.Max(0.0001f, p);

        if (_glowRenderer != null && _mpb != null)
        {
            _glowRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorID, _glowColor * p);
            _glowRenderer.SetPropertyBlock(_mpb);
        }
    }

    /// <summary>Reinicia el interruptor (carga a 0, sin activar). Para re-usarlo.</summary>
    public void ResetSwitch()
    {
        _progress  = 0f;
        _activated = false;
        _wasGazed  = false;
        OnProgress?.Invoke(0f);
        ApplyVisual(0f);
    }

    /// <summary>
    /// Configura el interruptor desde código (p. ej. los que se añaden en runtime al jefe).
    /// holdSeconds / maxDistance ≤ 0 → se deja el valor actual. glow = renderer que se ilumina al cargar.
    /// </summary>
    public void Configure(float holdSeconds, float maxDistance, Renderer glow = null)
    {
        if (holdSeconds > 0f) _holdDuration = holdSeconds;
        if (maxDistance > 0f) _maxDistance  = maxDistance;
        if (glow != null)
        {
            _glowRenderer = glow;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
        }
        if (_playerCamera == null) _playerCamera = Camera.main;
    }

    /// <summary>Capas que cuentan para el rayo de mirada (lo primero que toque debe ser este interruptor).</summary>
    public void SetObstruction(LayerMask mask) => _obstruction = mask;

    /// <summary>Progreso de carga actual [0..1].</summary>
    public float Progress => _progress;
    public bool  IsActivated => _activated;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.35f);
        // Rayo de referencia hacia la cámara (si está asignada) para ver la línea de mirada.
        if (_playerCamera != null)
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.3f);
            Gizmos.DrawLine(transform.position, _playerCamera.transform.position);
        }
    }
#endif
}
