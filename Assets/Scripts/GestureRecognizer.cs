using UnityEngine;
using System;
using System.Collections.Generic;

public class GestureRecognizer : MonoBehaviour
{
    public Transform target;
    public float rotateSpeed = 0.25f; public float scaleSpeed = 0.005f; public float minScale=0.3f, maxScale=3f;

    public event Action<Vector2> OnPan; public event Action<float> OnPinch; public event Action<float> OnRotate;

    private readonly Dictionary<int, Vector2> _positions = new();

    void OnEnable(){ var mt=MultiTouchManager.Instance; mt.OnTouchBegan+=B; mt.OnTouchMoved+=M; mt.OnTouchEnded+=E; }
    void OnDisable(){ var mt=MultiTouchManager.Instance; mt.OnTouchBegan-=B; mt.OnTouchMoved-=M; mt.OnTouchEnded-=E; }

    void B(MultiTouchManager.TouchEvt e){ _positions[e.fingerId]=e.position; }
    void M(MultiTouchManager.TouchEvt e){ _positions[e.fingerId]=e.position; Process(); }
    void E(MultiTouchManager.TouchEvt e){ _positions.Remove(e.fingerId); }

    void Process()
    {
        if (_positions.Count==1){
            foreach(var kv in _positions){ var delta = DeltaForFinger(kv.Key); OnPan?.Invoke(delta); ApplyPan(delta); }
        }
        else if (_positions.Count>=2)
        {
            var en = _positions.GetEnumerator(); en.MoveNext(); var a=en.Current; en.MoveNext(); var b=en.Current;
            var pinchDelta = (DeltaForFinger(a.Key) - DeltaForFinger(b.Key)).magnitude * scaleSpeed;
            var rotDelta = Vector2.SignedAngle(a.Value - b.Value, (a.Value + DeltaForFinger(a.Key)) - (b.Value + DeltaForFinger(b.Key)) ) * rotateSpeed;

            OnPinch?.Invoke(pinchDelta);
            OnRotate?.Invoke(rotDelta);
            ApplyScale(pinchDelta); ApplyRotate(rotDelta);
        }
    }

    Vector2 DeltaForFinger(int fingerId)
    {
        foreach(var t in Input.touches) if (t.fingerId==fingerId) return t.deltaPosition;
        return Vector2.zero;
    }

    void ApplyPan(Vector2 delta)
    {
        if (!target) return; var cam = Camera.main; if(!cam) return;
        var worldDelta = cam.ScreenToWorldPoint(new Vector3(delta.x, delta.y, cam.nearClipPlane)) - cam.ScreenToWorldPoint(new Vector3(0,0,cam.nearClipPlane));
        target.position += worldDelta;
    }

    void ApplyScale(float s)
    {
        if (!target) return; var scale = Mathf.Clamp(target.localScale.x + s, minScale, maxScale); target.localScale = Vector3.one * scale;
    }

    void ApplyRotate(float deg)
    {
        if (!target) return; target.Rotate(Vector3.up, deg, Space.World);
    }
}
