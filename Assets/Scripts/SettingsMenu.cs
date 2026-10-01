using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PANEL DE AJUSTES compartido (prefab Assets/Prefabs/SettingsMenu.prefab): el MISMO en el menú principal y en la pausa.
///   Título · Volumen · Sensibilidad · Idioma · Volver.
/// Se conecta solo (volumen/sensibilidad → GameSettings; idioma → LanguageButton). Quien lo usa pone:
///   • onBack  : qué hacer al pulsar VOLVER (o Esc).
///   • Preview : BPM del metrónomo de prueba del volumen (y, si hay música sonando, a qué AudioSource sincronizarlo).
/// </summary>
[AddComponentMenu("UI/Settings Menu")]
public class SettingsMenu : MonoBehaviour
{
    public Slider volume;
    public Slider sensitivity;
    public Button back;

    /// <summary>Al pulsar VOLVER.</summary>
    public Action onBack;

    private VolumePreview _preview;
    private bool _bound;

    /// <summary>Metrónomo que suena al mover el volumen (ajusta su BPM / sincronización desde fuera).</summary>
    public VolumePreview Preview => _preview ??= new VolumePreview(gameObject);

    private void Awake() => Bind();

    private void Bind()
    {
        if (_bound) return;
        _bound = true;
        if (volume == null)      volume      = transform.Find("Volume")?.GetComponent<Slider>();
        if (sensitivity == null) sensitivity = transform.Find("Sensitivity")?.GetComponent<Slider>();
        if (back == null)        back        = transform.Find("BackButton")?.GetComponent<Button>();
        GameSettings.BindSliders(volume, sensitivity, Preview);
        if (back != null) back.onClick.AddListener(() => onBack?.Invoke());
    }

    private void OnEnable()
    {
        Bind();
        // Al abrir: valores guardados (por si se cambiaron desde el otro menú).
        if (volume != null)      volume.SetValueWithoutNotify(GameSettings.Volume);
        if (sensitivity != null) sensitivity.SetValueWithoutNotify(GameSettings.Sensitivity);
    }

    private void Update() => _preview?.Update();

    private void OnDisable() => PlayerPrefs.Save();
}
