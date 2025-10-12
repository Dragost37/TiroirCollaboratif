using UnityEngine;
using UnityEngine.UI;

[ExecuteInEditMode]
public class MenuSelector : MonoBehaviour
{
    public Button[] buttons;
    public float radius = 250f;
    public Vector2 centerOffset = Vector2.zero;

    void Start()
    {
        ArrangeButtonsInCircle();
    }

    void OnValidate()
    {
        ArrangeButtonsInCircle();
    }

    void ArrangeButtonsInCircle()
    {
        if(buttons == null || buttons.Length == 0)
        return;

        float angleIncrement = 360f / buttons.Length;
        
        for(int i = 0; i < buttons.Length; i++)
        {
            if(buttons[i] == null)
            continue;
            
            float angle = i * angleIncrement;
            
            float radians = angle * Mathf.Deg2Rad;
            
            float x = Mathf.Sin(radians) * radius;
            float y = Mathf.Cos(radians) * radius;
            
            RectTransform buttontransform = buttons[i].GetComponent<RectTransform>();
            
            if(buttontransform != null)
            {
                buttontransform.anchoredPosition = new Vector2(x, y) + centerOffset;
                
                buttontransform.localEulerAngles = new Vector3(0, 0, -angle);
                
                foreach(RectTransform child in buttontransform)
                {
                    child.localEulerAngles = new Vector3(0, 0, angle);
                }
            }
        }

    }
}
