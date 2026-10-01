using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ORGANIZA LA JERARQUÍA DEL NIVEL POR DISTANCIA (segmentos de 4 beats).
///
///   Timeline
///     Segmento 010 · beats 40-43        (en la posición del inicio del segmento)
///       Escenario    (suelos, paredes, estructuras, decorado)
///       Obstáculos   (ObstacleInfo, osciladores, bloques que caen, capa Obstacle…)
///       Interactivos (kayaks, tirolinas, puertas, interruptores de mirada, corrientes)
///       Monedas      (monedas colocadas a mano dentro del Timeline)
///     Varios segmentos                  (objetos con script que controlan hijos repartidos en varios segmentos:
///                                        p. ej. Floaters (FloatingObject) o Spikes (WaterSpike); se dejan ENTEROS)
///   Coins / Coins (por beat)            (contenedor del BeatCoinPlacer: sus monedas se agrupan en «Segmento NNN»
///                                        DENTRO del mismo contenedor, así «Borrar monedas» sigue funcionando)
///
/// Reglas de seguridad (el comportamiento del juego no cambia):
///   • Todo se mueve conservando la posición/rotación/escala en el mundo (y se comprueba; si no se puede conservar
///     exactamente, ese objeto NO se mueve y se avisa).
///   • Solo se deshacen grupos VACÍOS (GameObjects con solo Transform). Un objeto con scripts, Rigidbody, etc. se
///     mueve entero con sus hijos. De las mallas ESTÁTICAS (sin scripts de juego) solo se sacan los hijos que estén
///     a MÁS de un segmento de distancia (p. ej. cubos metidos por error dentro de un suelo lejano); sus piezas
///     cercanas (arcos, vigas, vallas…) se quedan con ella.
///   • Las partes internas de prefabs no se tocan. Las raíces fuera de Timeline (Managers, Player, Start, End,
///     jefe, Checkpoints…) no se tocan.
///   • Todo es UNA operación de Undo (Ctrl+Z la deshace entera). La escena queda marcada como modificada: no se
///     guarda sola.
///
/// Menú: Tools/Nivel/Organizar jerarquía — Analizar (sin cambios) | Aplicar
/// </summary>
public static class LevelHierarchyOrganizer
{
    private const int BeatsPerSegment = 4;

    private const string CatScenery     = "Escenario";
    private const string CatObstacles   = "Obstáculos";
    private const string CatInteractive = "Interactivos";
    private const string CatCoins       = "Monedas";
    private const string MultiName      = "Varios segmentos";

    private static readonly HashSet<string> CoinTypes = new HashSet<string> { "Coin", "GazeCoin" };
    private static readonly HashSet<string> InteractiveTypes = new HashSet<string>
        { "KayakController", "Zipline", "DoorInteraction", "GazeSwitch", "WaterCurrentZone", "StartWall" };
    private static readonly HashSet<string> ObstacleTypes = new HashSet<string>
        { "ObstacleInfo", "BeatOscillator", "PivotOscillator", "FallingObstacle", "StairFallingObstacle",
          "FireballObstacle", "WaterSpike", "BreakableBlock", "CrushBottom", "FloatingObject" };

    // Nombres de grupos que son «cajones» (se vacían y su nombre da la categoría).
    private static readonly Dictionary<string, string> CategoryNames = new Dictionary<string, string>
    {
        { "obstacles", CatObstacles }, { "obstáculos", CatObstacles }, { "obstaculos", CatObstacles },
        { "falling blocks", CatObstacles }, { "spikes", CatObstacles },
        { "coins", CatCoins }, { "monedas", CatCoins },
        { "escenario", CatScenery }, { "decor", CatScenery }, { "props", CatScenery },
        { "interactivos", CatInteractive },
    };

    private static readonly string[] SceneryWords = { "structure", "estructura", "floor", "suelo", "wall", "pared",
                                                      "arch", "arco", "barrier", "fence", "valla", "segment" };

    [MenuItem("Tools/Nivel/Organizar jerarquía — Analizar (sin cambios)")]
    private static void Analyze() => Run(false);

    [MenuItem("Tools/Nivel/Organizar jerarquía — Aplicar")]
    private static void Apply()
    {
        if (!EditorUtility.DisplayDialog("Organizar jerarquía",
                "Se reorganizará el Timeline y las monedas de la escena abierta por segmentos de 4 beats.\n\n" +
                "Todo conserva su posición en el mundo y se puede deshacer con Ctrl+Z. La escena NO se guarda sola.\n\n" +
                "¿Continuar?", "Organizar", "Cancelar")) return;
        Run(true);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private class Unit
    {
        public Transform t;
        public int       seg;
        public string    cat;
    }

    private static BeatGridVisualizer s_grid;
    private static float  s_segLen;
    private static Vector3 s_origin, s_fwd;

    private static void Run(bool apply)
    {
        var scene = EditorSceneManager.GetActiveScene();
        s_grid = Object.FindAnyObjectByType<BeatGridVisualizer>();
        if (s_grid == null) { EditorUtility.DisplayDialog("Organizar jerarquía", "No hay BeatGridVisualizer en la escena.", "OK"); return; }
        s_origin = s_grid.Origin;
        s_fwd    = s_grid.Forward.normalized;
        s_segLen = s_grid.CellSize * BeatsPerSegment;
        if (s_segLen <= 0.01f) { Debug.LogError("[Organizar] Tamaño de beat inválido."); return; }

        Transform timeline = scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == "Timeline");
        if (timeline == null) { EditorUtility.DisplayDialog("Organizar jerarquía", "No hay un objeto raíz «Timeline».", "OK"); return; }

        // ── 1. Recoger unidades ─────────────────────────────────────────────
        var units    = new List<Unit>();
        var multi    = new List<Transform>();
        var dissolve = new List<Transform>();   // grupos vacíos que se deshacen (se borran si quedan vacíos)
        foreach (Transform c in timeline.Cast<Transform>().ToList())
            Collect(c, 0, null, units, multi, dissolve);

        // Monedas del BeatCoinPlacer (y raíces «Coins…»): se agrupan dentro de su propio contenedor.
        var coinContainers = new HashSet<Transform>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var p in root.GetComponentsInChildren<BeatCoinPlacer>(true))
                if (p.coinsParent != null) coinContainers.Add(p.coinsParent);
        foreach (var g in scene.GetRootGameObjects())
            if (g.name.StartsWith("Coins")) coinContainers.Add(g.transform);
        coinContainers.Remove(timeline);

        // ── 2. Informe ──────────────────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine($"Escena: {scene.name}   ·   beat = {s_grid.CellSize:F2} m   ·   segmento = {BeatsPerSegment} beats = {s_segLen:F2} m");
        sb.AppendLine($"Unidades en el Timeline: {units.Count}   ·   Grupos con script en varios segmentos: {multi.Count}");
        foreach (Transform m in multi) sb.AppendLine($"  [Varios segmentos] {HierPath(m)}  ({m.childCount} hijos)");
        foreach (var g in units.GroupBy(u => u.seg).OrderBy(g => g.Key))
        {
            sb.AppendLine($"{SegName(g.Key)}");
            foreach (var cg in g.GroupBy(u => u.cat).OrderBy(x => x.Key))
                sb.AppendLine($"    {cg.Key}: {string.Join(", ", cg.OrderBy(u => Along(u.t.position)).Select(u => u.t.name))}");
        }
        foreach (Transform cc in coinContainers)
        {
            int n = cc.Cast<Transform>().Count(t => t.GetComponent<Coin>() != null || t.GetComponent<GazeCoin>() != null);
            sb.AppendLine($"Contenedor de monedas «{cc.name}»: {n} monedas sueltas → se agrupan por segmento dentro de él.");
        }
        string reportPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, $"OrganizarJerarquia_{scene.name}.txt");
        File.WriteAllText(reportPath, sb.ToString());

        if (!apply)
        {
            Debug.Log($"[Organizar] ANÁLISIS de «{scene.name}» (sin cambios): {units.Count} unidades, {multi.Count} grupos multi-segmento. " +
                      $"Informe completo: {reportPath}");
            EditorUtility.OpenWithDefaultApp(reportPath);
            return;
        }

        // ── 3. Aplicar ──────────────────────────────────────────────────────
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Organizar jerarquía por segmentos");

        var segRoots = new Dictionary<int, Transform>();
        var catRoots = new Dictionary<(int, string), Transform>();
        int moved = 0, skipped = 0;
        var skippedNames = new List<string>();

        Transform SegRoot(Transform parent, Dictionary<int, Transform> cache, int seg)
        {
            if (cache.TryGetValue(seg, out var r)) return r;
            r = NewGroup(SegName(seg), parent);
            r.position = s_origin + s_fwd * (seg * s_segLen);
            r.rotation = Quaternion.identity;
            cache[seg] = r;
            return r;
        }

        int total = units.Count;
        int i = 0;
        foreach (var u in units.OrderBy(u => u.seg).ThenBy(u => CatOrder(u.cat)).ThenBy(u => Along(u.t.position)).ThenBy(u => u.t.position.x))
        {
            if ((i++ & 63) == 0) EditorUtility.DisplayProgressBar("Organizar jerarquía", $"{u.t.name}", i / (float)Mathf.Max(1, total));
            Transform seg = SegRoot(timeline, segRoots, u.seg);
            if (!catRoots.TryGetValue((u.seg, u.cat), out Transform cat))
                catRoots[(u.seg, u.cat)] = cat = NewGroup(u.cat, seg);
            if (Move(u.t, cat)) moved++; else { skipped++; skippedNames.Add(HierPath(u.t)); }
        }

        if (multi.Count > 0)
        {
            Transform mroot = NewGroup(MultiName, timeline);
            foreach (Transform m in multi.OrderBy(m => Along(m.position)))
                if (Move(m, mroot)) moved++; else { skipped++; skippedNames.Add(HierPath(m)); }
        }

        // Orden: segmentos por número, categorías en orden fijo, «Varios segmentos» al final.
        int sib = 0;
        foreach (var kv in segRoots.OrderBy(k => k.Key))
        {
            kv.Value.SetSiblingIndex(sib++);
            var cats = kv.Value.Cast<Transform>().OrderBy(t => CatOrder(t.name)).ToList();
            for (int k = 0; k < cats.Count; k++) cats[k].SetSiblingIndex(k);
        }

        // Grupos vacíos que se han quedado sin nada.
        int removed = 0;
        foreach (Transform d in dissolve.OrderByDescending(Depth))
            if (d != null && d.childCount == 0 && IsPureGroup(d)) { Undo.DestroyObjectImmediate(d.gameObject); removed++; }

        // Monedas del BeatCoinPlacer.
        int coinsMoved = 0;
        foreach (Transform cc in coinContainers)
        {
            var coins = cc.Cast<Transform>().Where(t => t.GetComponent<Coin>() != null || t.GetComponent<GazeCoin>() != null).ToList();
            var cache = new Dictionary<int, Transform>();
            // Reusar grupos «Segmento …» que ya existan (si se ejecuta dos veces).
            foreach (Transform t in cc) if (t.name.StartsWith("Segmento ") && IsPureGroup(t)) cache[SegFromName(t.name)] = t;
            foreach (Transform c in coins.OrderBy(c => Along(c.position)).ThenBy(c => c.position.x))
            {
                Transform g = SegRoot(cc, cache, SegOf(c.position));
                if (Move(c, g)) coinsMoved++; else { skipped++; skippedNames.Add(HierPath(c)); }
            }
            int s2 = 0;
            foreach (var kv in cache.OrderBy(k => k.Key)) kv.Value.SetSiblingIndex(s2++);
        }

        EditorUtility.ClearProgressBar();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);

        string msg = $"[Organizar] «{scene.name}»: {moved} objetos colocados en {segRoots.Count} segmentos, {coinsMoved} monedas agrupadas, " +
                     $"{removed} grupos vacíos eliminados, {multi.Count} grupos en «{MultiName}».";
        if (skipped > 0) msg += $"\n  {skipped} objetos NO movidos (no se podía conservar su transform exacto):\n  " + string.Join("\n  ", skippedNames);
        Debug.Log(msg + $"\nInforme: {reportPath}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Recogida
    // ─────────────────────────────────────────────────────────────────────────

    private static void Collect(Transform t, int depth, string catHint, List<Unit> units, List<Transform> multi, List<Transform> dissolve)
    {
        bool prefabRoot = PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject);
        bool inPrefab   = PrefabUtility.IsPartOfPrefabInstance(t.gameObject) && !prefabRoot;
        if (inPrefab) return;   // (no se puede sacar de su prefab)

        if (!prefabRoot && IsPureGroup(t))
        {
            string nameCat = CategoryFromName(t.name);
            if (depth == 0 || nameCat != null || SpansSegments(t))
            {
                dissolve.Add(t);
                foreach (Transform c in t.Cast<Transform>().ToList())
                    Collect(c, depth + 1, nameCat ?? catHint, units, multi, dissolve);
                return;
            }
            units.Add(new Unit { t = t, seg = SegOf(FirstPos(t)), cat = Classify(t, catHint) });
            return;
        }

        // Objeto con componentes (o prefab).
        if (HasGameplayBehaviour(t) || prefabRoot)
        {
            // Scripts que controlan MUCHOS hijos repartidos por el nivel (Floaters, Spikes…): se dejan enteros aparte.
            if (!prefabRoot && SegmentCount(t) >= 3 && t.childCount > 1) { multi.Add(t); return; }
            units.Add(new Unit { t = t, seg = SegOf(t.position), cat = Classify(t, catHint) });
            return;
        }

        // Malla estática: se queda entera con sus piezas, pero lo que esté a más de un segmento de distancia
        // (cosas metidas por error dentro de un suelo lejano) se saca y se coloca en su segmento.
        units.Add(new Unit { t = t, seg = SegOf(t.position), cat = Classify(t, catHint) });
        ExtractFar(Along(t.position), t, depth, catHint, units, multi, dissolve);
    }

    private static void ExtractFar(float anchor, Transform parent, int depth, string catHint,
                                   List<Unit> units, List<Transform> multi, List<Transform> dissolve)
    {
        foreach (Transform c in parent.Cast<Transform>().ToList())
        {
            if (PrefabUtility.IsPartOfPrefabInstance(c.gameObject) && !PrefabUtility.IsOutermostPrefabInstanceRoot(c.gameObject)) continue;
            if (Mathf.Abs(Along(c.position) - anchor) > s_segLen)
                Collect(c, depth + 1, catHint, units, multi, dissolve);
            else if (!HasGameplayBehaviour(c) && !PrefabUtility.IsOutermostPrefabInstanceRoot(c.gameObject))
                ExtractFar(anchor, c, depth + 1, catHint, units, multi, dissolve);
        }
    }

    private static string Classify(Transform t, string hint)
    {
        var types = new HashSet<string>(t.GetComponentsInChildren<Component>(true).Where(c => c != null).Select(c => c.GetType().Name));
        bool own(string n) => t.GetComponent(n) != null;
        if (CoinTypes.Any(own)) return CatCoins;
        if (InteractiveTypes.Any(own)) return CatInteractive;
        string lower = t.name.ToLowerInvariant();
        if (SceneryWords.Any(w => lower.Contains(w))) return CatScenery;
        if (hint != null) return hint;
        if (ObstacleTypes.Any(types.Contains) || LayerMask.LayerToName(t.gameObject.layer) == "Obstacle") return CatObstacles;
        if (InteractiveTypes.Any(types.Contains)) return CatInteractive;
        if (CoinTypes.Any(types.Contains) && t.GetComponentsInChildren<Renderer>(true).All(r => r.GetComponentInParent<Coin>() != null || r.GetComponentInParent<GazeCoin>() != null))
            return CatCoins;
        return CatScenery;
    }

    private static string CategoryFromName(string name)
    {
        string n = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), @"\s*\(.*\)\s*$", "").Trim();
        return CategoryNames.TryGetValue(n, out string c) ? c : null;
    }

    /// <summary>Solo Transform (sin componentes): un simple «cajón».</summary>
    private static bool IsPureGroup(Transform t) => t.GetComponents<Component>().Length == 1 && !(t is RectTransform);

    /// <summary>¿Tiene algo que dependa de su jerarquía? (scripts de juego, Rigidbody, animación, audio, LOD…).</summary>
    private static bool HasGameplayBehaviour(Transform t)
    {
        foreach (Component c in t.GetComponents<Component>())
        {
            if (c == null) return true;   // script perdido: no tocar sus hijos
            if (c is Transform || c is MeshFilter || c is MeshRenderer || c is Collider) continue;
            if (c is MonoBehaviour mb)
            {
                string ns = mb.GetType().Namespace ?? "";
                if (ns.StartsWith("UnityEngine.ProBuilder")) continue;   // mallas de ProBuilder = estáticas
                if (mb.GetType().Name == "BeatSnapHelper") continue;      // (ayuda de editor: no usa sus hijos)
            }
            return true;
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Segmentos
    // ─────────────────────────────────────────────────────────────────────────

    private static float Along(Vector3 p) => Vector3.Dot(p - s_origin, s_fwd);
    private static int   SegOf(Vector3 p) => Mathf.FloorToInt(Along(p) / s_segLen + 1e-4f);

    private static string SegName(int seg)
    {
        int b0 = seg * BeatsPerSegment;
        return seg < 0 ? $"Segmento -{-seg:000} · antes del beat 0"
                       : $"Segmento {seg:000} · beats {b0}-{b0 + BeatsPerSegment - 1}";
    }

    private static int SegFromName(string n)
    {
        var m = System.Text.RegularExpressions.Regex.Match(n, @"Segmento (-?)(\d+)");
        return m.Success ? (m.Groups[1].Value == "-" ? -1 : 1) * int.Parse(m.Groups[2].Value) : 0;
    }

    private static Vector3 FirstPos(Transform t)
    {
        var r = t.GetComponentInChildren<Renderer>(true);
        if (r != null) return r.transform.position;
        return t.childCount > 0 ? t.GetChild(0).position : t.position;
    }

    private static bool SpansSegments(Transform t)
    {
        int s = int.MinValue;
        foreach (Transform d in t.GetComponentsInChildren<Transform>(true))
        {
            if (d == t && IsPureGroup(t)) continue;
            int k = SegOf(d.position);
            if (s == int.MinValue) s = k; else if (k != s) return true;
        }
        return false;
    }

    private static int SegmentCount(Transform t)
    {
        var set = new HashSet<int>();
        foreach (Transform d in t.GetComponentsInChildren<Transform>(true)) set.Add(SegOf(d.position));
        return set.Count;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Movimiento seguro
    // ─────────────────────────────────────────────────────────────────────────

    private static Transform NewGroup(string name, Transform parent)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Crear grupo");
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    /// <summary>Cambia de padre conservando el transform en el mundo; si no se conserva exacto, lo deshace.</summary>
    private static bool Move(Transform t, Transform newParent)
    {
        if (t.parent == newParent) return true;
        Transform oldParent = t.parent;
        int oldIndex = t.GetSiblingIndex();
        Matrix4x4 before = t.localToWorldMatrix;
        Undo.SetTransformParent(t, newParent, "Mover");
        t.SetAsLastSibling();
        if (Close(before, t.localToWorldMatrix)) return true;

        t.SetParent(oldParent, true);
        t.SetSiblingIndex(oldIndex);
        return false;
    }

    private static bool Close(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++)
            if (Mathf.Abs(a[i] - b[i]) > 1e-3f * Mathf.Max(1f, Mathf.Abs(a[i]))) return false;
        return true;
    }

    private static int CatOrder(string c) =>
        c == CatScenery ? 0 : c == CatObstacles ? 1 : c == CatInteractive ? 2 : c == CatCoins ? 3 : 4;

    private static int Depth(Transform t) { int d = 0; while (t.parent != null) { t = t.parent; d++; } return d; }

    private static string HierPath(Transform t) => t.parent == null ? t.name : HierPath(t.parent) + "/" + t.name;
}
