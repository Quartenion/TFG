using TMPro;
using UnityEngine;

/// <summary>
/// AVISO DEL TUTORIAL como CARTEL EN EL MUNDO: un texto corto (la tecla/acción, p. ej. «ESPACIO») que flota junto al
/// obstáculo, mira siempre a la cámara y LATE AL RITMO de la música (crece en cada beat).
///   • Ponlo en un objeto vacío justo encima/delante del obstáculo donde hay que usar esa acción.
///   • Aparece cuando el jugador está a menos de «Distancia de aparición» y desaparece cuando lo ha pasado.
///   • Solo se ve si los consejos están activados (TAB → F2) o en la parte de práctica del modo prueba.
/// Menú: Tools/Tutorial/Crear avisos de ejemplo (Nivel 1) crea un juego de carteles para recolocar.
/// </summary>
[AddComponentMenu("Level/Tutorial Hint")]
public class TutorialHint : MonoBehaviour
{
    [Tooltip("Texto grande: la tecla o acción. Ej.: «ESPACIO», «CLIC», «SHIFT», «Q».")]
    public string key = "ESPACIO";
    [Tooltip("(Opcional) Palabra pequeña debajo. Ej.: «saltar», «deslizarse».")]
    public string message = "saltar";
    [Tooltip("(Inglés) Texto grande. Vacío = se traduce solo si es una palabra conocida (ESPACIO → SPACE…).")]
    public string keyEnglish = "";
    [Tooltip("(Inglés) Palabra pequeña. Vacío = se traduce sola si es conocida.")]
    public string messageEnglish = "";
    [Tooltip("Altura del cartel sobre este punto (m).")]
    public float height = 3f;
    [Tooltip("Tamaño del texto (10 ≈ 1 m de alto).")]
    public float textSize = 14f;
    [Tooltip("Metros antes de este punto a los que aparece el cartel.")]
    public float showDistance = 40f;
    [Tooltip("Cuánto crece en cada beat (0.25 = un 25 %).")]
    public float beatPulse = 0.3f;
    [Tooltip("Cartel para la zona de antes de empezar (freeroam, sin música).")]
    public bool inFreeroam = false;
    [Tooltip("Color del texto.")]
    public Color color = Color.white;
    [Tooltip("Cartel pegado a un objeto que se mueve o está escalado (enemigo, barril…): lo sigue sin girar con él y con su tamaño normal.")]
    public bool follow = false;
    [Tooltip("Color del borde de las letras.")]
    public Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Tooltip("(Carteles que siguen a un objeto) Desplazamiento extra en metros (X = lado, Y = arriba, Z = adelante).")]
    public Vector3 offset = Vector3.zero;
    [Tooltip("(Para probar) Se ve siempre, aunque los consejos estén apagados o el jugador esté lejos.")]
    public bool debugAlwaysVisible = false;

    /// <summary>(Opcional, por código) Condición extra para mostrarse, p. ej. «la cabeza del jefe está en el suelo».</summary>
    public System.Func<bool> visibleWhen;

    /// <summary>
    /// Pone un cartel del tutorial ENCIMA de un objeto que se mueve (barril, cabeza del jefe…), solo si los consejos
    /// están activados. Sigue al objeto sin girar con él y con su tamaño normal aunque el objeto esté escalado.
    /// </summary>
    public static TutorialHint Attach(Transform target, string key, string message, float height,
                                      float size = 8f, System.Func<bool> visibleWhen = null)
    {
        if (target == null || !TutorialHUD.HintsOn) return null;
        var h = target.gameObject.AddComponent<TutorialHint>();
        h.key = key; h.message = message; h.height = height; h.textSize = size;
        h.showDistance = 1000f;
        h.color = Color.white;
        h.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        h.visibleWhen = visibleWhen;
        h.follow = true;
        return h;
    }

    private Transform _billboard;
    private TextMeshPro _text;
    private Renderer[] _targetRenderers;
    private bool _targetIsParent;   // (follow) mallas del objeto seguido, para ponerse encima de ellas
    private PlayerMovement _pm;
    private Camera _cam;

    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();

        // (Se crea ya con RectTransform: si TextMeshPro lo cambia después, se pierde la posición.)
        var go = new GameObject("Billboard", typeof(RectTransform));
        _text = go.AddComponent<TextMeshPro>();
        UIFonts.Apply(_text, title: false);
        if (follow)
        {
            // Suelto en la escena (no hijo del objeto): así no hereda su escala/giro y no sale aplastado.
            go.transform.position = transform.position + Vector3.up * height;
            _targetRenderers = GetComponentsInChildren<Renderer>();
            if (_targetRenderers.Length == 0 && transform.parent != null)
            {
                _targetRenderers = transform.parent.GetComponentsInChildren<Renderer>();
                _targetIsParent = true;
            }
        }
        else
        {
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * height;
            go.transform.localRotation = Quaternion.identity;
        }
        _billboard = go.transform;

        ApplyText();
        Loc.OnChanged += ApplyText;
        Keybinds.Changed += ApplyText;
        _text.fontSize = textSize;
        _text.alignment = TextAlignmentOptions.Center;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.color = color;
        _text.outlineWidth = 0.25f;
        _text.outlineColor = outlineColor;
        _text.rectTransform.sizeDelta = new Vector2(20f, 8f);
        go.SetActive(false);
    }

    private void OnDestroy()
    {
        Loc.OnChanged -= ApplyText;
        Keybinds.Changed -= ApplyText;
        if (follow && _billboard != null) Destroy(_billboard.gameObject);   // (va suelto en la escena)
    }

    private void OnDisable()
    {
        if (follow && _billboard != null) _billboard.gameObject.SetActive(false);   // (el objeto seguido se apagó)
    }

    private void ApplyText()
    {
        if (_text == null) return;
        string k = Loc.English ? (string.IsNullOrEmpty(keyEnglish) ? Translate(key) : keyEnglish) : key;
        string m = Loc.English ? (string.IsNullOrEmpty(messageEnglish) ? Translate(message) : messageEnglish) : message;
        k = Keybinds.Fix(k); m = Keybinds.Fix(m);   // (teclas cambiadas en Ajustes → Controles)
        _text.text = string.IsNullOrEmpty(m) ? k : $"{k}\n<size=45%>{m}</size>";
    }

    /// <summary>Traducciones de los textos habituales de los carteles (para no tener que rellenar el inglés a mano).</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> Known =
        new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
    {
        { "ESPACIO", "SPACE" }, { "ESPACIO x2", "SPACE x2" }, { "ESPACIO ×2", "SPACE ×2" }, { "CLIC", "CLICK" },
        { "PARED", "WALL" }, { "SALTA →", "JUMP →" }, { "SALTA", "JUMP" },
        { "saltar", "jump" }, { "doble salto", "double jump" }, { "deslizarse", "slide" }, { "dash", "dash" },
        { "en el aire: caer", "in the air: drop" }, { "romper", "break" }, { "engancharse", "grab on" },
        { "INTERACTUAR", "INTERACT" }, { "MOVERSE", "MOVE" },
        { "a la pared", "at the wall" }, { "al ritmo", "on the beat" }, { "salta hacia ella", "jump towards it" },
        { "mira el muro y pulsa", "look at the wall and press" },
        { "← SALTA", "← JUMP" }, { "devuélvelo", "send it back" }, { "sobre la cabeza", "on its head" },
        { "remar al ritmo", "paddle on the beat" }, { "MÍRALO", "LOOK AT IT" }, { "mantén la mirada", "keep looking" },
        { "para quitártelo", "to get rid of it" }, { "SALTA + S", "JUMP + S" }, { "vida extra", "extra life" }, { "MIRA", "LOOK" },
        { "← CLIC DCHO     CLIC IZQ →", "← RIGHT CLICK     LEFT CLICK →" },
    };

    private static string Translate(string s)
    {
        if (s == null || !Known.TryGetValue(s.Trim(), out string en)) return s;
        // Respeta mayúsculas / minúsculas del original («INTERACTUAR» → «INTERACT», «saltar» → «jump»).
        string t = s.Trim();
        if (t == t.ToUpperInvariant()) return en.ToUpperInvariant();
        if (t == t.ToLowerInvariant()) return en.ToLowerInvariant();
        return en;
    }

    private void LateUpdate()
    {
        if (_billboard == null) return;
        bool show = ShouldShow();
        if (_billboard.gameObject.activeSelf != show) _billboard.gameObject.SetActive(show);
        if (!show) return;

        if (follow)
        {
            // Encima de lo más alto del objeto (sus mallas), 'height' metros por arriba.
            Vector3 pos = transform.position + Vector3.up * height;
            bool any = false;
            Bounds b = default;
            if (_targetRenderers != null)
                foreach (Renderer r in _targetRenderers)
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            if (any) pos = new Vector3(b.center.x, b.max.y + height, b.center.z);
            // Si el cartel es un objeto HIJO (p. ej. «Cartel · MÍRALO» bajo un enemigo), lo que lo muevas en el
            // editor se suma: así se puede recolocar a mano. Más el desplazamiento extra 'offset'.
            if (_targetIsParent && transform.parent != null) pos += transform.position - transform.parent.position;
            _billboard.position = pos + offset;
        }

        // Mirar a la cámara (solo giro horizontal, para que no se tuerza al saltar).
        if (_cam == null) _cam = Camera.main;
        if (_cam != null)
        {
            Vector3 d = _billboard.position - _cam.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) _billboard.rotation = Quaternion.LookRotation(d);
        }

        // Latido: golpe al empezar cada beat que se va apagando.
        // Solo late desde que sale el 3-2-1-¡YA! (a su metrónomo) y luego con la canción; antes, quieto.
        float pulse = 0f;
        BeatManager bm = BeatManager.Instance;
        if (bm != null && bm.IsPlaying && bm.BeatDuration > 0f && bm.CorrectedTime >= 0f)
            pulse = Mathf.Exp(-(bm.CorrectedTime % bm.BeatDuration) / bm.BeatDuration * 7f);
        else if (StartSequenceManager.CountdownBeatDSP >= 0 && StartSequenceManager.CountdownBeatInterval > 0)
        {
            double since = AudioSettings.dspTime - StartSequenceManager.CountdownBeatDSP;
            if (since >= 0)
            {
                double i = StartSequenceManager.CountdownBeatInterval;
                pulse = Mathf.Exp(-(float)((since % i) / i) * 7f);
            }
        }
        _billboard.localScale = Vector3.one * (1f + beatPulse * pulse);
    }

    private bool ShouldShow()
    {
        if (HudHider.IsHidden) return false;
        if (visibleWhen != null && !visibleWhen()) return false;
        if (debugAlwaysVisible) return true;
        if (!TutorialHUD.HintsOn) return false;
        if (_pm == null) { _pm = FindAnyObjectByType<PlayerMovement>(); if (_pm == null) return false; }
        if (_pm.IsFreeroam != inFreeroam) return false;
        float dz = transform.position.z - _pm.transform.position.z;
        return dz < showDistance && dz > -2f;   // aparece al acercarse, se va al pasarlo
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Vector3 top = transform.position + Vector3.up * height;
        Gizmos.DrawLine(transform.position, top);
        Gizmos.DrawWireCube(top, new Vector3(3f, 1.2f, 0.1f));
#if UNITY_EDITOR
        UnityEditor.Handles.Label(top + Vector3.up, $"{key} · {message}");
#endif
    }
}
