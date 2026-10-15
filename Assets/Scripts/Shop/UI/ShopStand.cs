using System;
using UnityEngine;

/// <summary>
/// TIENDA FÍSICA del área de freeroam (antes de empezar cada nivel): el jugador se acerca, la MIRA (raycast desde
/// la cámara, como la puerta de inicio) y pulsa E → se abre la tienda. Mientras está abierta el jugador se queda
/// quieto; al cerrarla (Esc / Volver) sigue explorando.
/// La lógica común (mirada, E, bloque provisional, aviso) está en FreeroamStand.
///
/// SETUP:
///   1. Pon este componente en el objeto de la tienda (mostrador, puesto, NPC…).
///   2. Necesita Collider(s) para que la mirada lo detecte: los de este objeto o sus hijos (o asígnalos a mano).
///      Si el objeto está VACÍO (sin mallas ni colliders), se genera un BLOQUE VERDE por código. Para usar un modelo,
///      mete tu prefab como hijo (o pon el componente en el prefab): entonces no se genera nada.
///   3. Por defecto solo funciona en FREEROAM (desactívalo en "Only In Freeroam" para probar en otro momento).
/// </summary>
[AddComponentMenu("Level/Shop Stand")]
public class ShopStand : FreeroamStand
{
    private void Reset()
    {
        _promptText   = "[ E ]  Tienda";
        _promptTextEn = "[ E ]  Shop";
        _blockColor   = new Color(0.2f, 0.7f, 0.25f);   // verde
    }

    protected override string LabelEs    => "TIENDA";
    protected override string LabelEn    => "SHOP";
    protected override Color  LabelColor => new Color(0.45f, 1f, 0.5f);

    protected override float PromptWidth => 300f;
    protected override Color PromptColor => new Color(1f, 0.85f, 0.3f);

    protected override string PromptLine() =>
        Loc.T($"{_promptText}   ·   {PlayerProgress.Coins} monedas", $"{_promptTextEn}   ·   {PlayerProgress.Coins} coins");

    protected override void OpenPanel(Action onClose) => ShopUI.Open(onClose);

    protected override Color  GizmoColor => new Color(1f, 0.8f, 0.1f, 0.5f);
    protected override string GizmoLabel => "TIENDA  (E)";
}
