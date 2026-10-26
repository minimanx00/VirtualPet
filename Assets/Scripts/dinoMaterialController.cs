using UnityEngine;

public class dinoMaterialController : MonoBehaviour
{
    public Material material;

    public void SetColor(Color color)
    {
        if (material != null)
            material.SetColor("_BaseColor", color);
    }

    public void SetWetness(float wetness)
    {
        if (material != null)
            material.SetFloat("_Smoothness", Mathf.Clamp01(wetness));
    }
}
