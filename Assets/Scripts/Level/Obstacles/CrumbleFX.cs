using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DESMORONAR un objeto (como las plataformas del jefe): tiembla un momento, se parte en bloques que caen girando y
/// se desintegran, y el objeto original desaparece. Solo visual: los colliders se quitan en cuanto empieza.
///   CrumbleFX.Crumble(pared);                  // con los valores por defecto
///   CrumbleFX.Crumble(pared, settings);        // o con los tuyos
/// El efecto vive en su propio GameObject, así que el objeto original puede desactivarse sin cortarlo.
/// </summary>
public class CrumbleFX : MonoBehaviour
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Tamaño (m) de cada bloque.")]
        public float chunkSize = 0.5f;
        [Tooltip("Máximo de bloques (rendimiento).")]
        public int maxChunks = 500;
        [Tooltip("Segundos que tiembla antes de romperse.")]
        public float shakeTime = 0.35f;
        [Tooltip("Amplitud (m) del temblor.")]
        public float shakeAmplitude = 0.06f;
        [Tooltip("Retraso máximo (s) con el que se sueltan los bloques (los de abajo primero, los de arriba después).")]
        public float maxDelay = 0.5f;
        [Tooltip("Vida (s) de cada bloque: mín / máx. Se desintegran al final.")]
        public Vector2 life = new Vector2(1.2f, 2.4f);
        [Tooltip("Gravedad de los bloques (m/s²).")]
        public float gravity = 22f;
        [Tooltip("Empujón hacia fuera (m/s) al soltarse.")]
        public Vector2 push = new Vector2(0.5f, 2.5f);
        [Tooltip("Sacudida de cámara al romperse (0 = nada).")]
        [Range(0f, 1f)] public float cameraShake = 0.3f;
        [Tooltip("Sonido al romperse (opcional).")]
        public AudioClip sound;
        [Range(0f, 1f)] public float soundVolume = 1f;
    }

    /// <summary>Un bloque suelto (lo usan también las plataformas del jefe).</summary>
    public struct Shard
    {
        public Transform t; public Vector3 vel, spinAxis, scale0;
        public float spin, delay, life, age;
    }

    private GameObject _target;
    private Vector3 _extraVel;   // velocidad extra de todos los bloques (p. ej. hacia donde va el jugador)

    /// <summary>
    /// Cancela el efecto (p. ej. al rebobinar el tiempo): los bloques desaparecen y el objeto vuelve a estar en su
    /// sitio. Reactivar el objeto y sus colliders lo hace quien llama.
    /// </summary>
    public void Cancel()
    {
        foreach (var (t, pos) in _shakeParts) if (t != null) t.position = pos;
        _target = null;
        Destroy(gameObject);
    }
    private Settings _s;
    private float _t;
    private bool _broken;
    private readonly List<(Transform t, Vector3 pos)> _shakeParts = new List<(Transform, Vector3)>();
    private readonly List<Shard> _shards = new List<Shard>();

    /// <summary>Desmorona 'target' (se desactiva al romperse). Devuelve el efecto.</summary>
    public static CrumbleFX Crumble(GameObject target, Settings settings = null, Vector3 extraVelocity = default)
    {
        if (target == null) return null;
        var go = new GameObject("CrumbleFX_" + target.name);
        var fx = go.AddComponent<CrumbleFX>();
        fx._target = target;
        fx._s = settings ?? new Settings();
        fx._extraVel = extraVelocity;
        // Fuera colliders YA: se puede pasar (y ya no se detecta la mirada).
        foreach (Collider c in target.GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
            fx._shakeParts.Add((r.transform, r.transform.position));
        return fx;
    }

    private void Update()
    {
        _t += Time.deltaTime;

        if (!_broken)
        {
            // 1) Tiembla, cada vez más.
            float k = _s.shakeTime > 0f ? Mathf.Clamp01(_t / _s.shakeTime) : 1f;
            float amp = _s.shakeAmplitude * k;
            foreach (var (t, pos) in _shakeParts)
                if (t != null) t.position = pos + Random.insideUnitSphere * amp;
            if (_t < _s.shakeTime) return;

            // 2) Se rompe en bloques.
            foreach (var (t, pos) in _shakeParts) if (t != null) t.position = pos;
            Shatter();
            _broken = true;
            if (_target != null) _target.SetActive(false);
            if (_s.cameraShake > 0f) CameraShaker.Instance?.ShakeAt(_s.cameraShake, transform.position);
            if (_s.sound != null) AudioSource.PlayClipAtPoint(_s.sound, transform.position, _s.soundVolume);
        }

        // 3) Los bloques caen girando y se desintegran.
        StepShards(_shards, _s.gravity, Time.deltaTime, 0.02f);
        if (_broken && _shards.Count == 0) Destroy(gameObject);
    }

    /// <summary>
    /// Avanza 'dt' segundos una lista de bloques: mientras no se sueltan (delay) tiemblan con amplitud 'holdJitter';
    /// después caen con 'gravity', giran y se desintegran en el último 60 % de su vida. Los terminados se destruyen y
    /// se quitan de la lista. Compartido con BossPlatformSpawner.
    /// </summary>
    public static void StepShards(List<Shard> shards, float gravity, float dt, float holdJitter)
    {
        for (int i = shards.Count - 1; i >= 0; i--)
        {
            Shard sh = shards[i];
            if (sh.t == null) { shards.RemoveAt(i); continue; }
            sh.age += dt;
            float live = sh.age - sh.delay;
            if (live > 0f)
            {
                sh.vel += Vector3.down * gravity * dt;
                sh.t.position += sh.vel * dt;
                sh.t.rotation  = Quaternion.AngleAxis(sh.spin * dt, sh.spinAxis) * sh.t.rotation;
                float k = Mathf.Clamp01(live / sh.life);
                float shrink = k < 0.4f ? 1f : 1f - (k - 0.4f) / 0.6f;     // se desintegra en el último 60 %
                sh.t.localScale = sh.scale0 * Mathf.Max(0f, shrink);
                if (k >= 1f) { Destroy(sh.t.gameObject); shards.RemoveAt(i); continue; }
            }
            else sh.t.position += Random.insideUnitSphere * holdJitter;   // aún aguanta: tiembla
            shards[i] = sh;
        }
    }

    /// <summary>Rellena cada malla del objeto con bloques en SU espacio local (sirve para paredes giradas).</summary>
    private void Shatter()
    {
        if (_target == null) return;
        int budget = Mathf.Max(1, _s.maxChunks);
        float cs = Mathf.Max(0.1f, _s.chunkSize);

        Bounds all = new Bounds(_target.transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer r in _target.GetComponentsInChildren<Renderer>())
        {
            if (first) { all = r.bounds; first = false; } else all.Encapsulate(r.bounds);
        }
        transform.position = all.center;

        foreach (Renderer r in _target.GetComponentsInChildren<Renderer>())
        {
            if (budget <= 0 || !r.enabled) continue;
            var mf = r.GetComponent<MeshFilter>();
            Transform rt = r.transform;
            Bounds lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds
                                                           : new Bounds(rt.InverseTransformPoint(r.bounds.center), Vector3.one);
            Vector3 ls = rt.lossyScale;
            Vector3 world = Vector3.Scale(lb.size, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
            int nx = Mathf.Clamp(Mathf.RoundToInt(world.x / cs), 1, 40);
            int ny = Mathf.Clamp(Mathf.RoundToInt(world.y / cs), 1, 40);
            int nz = Mathf.Clamp(Mathf.RoundToInt(world.z / cs), 1, 40);
            // Si pasa del presupuesto, bloques más grandes.
            while (nx * ny * nz > budget && (nx > 1 || ny > 1 || nz > 1))
            { nx = Mathf.Max(1, nx - 1); ny = Mathf.Max(1, ny - 1); nz = Mathf.Max(1, nz - 1); }

            Vector3 cellL = new Vector3(lb.size.x / nx, lb.size.y / ny, lb.size.z / nz);
            Vector3 cellW = Vector3.Scale(cellL, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
            Material mat = r.sharedMaterial;

            for (int y = 0; y < ny && budget > 0; y++)
                for (int x = 0; x < nx && budget > 0; x++)
                    for (int z = 0; z < nz && budget > 0; z++)
                    {
                        budget--;
                        Vector3 lp = lb.min + new Vector3((x + 0.5f) * cellL.x, (y + 0.5f) * cellL.y, (z + 0.5f) * cellL.z);
                        GameObject c = RuntimeMaterials.Primitive(PrimitiveType.Cube);
                        Destroy(c.GetComponent<Collider>());
                        c.name = "WallShard";
                        var cr = c.GetComponent<Renderer>();
                        if (mat != null) cr.sharedMaterial = mat;
                        cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        c.transform.SetParent(transform, true);
                        c.transform.SetPositionAndRotation(rt.TransformPoint(lp), rt.rotation);
                        Vector3 sc = cellW * Random.Range(0.85f, 1f);
                        c.transform.localScale = sc;

                        Vector3 outDir = c.transform.position - all.center; outDir.y = 0f;
                        if (outDir.sqrMagnitude < 0.0001f) outDir = Random.insideUnitSphere;
                        float h01 = ny > 1 ? y / (float)(ny - 1) : 0f;   // 0 = abajo, 1 = arriba
                        _shards.Add(new Shard
                        {
                            t = c.transform,
                            vel = outDir.normalized * Random.Range(_s.push.x, _s.push.y)
                                  + _extraVel * Random.Range(0.6f, 1.2f),
                            spinAxis = Random.onUnitSphere,
                            spin = Random.Range(40f, 240f),
                            // Se desmorona de abajo arriba: la base cede primero y el resto se viene abajo.
                            delay = h01 * _s.maxDelay * 0.7f + Random.Range(0f, _s.maxDelay * 0.3f),
                            life = Random.Range(_s.life.x, _s.life.y),
                            scale0 = sc,
                        });
                    }
        }
    }
}
