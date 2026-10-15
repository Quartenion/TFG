using System;
using UnityEngine;

/// <summary>
/// PUESTO DE CONTRATOS físico del área de freeroam (como la tienda): el jugador lo MIRA y pulsa E → se abre la
/// pantalla de contratos (ContractUI). Mientras está abierta el jugador se queda quieto.
/// La lógica común (mirada, E, bloque provisional, aviso) está en FreeroamStand.
///
/// SETUP:
///   1. Pon este componente en el objeto del puesto (tablón, mesa, NPC…).
///   2. Necesita Collider(s) para detectar la mirada: los de este objeto o sus hijos (o asígnalos a mano).
///      Si el objeto está VACÍO (sin mallas ni colliders), se genera un BLOQUE ROJO por código. Para usar un modelo,
///      mete tu prefab como hijo (o pon el componente en el prefab): entonces no se genera nada.
///   3. Por defecto solo funciona en FREEROAM.
/// </summary>
[AddComponentMenu("Level/Contract Stand")]
public class ContractStand : FreeroamStand
{
    private void Reset()
    {
        _promptText   = "[ E ]  Contratos";
        _promptTextEn = "[ E ]  Contracts";
        _blockColor   = new Color(0.75f, 0.15f, 0.15f);   // rojo
    }

    protected override string LabelEs    => "CONTRATOS";
    protected override string LabelEn    => "CONTRACTS";
    protected override Color  LabelColor => new Color(1f, 0.45f, 0.4f);

    protected override float PromptWidth => 460f;
    protected override Color PromptColor => new Color(0.93f, 0.86f, 0.7f);

    protected override string PromptLine()
    {
        int active = ContractCatalog.ActiveHere().Count;   // (firmados de este nivel)
        int toClaim = 0;
        foreach (ContractDef c in ContractCatalog.All) if (PlayerProgress.IsContractClaimable(c.id)) toClaim++;
        string extra = toClaim > 0 ? Loc.T($"   ·   ¡{toClaim} por cobrar!", $"   ·   {toClaim} to claim!") : "";
        return Loc.T($"{_promptText}   ·   firmados {active}/{ContractCatalog.MaxActive}{extra}",
                     $"{_promptTextEn}   ·   signed {active}/{ContractCatalog.MaxActive}{extra}");
    }

    protected override void OpenPanel(Action onClose) => ContractUI.Open(onClose);

    protected override Color  GizmoColor => new Color(0.3f, 0.8f, 0.4f, 0.5f);
    protected override string GizmoLabel => "CONTRATOS  (E)";
}
