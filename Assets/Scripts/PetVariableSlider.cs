using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a debug slider and label in sync with a pet variable in both directions.
/// </summary>
[RequireComponent(typeof(Slider))]
public class PetVariableSlider : MonoBehaviour
{
    [SerializeField] private PetVariable petVariable;
    [SerializeField] private TMP_Text valueLabel;

    private Slider slider;

    private void OnEnable()
    {
        slider = GetComponent<Slider>();
        if (petVariable == null)
            return;

        slider.minValue = petVariable.minValue;
        slider.maxValue = petVariable.maxValue;
        slider.onValueChanged.AddListener(petVariable.SetValue);
        petVariable.onValueChange.AddListener(RefreshValue);
        RefreshValue(petVariable.value);
    }

    private void OnDisable()
    {
        if (petVariable == null)
            return;

        slider.onValueChanged.RemoveListener(petVariable.SetValue);
        petVariable.onValueChange.RemoveListener(RefreshValue);
    }

    private void RefreshValue(float value)
    {
        // Stat changes update the UI without writing the value back to the pet.
        slider.SetValueWithoutNotify(value);
        if (valueLabel != null)
            valueLabel.text = $"{petVariable.name}: {value:0.0}";
    }
}
