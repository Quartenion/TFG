using UnityEditor;
using UnityEngine;

/// <summary>
/// Herramienta de editor para duplicar un objeto y desplazarlo en Z.
///
/// USO:
///   Tools → Duplicar con offset Z
///   1. Arrastra el objeto base al campo "Objeto base" (o selecciónalo en la escena).
///   2. Pulsa el botón tantas veces como quieras.
///      · 1ª pulsación → copia en Z + 15.99452
///      · 2ª pulsación → copia en Z + 31.98904
///      · 3ª pulsación → copia en Z + 47.98356  …
///   3. "Resetear contador" vuelve a empezar desde 0.
///
/// Soporta Undo (Ctrl+Z). Si el objeto es un prefab, lo instancia como prefab.
/// </summary>
public class DuplicateZOffsetTool : EditorWindow
{
    private const float Z_STEP = 16;

    private GameObject _source;
    private int        _count;

    // ── Abrir ventana ──────────────────────────────────────────────────────

    [MenuItem("Tools/Duplicar con offset Z")]
    public static void Open()
    {
        var w = GetWindow<DuplicateZOffsetTool>(false, "Dup. Z Offset", true);
        w.minSize = new Vector2(280, 160);
    }

    // ── GUI ───────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Paso Z: 16m", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // Auto-rellenar con la selección si el campo está vacío
        if (_source == null && Selection.activeGameObject != null)
            _source = Selection.activeGameObject;

        _source = (GameObject)EditorGUILayout.ObjectField(
            "Objeto base", _source, typeof(GameObject), true);

        EditorGUILayout.Space(8);

        // Mostrar el Z resultante de la próxima pulsación
        float nextZ = _source != null
            ? _source.transform.position.z + Z_STEP * (_count + 1)
            : 0f;

        string label = _source != null
            ? $"Duplicar  →  Z = {nextZ:F5} m  (×{_count + 1})"
            : "Selecciona un objeto primero";

        GUI.enabled = _source != null;
        if (GUILayout.Button(label, GUILayout.Height(36)))
            CreateDuplicate();
        GUI.enabled = true;

        EditorGUILayout.Space(6);

        // Contador (solo lectura)
        using (new EditorGUI.DisabledGroupScope(true))
            EditorGUILayout.IntField("Duplicados creados", _count);

        EditorGUILayout.Space(2);

        if (GUILayout.Button("Resetear contador"))
            _count = 0;
    }

    // ── Lógica ────────────────────────────────────────────────────────────

    private void CreateDuplicate()
    {
        if (_source == null) return;

        _count++;

        // Instanciar: si es prefab lo tratamos como tal, si no, copia normal
        GameObject go;
        var prefabRoot = PrefabUtility.GetCorrespondingObjectFromSource(_source);
        if (prefabRoot != null)
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefabRoot, _source.transform.parent);
        else
            go = Object.Instantiate(_source, _source.transform.parent);

        go.name = _source.name + " (" + _count + ")";

        // Posición: mismo XY que el original, Z desplazado N pasos
        Vector3 p = _source.transform.position;
        go.transform.SetPositionAndRotation(
            new Vector3(p.x, p.y, p.z + Z_STEP * _count),
            _source.transform.rotation);
        go.transform.localScale = _source.transform.localScale;

        Undo.RegisterCreatedObjectUndo(go, "Duplicate Z Offset");

        // Enfocar el nuevo objeto en la jerarquía
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        Debug.Log($"[DupZ] '{go.name}'  Z = {go.transform.position.z:F5} m  (paso {_count})");
    }
}
