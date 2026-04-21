using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class RoomMoodController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ThemeIntentController themeIntentController;
    [SerializeField] private Light mainDirectionalLight;

    private void Reset()
    {
        themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        mainDirectionalLight = FindDirectionalLight();
    }

    private void Awake()
    {
        if (themeIntentController == null)
        {
            themeIntentController = FindAnyObjectByType<ThemeIntentController>();
        }

        if (mainDirectionalLight == null)
        {
            mainDirectionalLight = FindDirectionalLight();
        }
    }

    private void OnEnable()
    {
        if (themeIntentController == null)
        {
            return;
        }

        themeIntentController.ThemeChanged -= HandleThemeChanged;
        themeIntentController.ThemeChanged += HandleThemeChanged;

        if (themeIntentController.ActiveTheme != null)
        {
            ApplyTheme(themeIntentController.ActiveTheme);
        }
    }

    private void OnDisable()
    {
        if (themeIntentController == null)
        {
            return;
        }

        themeIntentController.ThemeChanged -= HandleThemeChanged;
    }

    private void HandleThemeChanged(ThemeProfile theme)
    {
        ApplyTheme(theme);
    }

    private void ApplyTheme(ThemeProfile theme)
    {
        if (theme == null)
        {
            return;
        }

        if (mainDirectionalLight != null)
        {
            mainDirectionalLight.color = theme.Mood.MainLightColor;
            mainDirectionalLight.intensity = theme.Mood.MainLightIntensity;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = theme.Mood.AmbientSkyColor;
        RenderSettings.ambientEquatorColor = theme.Mood.AmbientEquatorColor;
        RenderSettings.ambientGroundColor = theme.Mood.AmbientGroundColor;
    }

    private static Light FindDirectionalLight()
    {
        var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        foreach (var lightComponent in lights)
        {
            if (lightComponent.type == LightType.Directional)
            {
                return lightComponent;
            }
        }

        return null;
    }
}
