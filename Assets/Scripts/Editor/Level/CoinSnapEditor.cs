using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// (Diseño de niveles) Encaja las MONEDAS SELECCIONADAS:
///   • A lo largo del nivel → al beat o medio beat más cercano (rejilla del BeatGridVisualizer: origen, dirección
///     y tamaño de celda = walkSpeed × duración del beat).
///   • Lateral y altura → al número entero o medio (…, 1, 1.5, 2…) más cercano.
/// Se usa con el botón que aparece en el Inspector al seleccionar una o varias monedas, o desde
/// Tools → Level Design (también vale seleccionar el objeto padre: coge todas las monedas de dentro). Ctrl+Z lo deshace.
/// </summary>
[CustomEditor(typeof(Coin)), CanEditMultipleObjects]
public class CoinSnapEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"Snap ({targets.Length} moneda{(targets.Length == 1 ? "" : "s")})", EditorStyles.boldLabel);

        GUI.backgroundColor = new Color(1f, 0.85f, 0.3f);
        if (GUILayout.Button("Snap al beat o medio beat más cercano  (+ posición a 0.5)", GUILayout.Height(30)))
            SnapCoins(CoinsFrom(targets), 0.5f);
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("Snap solo a beats enteros  (+ posición a 0.5)"))
            SnapCoins(CoinsFrom(targets), 1f);
        GUI.backgroundColor = new Color(0.6f, 0.85f, 1f);
        if (GUILayout.Button("Snap X al número IMPAR más cercano  (…, -1, 1, 3…)"))
            SnapXToOdd(CoinsFrom(targets));
        GUI.backgroundColor = Color.white;
    }

    private static List<Transform> CoinsFrom(Object[] objs)
    {
        var list = new List<Transform>();
        foreach (Object o in objs)
            if (o is Coin c && !list.Contains(c.transform)) list.Add(c.transform);
        return list;
    }

    // ── Menú (sirve también seleccionando el padre de las monedas) ────────────

    /// <summary>Solo el eje X (mundo): al número impar más cercano (…, -3, -1, 1, 3…). Y y Z no se tocan.</summary>
    public static void SnapXToOdd(List<Transform> coins)
    {
        if (coins.Count == 0) { Debug.LogWarning("[Snap monedas] No hay monedas seleccionadas."); return; }
        Undo.RecordObjects(coins.ToArray(), "Snap X al impar");
        int moved = 0;
        foreach (Transform t in coins)
        {
            Vector3 p = t.position;
            float odd = 2f * Mathf.Round((p.x - 1f) / 2f) + 1f;
            if (!Mathf.Approximately(odd, p.x)) { p.x = odd; t.position = p; moved++; }
        }
        Debug.Log($"[Snap monedas] X al impar más cercano: {coins.Count} monedas ({moved} movidas).");
    }

    private static List<Transform> SelectedCoins()
    {
        var list = new List<Transform>();
        foreach (GameObject go in Selection.gameObjects)
            foreach (Coin c in go.GetComponentsInChildren<Coin>(true))
                if (!list.Contains(c.transform)) list.Add(c.transform);
        return list;
    }

    // ── Snap ──────────────────────────────────────────────────────────────────

    /// <param name="beatStep">1 = beats enteros, 0.5 = beats y medios beats.</param>
    public static void SnapCoins(List<Transform> coins, float beatStep)
    {
        if (coins.Count == 0) { Debug.LogWarning("[Snap monedas] No hay monedas seleccionadas."); return; }

        var grid = Object.FindAnyObjectByType<BeatGridVisualizer>();
        if (grid == null) { Debug.LogWarning("[Snap monedas] No hay BeatGridVisualizer en la escena."); return; }
        float cell = grid.CellSize;
        if (cell < 0.001f) return;
        float step = cell * beatStep;

        Vector3 o   = grid.Origin;
        Vector3 fwd = grid.Forward; fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
        Vector3 up    = Vector3.up;
        Vector3 right = Vector3.Cross(up, fwd).normalized;

        Undo.RecordObjects(coins.ToArray(), "Snap monedas al beat");
        int moved = 0;
        foreach (Transform t in coins)
        {
            Vector3 p = t.position;

            // 1) A lo largo del nivel: al beat / medio beat más cercano (medido desde el origen de la rejilla).
            float along   = Vector3.Dot(p - o, fwd);
            float alongSn = Mathf.Round(along / step) * step;
            float fwdComp = Vector3.Dot(o, fwd) + alongSn;

            // 2) Lateral y altura: al número entero o medio más cercano.
            float lateral = RoundHalf(Vector3.Dot(p, right));
            float height  = RoundHalf(p.y);

            Vector3 np = right * lateral + up * height + fwd * fwdComp;
            if ((np - p).sqrMagnitude > 1e-8f) { t.position = np; moved++; }
        }
        Debug.Log($"[Snap monedas] {coins.Count} monedas encajadas ({moved} movidas) · " +
                  $"{(beatStep < 1f ? "beat o medio beat" : "beat entero")} · celda {cell:0.###} m.");
    }

    private static float RoundHalf(float v) => Mathf.Round(v * 2f) / 2f;
}
