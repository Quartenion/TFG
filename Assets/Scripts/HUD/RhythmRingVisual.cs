using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nota entrante del HUD rítmico: aro + spikes FRONT/BACK (como el crosshair).
/// La nota NO escala: se queda al tamaño del hit-zone (= crosshair). Lo que se mueve son
/// los SPIKES, que entran desde fuera y se desplazan hacia dentro hasta coincidir con los
/// spikes del crosshair. El aro se queda fijo.
///
/// CICLO DE VIDA:
///   Fase 1 — Aproximación: los spikes se trasladan radialmente hacia el hit-zone
///            (offset grande → 0). Frames por cercanía (lejos → medio → hittable). Rotan si
///            enableSpikeRotation (sincronizados con el crosshair).
///   Fase 2 — Llegada: los spikes quedan en el hit-zone y la nota se desvanece EN EL SITIO
///            (no sigue al centro).
///   ACIERTO: los spikes hacen un BOUNCE hacia el centro (como el crosshair al clicar),
///            tintados de hitColor, y desaparecen.
///
/// SETUP DEL PREFAB: root con este script (sin Image); hijos BackSpikes, Ring, FrontSpikes.
/// Maqueta todo a escala 1 = tamaño del hit-zone para que coincida con el crosshair.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class RhythmRingVisual : MonoBehaviour
{
    [Header("Aro")]
    public Image ringImage;
    public Sprite[] ringFrames;

    [Header("Spikes — FRONT (delante del aro)")]
    public Image[]       frontSpikeImages;
    public Sprite[]      frontSpikeFrames;
    public RectTransform frontSpikeContainer;
    public float         frontSpikeRotationSpeed = 90f;

    [Header("Spikes — BACK (detrás del aro)")]
    public Image[]       backSpikeImages;
    public Sprite[]      backSpikeFrames;
    public RectTransform backSpikeContainer;
    public float         backSpikeRotationSpeed = 90f;

    [Header("Sincronización con el crosshair")]
    [Tooltip("TEMPORAL: desactiva TODA rotación de los spikes de la nota (se quedan en su base) " +
             "para comprobar el alineamiento con el crosshair. Reactívalo cuando cuadre.")]
    public bool enableSpikeRotation = false;

    [Tooltip("Con crosshair asignado, los spikes de la nota giran en sentido CONTRARIO al crosshair.")]
    public bool counterRotateToCrosshair = true;

    [Header("Cercanía")]
    [Tooltip("Progreso (0..1) a partir del cual se pasa del frame 0 (lejos) al 1 (medio). " +
             "El frame 2 (hittable) aparece al entrar en la ventana de acierto.")]
    [Range(0.1f, 0.95f)] public float midFrameThreshold = 0.6f;

    [Tooltip("Brillo/tinte cuando la nota NO es hittable (lejos). Sube a BLANCO al llegar a hittable.\n" +
             "Así aro y spikes van igual de tenues aunque sus sprites sean blancos.\n" +
             "Si ya controlas el brillo con los sprites, pon esto en BLANCO para no tintar.")]
    public Color dimColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("Aproximación de los spikes")]
    [Tooltip("Distancia (px) EXTRA hacia fuera desde la que entran los spikes. Si es ≤ 0 se " +
             "calcula desde (startDiameter − targetDiameter)/2 del RhythmHUDController.")]
    public float spikeApproachDistance = 0f;

    [Header("Desaparición en el hit-zone")]
    [Tooltip("Al llegar al hit-zone la nota se queda ahí (coincidiendo con el crosshair) y se " +
             "desvanece en este tiempo. NO sigue hacia el centro.")]
    public float passDuration = 0.25f;

    [Header("Acierto")]
    [Tooltip("Color al que se tiñe la nota al acertar el beat, antes de desaparecer.")]
    public Color hitColor = new Color(0.25f, 1f, 0.35f, 1f);   // verde
    [Tooltip("Distancia (px) que los spikes rebotan hacia el CENTRO al acertar (como el crosshair).")]
    public float hitBounceDistance = 25f;

    // ── API pública ──────────────────────────────────────────────────────────
    public float Progress => _travelDuration > 0f ? _elapsed / _travelDuration : 0f;
    public bool  IsConsumed => _consumed;
    public event Action OnExpired;

    // ── Privados ───────────────────────────────────────────────────────────────
    private RectTransform      _rect;
    private BeatManager        _beatManager;
    private CrosshairController _crosshair;

    private float _travelDuration;
    private float _approachDistance;
    private float _elapsed;
    private bool  _consumed;
    private bool  _inPhase2;
    private int   _frame = -1;

    // Posiciones base (hit-zone) y direcciones hacia fuera de cada spike.
    private Vector2[] _frontBase, _frontOut, _backBase, _backOut;

    // =========================================================================
    // INICIALIZACIÓN
    // =========================================================================

    public void Init(float startDiameter, float targetDiameter,
                     float travelDuration, BeatManager bm,
                     CrosshairController crosshair = null)
    {
        _rect           = GetComponent<RectTransform>();
        _beatManager    = bm;
        _crosshair      = crosshair;
        _travelDuration = travelDuration;
        _elapsed        = 0f;
        _consumed       = false;
        _inPhase2       = false;
        _frame          = -1;

        // Distancia de aproximación de los spikes: la del inspector o, si es 0, la
        // equivalente al antiguo escalado ((start − target)/2).
        _approachDistance = spikeApproachDistance > 0f
            ? spikeApproachDistance
            : Mathf.Max(0f, (startDiameter - targetDiameter) * 0.5f);

        // La nota NO escala: queda a tamaño 1 (= hit-zone / crosshair).
        _rect.anchorMin        = new Vector2(0.5f, 0.5f);
        _rect.anchorMax        = new Vector2(0.5f, 0.5f);
        _rect.pivot            = new Vector2(0.5f, 0.5f);
        _rect.anchoredPosition = Vector2.zero;
        _rect.localScale       = Vector3.one;

        CaptureBase(frontSpikeImages, out _frontBase, out _frontOut);
        CaptureBase(backSpikeImages,  out _backBase,  out _backOut);

        if (ringImage != null) ringImage.raycastTarget = false;

        SetAllAlpha(1f);
        ApplyFrame(0);
        ApplySpikeOffset(_approachDistance);   // arrancan fuera
    }

    // =========================================================================
    // UPDATE
    // =========================================================================

    private void Update()
    {
        if (_consumed) return;

        _elapsed += Time.deltaTime;
        RotateSpikes(Time.deltaTime);

        if (!_inPhase2) UpdatePhase1();
        else            UpdatePhase2Normal();
    }

    private void RotateSpikes(float dt)
    {
        if (!enableSpikeRotation)
        {
            if (frontSpikeContainer != null) frontSpikeContainer.localRotation = Quaternion.identity;
            if (backSpikeContainer  != null) backSpikeContainer.localRotation  = Quaternion.identity;
            return;
        }

        if (_crosshair != null)
        {
            float ang = _crosshair.CurrentRotationAngle * (counterRotateToCrosshair ? -1f : 1f);
            Quaternion rot = Quaternion.Euler(0f, 0f, ang);
            if (frontSpikeContainer != null) frontSpikeContainer.localRotation = rot;
            if (backSpikeContainer  != null) backSpikeContainer.localRotation  = rot;
        }
        else
        {
            if (frontSpikeContainer != null) frontSpikeContainer.Rotate(0f, 0f, frontSpikeRotationSpeed * dt);
            if (backSpikeContainer  != null) backSpikeContainer.Rotate(0f, 0f, backSpikeRotationSpeed  * dt);
        }
    }

    // ── Fase 1: los spikes entran hacia el hit-zone ──────────────────────────
    private void UpdatePhase1()
    {
        float t = Mathf.Clamp01(_elapsed / _travelDuration);
        ApplySpikeOffset(Mathf.Lerp(_approachDistance, 0f, t));   // fuera → hit-zone
        ApplyFrame(FrameIndex());

        if (_elapsed >= _travelDuration)
            _inPhase2 = true;
    }

    // ── Fase 2: llegaron → se quedan y se desvanecen (NO siguen al centro) ────
    private void UpdatePhase2Normal()
    {
        float t = (_elapsed - _travelDuration) / passDuration;
        if (t >= 1f) { Expire(); return; }

        ApplySpikeOffset(0f);                    // en el hit-zone, sobre el crosshair
        SetAllAlpha(Mathf.Lerp(1f, 0f, t));      // fade en el sitio
    }

    /// <summary>0 lejos, 1 medio, 2 en la ventana de acierto.</summary>
    private int FrameIndex()
    {
        float timeRemaining = _travelDuration - _elapsed;
        float goodWindow    = _beatManager != null ? _beatManager.GoodWindow : 0.15f;
        if (timeRemaining <= goodWindow) return 2;
        return Progress >= midFrameThreshold ? 1 : 0;
    }

    // =========================================================================
    // ACIERTO — bounce hacia el centro + color
    // =========================================================================

    public void TriggerHitAbsorption()
    {
        if (_consumed) return;
        _consumed = true;
        StopAllCoroutines();
        StartCoroutine(HitBounce());
    }

    private IEnumerator HitBounce()
    {
        const float dur = 0.15f;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float e = Mathf.Clamp01(t);

            // Rebote hacia el CENTRO (offset negativo) + tinte + fade.
            ApplySpikeOffset(Mathf.Lerp(0f, -hitBounceDistance, Mathf.SmoothStep(0f, 1f, e)));
            SetAllColor(hitColor, Mathf.Lerp(1f, 0f, e));
            RotateSpikes(Time.deltaTime);

            yield return null;
        }

        Expire();
    }

    // =========================================================================
    // SPIKES — posición radial
    // =========================================================================

    private static void CaptureBase(Image[] imgs, out Vector2[] bases, out Vector2[] outDirs)
    {
        int n = imgs != null ? imgs.Length : 0;
        bases   = new Vector2[n];
        outDirs = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            if (imgs[i] == null) continue;
            Vector2 p = imgs[i].rectTransform.anchoredPosition;
            bases[i]   = p;
            outDirs[i] = p.sqrMagnitude > 0.0001f ? p.normalized : Vector2.zero;
        }
    }

    /// <summary>Coloca los spikes a 'offset' px hacia fuera de su base (negativo = hacia el centro).</summary>
    private void ApplySpikeOffset(float offset)
    {
        Move(frontSpikeImages, _frontBase, _frontOut, offset);
        Move(backSpikeImages,  _backBase,  _backOut,  offset);
    }

    private static void Move(Image[] imgs, Vector2[] bases, Vector2[] outDirs, float offset)
    {
        if (imgs == null || bases == null) return;
        for (int i = 0; i < imgs.Length && i < bases.Length; i++)
            if (imgs[i] != null)
                imgs[i].rectTransform.anchoredPosition = bases[i] + outDirs[i] * offset;
    }

    // =========================================================================
    // SPRITES / COLOR
    // =========================================================================

    // state: 0 = lejos, 1 = medio, 2 = hittable. Se mapea al rango de CADA array, así el aro
    // y los spikes llegan a su "hittable" (último frame) a la vez aunque tengan distinto nº
    // de frames (antes, con índice crudo, un array más corto se adelantaba → des-sincronía).
    private void ApplyFrame(int state)
    {
        if (state == _frame) return;
        _frame = state;
        SetSpriteState(ringImage,           ringFrames,       state);
        SetSpriteStateMany(frontSpikeImages, frontSpikeFrames, state);
        SetSpriteStateMany(backSpikeImages,  backSpikeFrames,  state);

        // Brillo por cercanía: tenue (dimColor) en lejos/medio, BLANCO en hittable.
        // Garantiza que aro y spikes vayan igual de tenues aunque sus sprites sean blancos.
        SetAllColor(Color.Lerp(dimColor, Color.white, state * 0.5f), 1f);
    }

    /// <summary>Mapea el estado {0,1,2} a un índice dentro de un array de longitud 'len'.</summary>
    private static int MapIndex(int state, int len)
        => len <= 1 ? 0 : Mathf.Clamp(Mathf.RoundToInt(state * 0.5f * (len - 1)), 0, len - 1);

    private static void SetSpriteState(Image img, Sprite[] frames, int state)
    {
        if (img == null || frames == null || frames.Length == 0) return;
        Sprite s = frames[MapIndex(state, frames.Length)];
        if (s != null) img.sprite = s;
    }

    private static void SetSpriteStateMany(Image[] imgs, Sprite[] frames, int state)
    {
        if (imgs == null || frames == null || frames.Length == 0) return;
        Sprite sp = frames[MapIndex(state, frames.Length)];
        if (sp == null) return;
        foreach (Image s in imgs) if (s != null) s.sprite = sp;
    }

    private void SetAllAlpha(float a)
    {
        SetAlpha(ringImage, a);
        SetAlphaMany(frontSpikeImages, a);
        SetAlphaMany(backSpikeImages, a);
    }

    private static void SetAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color; c.a = a; img.color = c;
    }

    private static void SetAlphaMany(Image[] imgs, float a)
    {
        if (imgs == null) return;
        foreach (Image s in imgs) SetAlpha(s, a);
    }

    private void SetAllColor(Color rgb, float a)
    {
        Color c = rgb; c.a = a;
        if (ringImage != null) ringImage.color = c;
        SetColorMany(frontSpikeImages, c);
        SetColorMany(backSpikeImages, c);
    }

    private static void SetColorMany(Image[] imgs, Color c)
    {
        if (imgs == null) return;
        foreach (Image s in imgs) if (s != null) s.color = c;
    }

    // =========================================================================
    private void Expire()
    {
        OnExpired?.Invoke();
        Destroy(gameObject);
    }
}
