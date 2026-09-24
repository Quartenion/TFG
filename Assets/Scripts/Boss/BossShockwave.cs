using UnityEngine;

/// <summary>
/// Onda expansiva del jefe (Fase 2), generada ÍNTEGRAMENTE POR CÓDIGO (sin prefab ni mallas). Sale de
/// debajo de un puño al estampar y se EXPANDE como un anillo por el suelo, dibujado con un LineRenderer.
///
/// Es LETAL si te pilla con los pies cerca del suelo → hay que SALTAR justo cuando la onda pasa por
/// debajo. Agacharse/slide NO salva (sigues a ras de suelo); SOLO el salto (se comprueba la altura de
/// los pies, no isGrounded).
///
/// Se crea con BossShockwave.Spawn(boss, origin).
/// </summary>
[AddComponentMenu("Boss/Boss Shockwave")]
public class BossShockwave : MonoBehaviour
{
    // Ajustes (constantes; como se crea por código no hay inspector).
    private const float Speed        = 26f;    // m/s de crecimiento del radio
    private const float MaxRadius    = 60f;    // se destruye al llegar
    private const float Thickness    = 2.5f;   // grosor del anillo letal
    private const float JumpClearance = 1.5f;  // altura de salto para librarse
    private const int   Segments     = 48;     // resolución del anillo
    private const float LineWidth    = 0.6f;

    private BossController      _boss;
    private CharacterController _playerCC;
    private LineRenderer _line;
    private Vector3 _origin;
    private float   _radius;
    private float   _speedMul = 1f;   // según la dificultad de la ronda (BossController.WaveSpeedMultiplier)
    private float   _speed;           // m/s de esta onda
    private bool    _dead;

    /// <summary>Crea una onda por código en 'origin' y la inicializa.</summary>
    public static BossShockwave Spawn(BossController boss, Vector3 origin)
    {
        var go = new GameObject("BossShockwave");
        var wave = go.AddComponent<BossShockwave>();
        wave.Setup(boss, origin);
        return wave;
    }

    private void Setup(BossController boss, Vector3 origin)
    {
        _speedMul = boss != null ? Mathf.Max(0.1f, boss.WaveSpeedMultiplier) : 1f;
        _boss = boss;
        if (boss != null && boss.PlayerT != null) _playerCC = boss.PlayerT.GetComponent<CharacterController>();
        _origin = origin;
        transform.position = origin;

        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.loop = true;
        _line.positionCount = Segments;
        _line.widthMultiplier = LineWidth;
        _line.numCapVertices = 2;
        _line.material = new Material(Shader.Find("Sprites/Default"));
        _line.startColor = _line.endColor = new Color(1f, 0.55f, 0.12f, 1f);   // naranja infierno
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;

        _radius = 0f;
        _speed  = Speed * _speedMul;
        RedrawRing();
    }

    private void Update()
    {
        if (_dead) return;
        _radius += _speed * Time.deltaTime;
        RedrawRing();

        // Letal: pies cerca del suelo (no ha saltado) y a la distancia del borde del anillo.
        if (_playerCC != null && _boss != null)
        {
            Vector3 p = _playerCC.transform.position;
            float horiz = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(_origin.x, _origin.z));
            float feetY = _playerCC.bounds.min.y;
            bool onGround = feetY <= _origin.y + JumpClearance;

            if (onGround && Mathf.Abs(horiz - _radius) <= Thickness)
            {
                _dead = true;
                _boss.KillPlayer();
                Destroy(gameObject);
                return;
            }
        }

        if (_radius >= MaxRadius) Destroy(gameObject);
    }

    private void RedrawRing()
    {
        if (_line == null) return;
        // Fundido de la opacidad conforme se agranda (se desvanece al final).
        float fade = Mathf.Clamp01(1f - _radius / MaxRadius);
        Color c = new Color(1f, 0.55f, 0.12f, fade);
        _line.startColor = _line.endColor = c;

        for (int i = 0; i < Segments; i++)
        {
            float a = (i / (float)Segments) * Mathf.PI * 2f;
            Vector3 pt = new Vector3(_origin.x + Mathf.Cos(a) * _radius,
                                     _origin.y + 0.05f,   // mínimo offset para que no parpadee con el suelo
                                     _origin.z + Mathf.Sin(a) * _radius);
            _line.SetPosition(i, pt);
        }
    }
}
