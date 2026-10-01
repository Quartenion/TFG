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

    // ── Nivel 2: kayak ────────────────────────────────────────────────────────

    [MenuItem("Tools/Tutorial/Crear avisos (Nivel 2: kayak)")]
    private static void CreateLevel2()
    {
        var kayak = Object.FindObjectsByType<KayakController>(FindObjectsInactive.Include)
                          .OrderBy(k => k.transform.position.z).FirstOrDefault();
        if (kayak == null) { EditorUtility.DisplayDialog("Tutorial", "No hay ningún kayak (KayakController) en la escena abierta.", "OK"); return; }

        var root = NewRoot("Tutorial (kayak)");
        if (root == null) return;
        Vector3 p = kayak.transform.position;
        Add(root, "Kayak · remar", p.z, "← CLIC DCHO     CLIC IZQ →", "remar al ritmo", false, p.x);
        Finish(root, "Aviso del kayak creado sobre el primer kayak: «clic derecho → izquierda, clic izquierdo → derecha».");
    }

    // ── Nivel 3: mirar (GazeSwitch y BloodSuckers) ────────────────────────────

    [MenuItem("Tools/Tutorial/Crear avisos (Nivel 3: mirar)")]
    private static void CreateLevel3()
    {
        var root = NewRoot("Tutorial (mirar)");
        if (root == null) return;
        int n = 0;

        // Primer interruptor de mirada que NO es un BloodSucker.
        var gaze = Object.FindObjectsByType<GazeSwitch>(FindObjectsInactive.Include)
                         .Where(g => g.GetComponentInParent<BloodSucker>(true) == null)
                         .OrderBy(g => g.transform.position.z).FirstOrDefault();
        if (gaze != null) { AddOn(gaze.transform, "MÍRALO", "mantén la mirada"); n++; }

        // Primer BloodSucker.
        var sucker = Object.FindObjectsByType<BloodSucker>(FindObjectsInactive.Include)
                           .OrderBy(b => b.transform.position.z).FirstOrDefault();
        if (sucker != null) { AddOn(sucker.transform, "MÍRALO", "para quitártelo"); n++; }

        if (n == 0) { Object.DestroyImmediate(root); EditorUtility.DisplayDialog("Tutorial", "No hay GazeSwitch ni BloodSucker en la escena abierta.", "OK"); return; }
        Finish(root, $"{n} avisos de «mirar» creados (pegados al primer interruptor de mirada y al primer BloodSucker).");
    }

    private static GameObject NewRoot(string name)
    {
        if (GameObject.Find(name) != null && !EditorUtility.DisplayDialog("Tutorial", $"Ya hay un objeto «{name}». ¿Crear otro igualmente?", "Crear", "Cancelar"))
            return null;
        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, "Crear avisos del tutorial");
        return root;
    }

    private static void Finish(GameObject root, string msg)
    {
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[Tutorial] " + msg + " Revisa su posición y guarda la escena.");
    }

    /// <summary>Cartel pegado a un objeto que se mueve (lo sigue sin girar ni escalarse con él).</summary>
    private static void AddOn(Transform target, string key, string msg)
    {
        var go = new GameObject("Cartel · " + key);
        Undo.RegisterCreatedObjectUndo(go, "Cartel");
        go.transform.SetParent(target, false);
        var h = go.AddComponent<TutorialHint>();
        h.key = key; h.message = msg; h.follow = true; h.height = 0.6f; h.textSize = 8f;
        h.color = Color.white; h.outlineColor = new Color(0f, 0f, 0f, 0.9f);
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
