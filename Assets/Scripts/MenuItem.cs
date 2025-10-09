using UnityEngine;
using UnityEngine.EventSystems;

public class MenuItem : MonoBehaviour, IPointerClickHandler
{
    private static MenuItem selectedItem = null;
    public GameObject radialMenu;
    
    public void OnPointerClick(PointerEventData eventData)
    {
        SelectItem();
    }
    
    private void SelectItem()
    {
        if(selectedItem != null)
        {
            selectedItem.DeselectItem();
        }
        selectedItem = this;
        Debug.Log("Menu item selected: " + gameObject.name);
        radialMenu.SetActive(false);
    }
    private void DeselectItem()
    {
        if(selectedItem == this)
        {
            selectedItem = null;
            Debug.Log("Menu item deselected: " + gameObject.name);
        }
    }
}
