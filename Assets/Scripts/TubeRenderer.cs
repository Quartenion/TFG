using UnityEngine;

/// <summary>
/// Renderiza una cuerda de zipline como un tubo 3D procedural.
///
/// SETUP:
///   1. Crea un GameObject vacio hijo de la Zipline, llamalo "RopeMesh".
///   2. Añadele este componente (anade MeshFilter y MeshRenderer automaticamente).
///   3. Asigna el material de la cuerda al MeshRenderer.
///   4. Arrastra el componente Zipline al campo _zipline.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TubeRenderer : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Zipline a renderizar.")]
    [SerializeField] private Zipline _zipline;

    [Header("Geometria del tubo")]
    [Tooltip("Radio del tubo en metros. 0.03-0.06 para una cuerda.")]
    [SerializeField] private float _radius = 0.04f;

    [Tooltip("Numero de caras laterales. 6 = hexagonal, 8 = octagonal. Mas = mas redondo pero mas vertices.")]
    [SerializeField] [Range(4, 16)] private int _radialSegments = 8;

    [Tooltip("Divisiones a lo largo de la cuerda. 1 = recto. Mas divisiones permiten curvas (futuro).")]
    [SerializeField] [Range(1, 8)] private int _lengthSegments = 1;

    [Header("Textura")]
    [Tooltip("Cuantas veces se repite la textura a lo largo de la cuerda por metro.")]
    [SerializeField] private float _uvTilingPerMeter = 1.5f;

    [Tooltip("Giro adicional en grados para alinear la textura con la direccion de la cuerda.")]
    [SerializeField] private float _uvRotationOffset = 0f;

    // --- Internals ---
    private Mesh        _mesh;
    private MeshFilter  _filter;
    private Vector3[]   _vertices;
    private Vector3[]   _normals;
    private Vector2[]   _uvs;
    private int[]       _triangles;
    private bool        _initialized;

    private void Awake()
    {
        _filter      = GetComponent<MeshFilter>();
        _mesh        = new Mesh { name = "RopeTube" };
        _filter.mesh = _mesh;
        _initialized = true;
    }

    private void LateUpdate()
    {
        if (!_initialized) return;

        if (_zipline != null)
        {
            if (_zipline.startPoint == null || _zipline.endPoint == null)
            {
                _mesh.Clear();
                return;
            }
            BuildTube(_zipline.StartPos, _zipline.EndPos);
        }
        else
        {
            _mesh.Clear();
        }
    }

    private void OnDisable()
    {
        if (_mesh != null) _mesh.Clear();
    }

    // -------------------------------------------------------------------------
    // Generacion del tubo
    // -------------------------------------------------------------------------

    private void BuildTube(Vector3 start, Vector3 end)
    {
        Vector3 dir    = end - start;
        float   length = dir.magnitude;
        if (length < 0.001f) { _mesh.Clear(); return; }

        Vector3 axis = dir / length;

        // Base ortonormal: eje derecho y eje arriba perpendiculares al eje de la cuerda
        Vector3 worldUp = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.99f
                          ? Vector3.forward
                          : Vector3.up;
        Vector3 right = Vector3.Cross(axis, worldUp).normalized;
        Vector3 up    = Vector3.Cross(right, axis).normalized;

        int rings    = _lengthSegments + 1;           // anillos de vertices (extremos + intermedios)
        int vertsPerRing = _radialSegments + 1;       // +1 para cerrar la costura UV sin artefactos
        int totalVerts   = rings * vertsPerRing;

        if (_vertices == null || _vertices.Length != totalVerts)
        {
            _vertices  = new Vector3[totalVerts];
            _normals   = new Vector3[totalVerts];
            _uvs       = new Vector2[totalVerts];
            _triangles = new int[_radialSegments * _lengthSegments * 6];
        }

        float uvLength = length * _uvTilingPerMeter;

        // Generar vertices.
        // Los vertices del Mesh estan en local space del GameObject, pero los puntos
        // de la cuerda vienen en world space. InverseTransformPoint/Direction convierte.
        for (int ring = 0; ring < rings; ring++)
        {
            float t  = (float)ring / _lengthSegments;
            Vector3 centerWorld = start + axis * (length * t);

            for (int seg = 0; seg < vertsPerRing; seg++)
            {
                float angle = (seg / (float)_radialSegments) * Mathf.PI * 2f
                              + _uvRotationOffset * Mathf.Deg2Rad;
                Vector3 normalWorld = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                Vector3 vertWorld   = centerWorld + normalWorld * _radius;
                int idx = ring * vertsPerRing + seg;

                _vertices[idx] = transform.InverseTransformPoint(vertWorld);
                _normals[idx]  = transform.InverseTransformDirection(normalWorld);
                _uvs[idx]      = new Vector2((float)seg / _radialSegments, t * uvLength);
            }
        }

        // Generar triangulos
        int triIdx = 0;
        for (int ring = 0; ring < _lengthSegments; ring++)
        {
            for (int seg = 0; seg < _radialSegments; seg++)
            {
                int a = ring       * vertsPerRing + seg;
                int b = ring       * vertsPerRing + seg + 1;
                int c = (ring + 1) * vertsPerRing + seg;
                int d = (ring + 1) * vertsPerRing + seg + 1;

                // Triangulo 1
                _triangles[triIdx++] = a;
                _triangles[triIdx++] = c;
                _triangles[triIdx++] = b;

                // Triangulo 2
                _triangles[triIdx++] = b;
                _triangles[triIdx++] = c;
                _triangles[triIdx++] = d;
            }
        }

        // Aplicar al mesh
        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetNormals(_normals);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.RecalculateBounds();
    }
}
