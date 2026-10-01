using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Crea en la escena abierta (pensado para el Nivel 1) un juego de CARTELES del tutorial (TutorialHint: la tecla flotando
/// junto al obstáculo y latiendo al ritmo) dentro de un objeto raíz «Tutorial». Los de las acciones especiales (Q, pared, tirolina) se colocan solos antes del PRIMER
/// bloque rompible / pared de wallrun / tirolina del nivel; los demás van en beats fijos: muévelos a donde quieras.
/// </summary>
public static class TutorialEditorMenu
{
    [MenuItem("Tools/Tutorial/Crear avisos de ejemplo (Nivel 1)")]
    private static void Create()
    {
        var grid = Object.FindAnyObjectByType<BeatGridVisualizer>();
        float cell = grid != null ? grid.CellSize : 4f;
        float z0 = grid != null ? grid.Origin.z : 0f;
        float Z(float beat) => z0 + beat * cell;

        var old = GameObject.Find("Tutorial");
        if (old != null && !EditorUtility.DisplayDialog("Tutorial", "Ya hay un objeto «Tutorial». ¿Crear otro igualmente?", "Crear", "Cancelar")) return;

        var root = new GameObject("Tutorial");
        Undo.RegisterCreatedObjectUndo(root, "Crear avisos del tutorial");

        var pm = Object.FindAnyObjectByType<PlayerMovement>();
        float startZ = pm != null ? pm.transform.position.z : Z(-4f);

        Add(root, "Inicio (freeroam)", startZ + 8f, "E", "mira el muro y pulsa", true);
        Add(root, "Latido", Z(2f), "CLIC", "al ritmo");
        Add(root, "Salto", Z(24f), "ESPACIO", "saltar");
        Add(root, "Doble salto", Z(32f), "ESPACIO x2", "doble salto");
        Add(root, "Deslizarse", Z(40f), "SHIFT", "deslizarse");
        Add(root, "Dash", Z(56f), "E + A / D", "dash");
        Add(root, "Slam", Z(72f), "S", "en el aire: caer");

        // Acciones especiales: antes del primer elemento que las necesita.
        var breakable = Object.FindObjectsByType<BreakableBlock>().OrderBy(b => b.transform.position.z).FirstOrDefault();
        if (breakable != null) Add(root, "Bloquear", breakable.transform.position.z, "Q", "romper", false, breakable.transform.position.x);

        int jumpable = LayerMask.NameToLayer("Jumpable");
        if (jumpable >= 0)
        {
            var wall = Object.FindObjectsByType<Collider>().Where(c => c.gameObject.layer == jumpable && c.bounds.center.z > Z(0f))
                             .OrderBy(c => c.bounds.min.z).FirstOrDefault();
            if (wall != null) Add(root, "Wallrun", wall.bounds.min.z, "PARED", "salta hacia ella", false, wall.bounds.center.x);
        }

        var zip = Object.FindObjectsByType<Zipline>().OrderBy(z => z.StartPos.z).FirstOrDefault();
        if (zip != null) Add(root, "Tirolina", zip.StartPos.z, "F", "engancharse", false, zip.StartPos.x);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[Tutorial] Avisos creados en «Tutorial». Revisa su posición (gizmo azul) y su texto en el Inspector.");
    }

    private static void Add(GameObject root, string name, float z, string key, string msg, bool freeroam = false, float x = 0f)
    {
        var go = new GameObject("Cartel · " + name);
        go.transform.SetParent(root.transform, false);
        go.transform.position = new Vector3(x, 0f, z);
        var h = go.AddComponent<TutorialHint>();
        h.key = key; h.message = msg; h.inFreeroam = freeroam;
    }
}
