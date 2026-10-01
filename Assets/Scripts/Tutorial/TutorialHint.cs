using TMPro;
using UnityEngine;

/// <summary>
/// AVISO DEL TUTORIAL como CARTEL EN EL MUNDO: un texto corto (la tecla/acción, p. ej. «ESPACIO») que flota junto al
/// obstáculo, mira siempre a la cámara y LATE AL RITMO de la música (crece en cada beat).
///   • Ponlo en un objeto vacío justo encima/delante del obstáculo donde hay que usar esa acción.
///   • Aparece cuando el jugador está a menos de «Distancia de aparición» y desaparece cuando lo ha pasado.
///   • Solo se ve si los consejos están activados (F1 → F2) o en la parte de práctica del modo prueba.
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
    [Tooltip("(Para probar) Se ve siempre, aunque los consejos estén apagados o el jugador esté lejos.")]
    public bool debugAlwaysVisible = false;

    private Transform _billboard;
    private TextMeshPro _text;
    private PlayerMovement _pm;
    private Camera _cam;

    private void Start()
    {
        _pm = FindAnyObjectByType<PlayerMovement>();

        // (Se crea ya con RectTransform: si TextMeshPro lo cambia después, se pierde la posición.)
        var go = new GameObject("Billboard", typeof(RectTransform));
        _text = go.AddComponent<TextMeshPro>();
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * height;
        go.transform.localRotation = Quaternion.identity;
        _billboard = go.transform;

        ApplyText();
        Loc.OnChanged += ApplyText;
        _text.fontSize = textSize;
        _text.alignment = TextAlignmentOptions.Center;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.color = color;
        _text.outlineWidth = 0.25f;
        _text.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        _text.rectTransform.sizeDelta = new Vector2(20f, 8f);
        go.SetActive(false);
    }

    private void OnDestroy() => Loc.OnChanged -= ApplyText;

    private void ApplyText()
    {
        if (_text == null) return;
        string k = Loc.English ? (string.IsNullOrEmpty(keyEnglish) ? Translate(key) : keyEnglish) : key;
        string m = Loc.English ? (string.IsNullOrEmpty(messageEnglish) ? Translate(message) : messageEnglish) : message;
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
