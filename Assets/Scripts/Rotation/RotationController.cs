using UnityEngine;
using UnityEngine.UI;

public class RotationController : MonoBehaviour
{
    [Header("UI Controls")]
    [Tooltip("Assign your CustomRingControl UI element here")]
    public CustomRingControl ringControl;

    [Tooltip("Assign your new Scale Slider UI element here")]
    public Slider scaleSlider;

    [Header("Scale Settings")]
    [Tooltip("The minimum scale the object can be")]
    public float minScale = 0.1f;
    [Tooltip("The maximum scale the object can be")]
    public float maxScale = 3.0f;

    private Transform targetObject;
    private bool isUpdatingScaleSlider = false;

    public void SetTargetObject(Transform target)
    {
        targetObject = target;
        if (ringControl != null)
        {
            ringControl.SetTarget(targetObject);
        }
        else
        {
            Debug.LogError("RingControl is not assigned in the RotationController inspector!");
        }

        if (scaleSlider != null)
        {
            InitializeScaleSlider();
        }
        else
        {
            Debug.LogError("ScaleSlider is not assigned in the RotationController inspector!");
        }
    }

    private void InitializeScaleSlider()
    {

        scaleSlider.minValue = minScale;
        scaleSlider.maxValue = maxScale;
        isUpdatingScaleSlider = true;
        scaleSlider.value = targetObject.localScale.x;
        isUpdatingScaleSlider = false;
        scaleSlider.onValueChanged.AddListener(UpdateScale);
    }

    public void UpdateScale(float newScale)
    {
        if (targetObject == null || isUpdatingScaleSlider)
        {
            return;
        }
        targetObject.localScale = new Vector3(newScale, newScale, newScale);
    }

    public void ClosePanel()
    {
        Destroy(gameObject);
    }
}
