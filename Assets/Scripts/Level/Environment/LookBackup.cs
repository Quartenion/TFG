using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Copia de la iluminación de la escena ANTES de aplicar la estética «Dark Fantasy 80s» (Tools/Estética/…),
/// para poder restaurarla. Solo guarda datos; no hace nada en juego.
/// </summary>
[AddComponentMenu("")]
public class LookBackup : MonoBehaviour
{
    [System.Serializable]
    public struct LightState { public Light light; public float intensity; public Color color; }

    public Material      skybox;
    public AmbientMode   ambientMode;
    public Color         ambientLight, ambientSky, ambientEquator, ambientGround;
    public float         ambientIntensity, reflectionIntensity;
    public bool          fog;
    public FogMode       fogMode;
    public Color         fogColor;
    public float         fogDensity;
    public LightState[]  directionals;

    public void Capture()
    {
        skybox = RenderSettings.skybox;
        ambientMode = RenderSettings.ambientMode;
        ambientLight = RenderSettings.ambientLight;
        ambientSky = RenderSettings.ambientSkyColor;
        ambientEquator = RenderSettings.ambientEquatorColor;
        ambientGround = RenderSettings.ambientGroundColor;
        ambientIntensity = RenderSettings.ambientIntensity;
        reflectionIntensity = RenderSettings.reflectionIntensity;
        fog = RenderSettings.fog;
        fogMode = RenderSettings.fogMode;
        fogColor = RenderSettings.fogColor;
        fogDensity = RenderSettings.fogDensity;

        var list = new System.Collections.Generic.List<LightState>();
        foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Include))
            if (l.type == LightType.Directional)
                list.Add(new LightState { light = l, intensity = l.intensity, color = l.color });
        directionals = list.ToArray();
    }

    public void Restore()
    {
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientLight = ambientLight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.ambientIntensity = ambientIntensity;
        RenderSettings.reflectionIntensity = reflectionIntensity;
        RenderSettings.fog = fog;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;
        if (directionals != null)
            foreach (LightState s in directionals)
                if (s.light != null) { s.light.intensity = s.intensity; s.light.color = s.color; }
    }
}
