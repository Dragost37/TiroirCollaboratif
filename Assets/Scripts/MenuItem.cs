using UnityEngine;
using UnityEngine.EventSystems;

public class MenuItem : MonoBehaviour, IPointerClickHandler
{
    private static MenuItem selectedItem = null;
    public GameObject radialMenu;
    public ObjectCreator objectCreator;
    
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
        if(gameObject.name == "Item - Clou")
        {
            objectCreator.CreateNailObject();
        }else if(gameObject.name == "Item - Vis")
        {
            objectCreator.CreateScrewObject();
        }else if(gameObject.name == "Item - Bois")
        {
            // Ajouter la logique pour créer un écrou
            Debug.Log("Create Bois Object - Not Implemented");
        }
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
