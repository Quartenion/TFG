using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// JERINGUILLAS 3D DEL HUD: independientes de la resolución.
///
/// Las jeringuillas son modelos 3D que graban cámaras ortográficas en una RenderTexture (que luego enseña una
/// RawImage). Esos modelos y cámaras estaban DENTRO del Canvas «Screen Space - Overlay», y ese Canvas se escala con
/// la resolución de la pantalla (CanvasScaler): con la ventana pequeña del editor el modelo salía pequeño y cabía
/// entero, pero en el juego exportado (pantalla completa, más píxeles) el modelo crece mientras el tamaño de la
/// cámara ortográfica sigue igual → la jeringuilla sale ampliada, descolocada y la AGUJA SE QUEDA FUERA de la imagen.
///
/// Arreglo (automático al cargar cada escena): los objetos 3D que cuelgan del Canvas (los que no son de UI) se sacan
/// a un objeto aparte con escala 1, y cada cámara que graba en una textura se encuadra sola sobre lo que ve
/// (la jeringuilla entera, con margen para el latido). Así se ve igual a cualquier resolución.
/// </summary>
public static class HudModelRig
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) Fix(c, scene);
    }

    /// <summary>Nombre del grupo del Canvas que reúne todo lo de las jeringuillas (modelos, cámaras, luces, volúmenes…).</summary>
    public const string SyringeGroup = "Jeringuillas";

    /// <summary>
    /// Hijos directos del Canvas, más los del grupo «Jeringuillas» si existe (esté donde esté dentro del Canvas,
    /// p. ej. dentro de «HUD»), en su orden.
    /// </summary>
    private static System.Collections.Generic.List<Transform> Candidates(Transform ct)
    {
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform ch in ct) list.Add(ch);
        Transform group = FindDeep(ct, SyringeGroup);
        if (group != null) foreach (Transform g in group) list.Add(g);
        return list;
    }

    private static Transform FindDeep(Transform t, string name)
    {
        foreach (Transform ch in t)
        {
            if (ch.name == name && ch is RectTransform) return ch;
            Transform r = FindDeep(ch, name);
            if (r != null) return r;
        }
        return null;
    }

    private static void Fix(Canvas canvas, Scene scene)
    {
        Transform ct = canvas.transform;
        var candidates = Candidates(ct);
        bool hasRtCamera = false;
        foreach (Transform ch in candidates)
        {
            var cam = ch.GetComponent<Camera>();
            if (cam != null && cam.targetTexture != null) { hasRtCamera = true; break; }
        }
        if (!hasRtCamera) return;

        // 1) Sacar los objetos 3D (Transform normal, no RectTransform) a un «rig» con escala 1.
        var rig = new GameObject("HUD 3D (" + canvas.name + ")").transform;
        SceneManager.MoveGameObjectToScene(rig.gameObject, scene);
        rig.position = new Vector3(0f, -10000f, 0f);
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            Transform ch = candidates[i];
            if (ch is RectTransform) continue;
            // Solo lo que se ve en las texturas (modelos, cámaras, luces); los Volume de efectos se quedan.
            if (ch.GetComponentInChildren<Renderer>(true) == null && ch.GetComponentInChildren<Camera>(true) == null
                && ch.GetComponentInChildren<Light>(true) == null) continue;
            ch.SetParent(rig, false);   // conserva posición/giro/escala LOCALES (sin la escala del Canvas)
        }

        // 2) Encuadrar cada cámara sobre lo que graba.
        Renderer[] all = rig.GetComponentsInChildren<Renderer>(true);
        foreach (Camera cam in rig.GetComponentsInChildren<Camera>(true))
        {
            if (cam.targetTexture == null || !cam.orthographic) continue;
            Frame(cam, all);
        }
    }

    private static void Frame(Camera cam, Renderer[] renderers)
    {
        Transform t = cam.transform;
        bool any = false;
        float minX = 0, maxX = 0, minY = 0, maxY = 0;
        Vector3 centerSum = Vector3.zero; int n = 0;
        Transform root = null;   // la jeringuilla (hijo directo del rig): su origen es el centro del latido

        void Add(Bounds b, Vector3 shift)
        {
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                Vector3 l = t.InverseTransformPoint(p + shift);   // espacio de la cámara (X = derecha, Y = arriba)
                if (!any) { minX = maxX = l.x; minY = maxY = l.y; any = true; }
                else { minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x); minY = Mathf.Min(minY, l.y); maxY = Mathf.Max(maxY, l.y); }
            }
        }

        foreach (Renderer r in renderers)
        {
            if (r == null || (cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
            if (r is ParticleSystemRenderer) continue;
            Bounds b = r.bounds;
            Add(b, Vector3.zero);
            centerSum += b.center; n++;

            Transform branch = r.transform;
            while (branch.parent != null && branch.parent.parent != null) branch = branch.parent;
            if (root == null) root = branch;

            // Émbolo: también donde estará con la jeringuilla LLENA (sale por el final).
            foreach (LiquidLevel lq in branch.GetComponentsInChildren<LiquidLevel>(true))
                if (lq.Plunger != null && lq.PlungerAt100 != null && r.transform.IsChildOf(lq.Plunger))
                    Add(b, lq.PlungerAt100.position - lq.Plunger.position);
        }
        if (!any) return;

        // Abajo: la punta de la aguja justo en el borde (así toca el monitor). Arriba: el émbolo con la jeringuilla
        // llena + lo que crece con el latido (×1.15 desde el origen de la jeringuilla) + un poco de aire.
        float originY = root != null ? t.InverseTransformPoint(root.position).y : (minY + maxY) * 0.5f;
        float pulse = 1.15f;
        float bottom = minY;
        float top = originY + (maxY - originY) * pulse;
        top += (top - bottom) * 0.02f;

        float aspect = cam.targetTexture.height > 0 ? (float)cam.targetTexture.width / cam.targetTexture.height : 1f;
        float halfW = Mathf.Max(maxX - minX, 1e-3f) * 0.5f * pulse * 1.1f;   // (gira sobre su eje: margen)
        float size = Mathf.Max((top - bottom) * 0.5f, halfW / Mathf.Max(aspect, 1e-3f));
        float centerY = bottom + size;   // si hay que agrandar por el ancho, la aguja sigue pegada al borde

        Vector3 centerLocal = new Vector3((minX + maxX) * 0.5f, centerY, 0f);
        Vector3 center = t.TransformPoint(centerLocal);
        // Mover la cámara en su plano y en profundidad, a una distancia cómoda delante del modelo.
        Vector3 objCenter = centerSum / Mathf.Max(n, 1);
        float depth = Vector3.Dot(objCenter - t.position, t.forward);
        t.position = center + t.forward * (depth - 20f);
        cam.orthographicSize = size;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 60f;
    }
}
