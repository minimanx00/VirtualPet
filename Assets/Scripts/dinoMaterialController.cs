using UnityEngine;

[ExecuteAlways]
public class dinoMaterialController : MonoBehaviour
{
    public Material material;

    public float hueVariance = 0;
    public float wetness = 0;

    private Material originalMaterial;
    private Color originalColor;

    private void Update()
    {
        if (material != originalMaterial)
        {
            RestoreOriginalColor();
            originalMaterial = material;
            if (material != null)
                originalColor = material.color;
        }

        if (material == null)
            return;

        Color.RGBToHSV(originalColor, out float hue, out float saturation, out float value);
        Color color = Color.HSVToRGB(Mathf.Repeat(hue + hueVariance, 1f), saturation, value, true);
        color.a = originalColor.a;
        material.color = color;
    }

    private void OnDisable()
    {
        RestoreOriginalColor();
    }

    private void RestoreOriginalColor()
    {
        if (originalMaterial != null)
            originalMaterial.color = originalColor;

        originalMaterial = null;
    }
}
