using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Interruptor de mirada. Se activa cuando el jugador lo mira (con el punto de mira encima y sin paredes en medio)
/// durante <see cref="_holdDuration"/> segundos. Al completarse lanza <see cref="OnActivated"/>, que se puede
/// conectar a cualquier método, por ejemplo a <c>BeatOscillator.Trigger()</c> para abrir una puerta.
///
/// Configuración.
///   1. Se añade este componente al objeto del interruptor, que necesita un Collider en él o en un hijo.
///   2. Se asigna la cámara del jugador (si no, se usa Camera.main).
///   3. Se conecta OnActivated a BeatOscillator.Trigger() de la puerta, o a cualquier otro método.
///   4. Opcionalmente, se conecta OnProgress a una barra o un aro de carga, o se asignan Fill Scale y Glow Renderer
///      para usar la indicación integrada.
///
/// Como el jugador avanza solo, el interruptor se coloca más adelante en su camino, de forma que lo tenga a la vista
/// mientras se acerca y la carga se complete durante la aproximación. Hold Duration se ajusta a ese tiempo.
/// </summary>
[AddComponentMenu("Level/Gaze Switch")]
public class GazeSwitch : MonoBehaviour
{
    [Header("Detección de mirada")]
    [Tooltip("Cámara del jugador. Si se deja vacío se usa Camera.main.")]
    [SerializeField] private Camera _playerCamera;

    [Tooltip("Distancia máxima a la que se puede activar mirándolo.")]
    [SerializeField] private float _maxDistance = 40f;
    /// <summary>Distancia máxima desde la que se puede mirar. Un BloodSucker no drena sangre más allá de esta distancia.</summary>
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

    // -=+=- Estado -=+=-

    private float _progress;
    private bool  _activated;
    private bool  _wasGazed;
    private MaterialPropertyBlock _mpb;
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    /// <summary>Interruptores activos en la escena, incluidos los que se crean durante la partida. Los lee GazeChargeUI.</summary>
    public static readonly System.Collections.Generic.List<GazeSwitch> All = new System.Collections.Generic.List<GazeSwitch>();

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        GazeChargeUI.Ensure();   // si la escena no tiene un aro de carga en el HUD, se crea uno
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
            Camera main = Camera.main;               // la cámara puede haber cambiado, por ejemplo al subir al kayak
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

    // -=+=- Detección -=+=-

    private bool IsGazedAt()
    {
        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        // Con raíces ignoradas (por ejemplo, el jugador y su kayak), cuenta el primer collider que no pertenezca a ellas.
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

        // Primer collider que encuentra el rayo desde el centro de la cámara. Si es este interruptor (o un hijo suyo),
        // el jugador lo está mirando directamente, sin paredes en medio.
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

    /// <summary>Los colliders de estas raíces (y de sus hijos) no tapan la mirada, por ejemplo el jugador y su kayak.</summary>
    public void SetIgnoredRoots(params Transform[] roots) => _ignoredRoots = roots;

    /// <summary>Cámara desde la que se mira, si Camera.main no es la del jugador.</summary>
    public void SetCamera(Camera cam) { if (cam != null) _playerCamera = cam; }

    /// <summary>Nombre del primer collider que tocó el rayo de la mirada en el último fotograma, para depurar.</summary>
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

    /// <summary>Reinicia el interruptor (carga a 0 y sin activar) para poder volver a usarlo.</summary>
    public void ResetSwitch()
    {
        _progress  = 0f;
        _activated = false;
        _wasGazed  = false;
        OnProgress?.Invoke(0f);
        ApplyVisual(0f);
    }

    /// <summary>
    /// Configura el interruptor desde el código, por ejemplo los que se añaden al jefe durante la partida. Si holdSeconds
    /// o maxDistance son menores o iguales que 0, se mantiene el valor actual. glow es el renderer que se ilumina al cargar.
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

    /// <summary>Capas que cuentan para el rayo de la mirada. Lo primero que toque el rayo tiene que ser este interruptor.</summary>
    public void SetObstruction(LayerMask mask) => _obstruction = mask;

    /// <summary>Progreso de carga actual, de 0 a 1.</summary>
    public float Progress => _progress;
    public bool  IsActivated => _activated;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.35f);
        // Rayo de referencia hacia la cámara (si está asignada), para ver la línea de la mirada.
        if (_playerCamera != null)
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.3f);
            Gizmos.DrawLine(transform.position, _playerCamera.transform.position);
        }
    }
#endif
}
