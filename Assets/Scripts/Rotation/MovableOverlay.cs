using UnityEngine;
using UnityEngine.EventSystems;

public class DraggableWindow : MonoBehaviour, IDragHandler
{
    [SerializeField] private RectTransform dragRectTransform;

    private void Awake()
    {
        if (dragRectTransform == null)
        {
            dragRectTransform = GetComponent<RectTransform>();
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        dragRectTransform.anchoredPosition += eventData.delta / transform.root.localScale.x;
    }
}
