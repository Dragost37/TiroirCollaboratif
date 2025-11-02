using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class CustomRingControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Ring Definitions (Local UI Units)")]
    [Tooltip("The radius of the innermost (X) ring")]
    public float xRingRadius = 75f;
    [Tooltip("The radius of the middle (Y) ring")]
    public float yRingRadius = 125f;
    [Tooltip("The radius of the outermost (Z) ring")]
    public float zRingRadius = 175f;

    [Header("Control Settings")]
    [Tooltip("How sensitive the drag-to-rotate motion is")]
    public float rotationSensitivity = 1.0f;

    private Transform targetObject;
    private RectTransform rectTransform;
    private Camera pressEventCamera;

    private enum Axis { None, X, Y, Z }
    private Axis activeRing = Axis.None;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    public void SetTarget(Transform target)
    {
        targetObject = target;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressEventCamera = eventData.pressEventCamera;

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            eventData.position,
            pressEventCamera,
            out localPoint);

        float distance = localPoint.magnitude;

        if (distance <= xRingRadius)
        {
            activeRing = Axis.X;
        }
        else if (distance <= yRingRadius)
        {
            activeRing = Axis.Y;
        }
        else if (distance <= zRingRadius)
        {
            activeRing = Axis.Z;
        }
        else
        {
            activeRing = Axis.None;
        }
        Debug.Log($"Clicked at local distance: {distance}. Detected ring: {activeRing}");
    }




    public void OnDrag(PointerEventData eventData)
    {
        if (activeRing == Axis.None || targetObject == null)
        {
            return;
        }

        Vector2 prevLocalPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            eventData.position - eventData.delta,
            pressEventCamera,
            out prevLocalPoint);

        Vector2 currentLocalPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            eventData.position,
            pressEventCamera,
            out currentLocalPoint);

        float deltaAngle = Vector2.SignedAngle(prevLocalPoint, currentLocalPoint);
        float rotationAmount = deltaAngle * rotationSensitivity;

        switch (activeRing)
        {
            case Axis.X:

                targetObject.Rotate(rotationAmount, 0, 0, Space.World);
                break;
            case Axis.Y:

                targetObject.Rotate(0, rotationAmount, 0, Space.World);
                break;
            case Axis.Z:

                targetObject.Rotate(0, 0, rotationAmount, Space.World);
                break;
        }
    }




    public void OnPointerUp(PointerEventData eventData)
    {
        activeRing = Axis.None;
    }
}
