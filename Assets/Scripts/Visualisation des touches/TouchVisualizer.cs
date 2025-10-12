using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class TouchVisualizer : MonoBehaviour
{
    public RectTransform touchPrefab; // UI Image round, disabled by default
    private Canvas _canvas; private readonly Dictionary<int, RectTransform> _dots = new();

    void Start(){
        _canvas = FindObjectOfType<Canvas>();
        var mt = MultiTouchManager.Instance;
        mt.OnTouchBegan += Began; mt.OnTouchMoved += Moved; mt.OnTouchEnded += Ended;
    }

    void Began(MultiTouchManager.TouchEvt e){ var dot = Instantiate(touchPrefab, _canvas.transform); dot.gameObject.SetActive(true); _dots[e.fingerId]=dot; Move(dot,e.position); }
    void Moved(MultiTouchManager.TouchEvt e){ if(_dots.TryGetValue(e.fingerId,out var dot)) Move(dot,e.position); }
    void Ended(MultiTouchManager.TouchEvt e){ if(_dots.TryGetValue(e.fingerId,out var dot)){ Object.Destroy(dot.gameObject); _dots.Remove(e.fingerId);} }

    void Move(RectTransform rt, Vector2 screenPos){
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas.transform as RectTransform, screenPos, _canvas.worldCamera, out var lp);
        rt.anchoredPosition = lp;
    }
}
