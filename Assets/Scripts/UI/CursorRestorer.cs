using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Vuelve a dejar el cursor como estaba (normalmente BLOQUEADO y oculto) al cerrar un menú.
/// En el editor de Unity, pulsar Esc desbloquea y muestra el ratón por su cuenta (mientras la tecla sigue pulsada y
/// justo después), así que se reaplica MIENTRAS Esc esté pulsada y unos instantes más tras soltarla.
/// </summary>
public class CursorRestorer : MonoBehaviour
{
    private CursorLockMode _mode;
    private bool  _visible;
    private float _afterRelease = 0.35f;   // segundos que se sigue reaplicando tras soltar Esc
    private float _maxLife = 3f;

    public static void Restore(CursorLockMode mode, bool visible)
    {
        Apply(mode, visible);
        var go = new GameObject("CursorRestorer") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        var r = go.AddComponent<CursorRestorer>();
        r._mode = mode; r._visible = visible;
    }

    private static void Apply(CursorLockMode mode, bool visible)
    {
        Cursor.lockState = mode;
        Cursor.visible   = visible;
    }

    private void Update()     => Apply(_mode, _visible);

    private void LateUpdate()
    {
        Apply(_mode, _visible);
        _maxLife -= Time.unscaledDeltaTime;
        bool escHeld = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
        if (!escHeld) _afterRelease -= Time.unscaledDeltaTime;
        if (_afterRelease <= 0f || _maxLife <= 0f) Destroy(gameObject);
    }

    private void OnApplicationFocus(bool focus)
    {
        if (focus) Apply(_mode, _visible);
    }
}
