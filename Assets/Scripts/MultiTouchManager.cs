using UnityEngine;
using System;
using System.Collections.Generic;

public class MultiTouchManager : MonoBehaviour
{
    public static MultiTouchManager Instance { get; private set; }

    public struct TouchEvt
    {
        public int fingerId; public Vector2 position; public Vector2 delta; public TouchPhase phase;
        public TouchEvt(int id, Vector2 pos, Vector2 d, TouchPhase p){ fingerId=id; position=pos; delta=d; phase=p; }
    }

    public event Action<TouchEvt> OnTouchBegan;
    public event Action<TouchEvt> OnTouchMoved;
    public event Action<TouchEvt> OnTouchEnded;

    private readonly Dictionary<int, Vector2> _lastPos = new();
    private int _mouseFingerId = 9999;

    void Awake(){ if(Instance!=null){ Destroy(gameObject); return; } Instance=this; }

    void Update()
    {
        // 1) Native touches
        foreach (var t in Input.touches)
        {
            var delta = _lastPos.ContainsKey(t.fingerId) ? t.position - _lastPos[t.fingerId] : Vector2.zero;
            var evt = new TouchEvt(t.fingerId, t.position, delta, t.phase);
            switch (t.phase)
            {
                case TouchPhase.Began: OnTouchBegan?.Invoke(evt); break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary: OnTouchMoved?.Invoke(evt); break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled: OnTouchEnded?.Invoke(evt); break;
            }
            _lastPos[t.fingerId] = t.position;
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) _lastPos.Remove(t.fingerId);
        }

        // 2) Mouse (debug) – left button as pseudo-touch
        if (Input.touchCount == 0)
        {
            if (Input.GetMouseButtonDown(0))
            {
                var pos = (Vector2)Input.mousePosition; _lastPos[_mouseFingerId] = pos;
                OnTouchBegan?.Invoke(new TouchEvt(_mouseFingerId, pos, Vector2.zero, TouchPhase.Began));
            }
            else if (Input.GetMouseButton(0))
            {
                var pos = (Vector2)Input.mousePosition; var delta = pos - _lastPos[_mouseFingerId];
                OnTouchMoved?.Invoke(new TouchEvt(_mouseFingerId, pos, delta, TouchPhase.Moved));
                _lastPos[_mouseFingerId] = pos;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                var pos = (Vector2)Input.mousePosition;
                OnTouchEnded?.Invoke(new TouchEvt(_mouseFingerId, pos, Vector2.zero, TouchPhase.Ended));
                _lastPos.Remove(_mouseFingerId);
            }
        }
    }
}
