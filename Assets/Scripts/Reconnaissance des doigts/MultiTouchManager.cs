using UnityEngine;
using System;
using System.Collections.Generic;

[DefaultExecutionOrder(-10000)]
public class MultiTouchManager : MonoBehaviour
{
    public static MultiTouchManager Instance { get; private set; }

    public enum SwipeAxis { None, Horizontal, Vertical }

    public struct TouchEvt
    {
        public int fingerId;
        public Vector2 position;
        public Vector2 delta;
        public Vector2 lockedDelta;
        public TouchPhase phase;
        public SwipeAxis lockedAxis;

        public TouchEvt(int id, Vector2 pos, Vector2 d, Vector2 lockDelta, TouchPhase p, SwipeAxis axis)
        {
            fingerId=id;
            position=pos;
            delta=d;
            lockedDelta=lockDelta;
            phase=p;
            lockedAxis=axis;
        }
    }

    public event Action<TouchEvt> OnTouchBegan;
    public event Action<TouchEvt> OnTouchMoved;
    public event Action<TouchEvt> OnTouchEnded;

    [Header("Axis Lock Settings")]
    [Tooltip("Minimum movement to detect axis direction")]
    public float axisDetectionThreshold = 10f;
    [Tooltip("If true, movement locks to detected axis")]
    public bool enableAxisLocking = true;

    private readonly Dictionary<int, Vector2> _lastPos = new();
    private readonly Dictionary<int, SwipeAxis> _lockedAxis = new();
    private readonly Dictionary<int, Vector2> _startPos = new();
    private const int _mouseFingerId = 9999;

    // --- Auto-spawn avant le chargement de la 1ère scène ---
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void EnsureExists()
    {
        if (Instance == null)
        {
            var go = new GameObject("MultiTouchManager");
            go.AddComponent<MultiTouchManager>();
            DontDestroyOnLoad(go);
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        // 1) Touch natif
        foreach (var t in Input.touches)
        {
            var delta = _lastPos.TryGetValue(t.fingerId, out var last) ? t.position - last : Vector2.zero;
            var lockedAxis = GetOrDetectAxis(t.fingerId, t.position, delta, t.phase);
            var lockedDelta = ApplyAxisLock(delta, lockedAxis);
            var evt = new TouchEvt(t.fingerId, t.position, delta, lockedDelta, t.phase, lockedAxis);

            switch (t.phase)
            {
                case TouchPhase.Began:
                    _startPos[t.fingerId] = t.position;
                    _lockedAxis[t.fingerId] = SwipeAxis.None;
                    OnTouchBegan?.Invoke(evt);
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    OnTouchMoved?.Invoke(evt);
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    OnTouchEnded?.Invoke(evt);
                    _startPos.Remove(t.fingerId);
                    _lockedAxis.Remove(t.fingerId);
                    break;
            }
            _lastPos[t.fingerId] = t.position;
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) _lastPos.Remove(t.fingerId);
        }

        // 2) Souris (debug)
        if (Input.touchCount == 0)
        {
            if (Input.GetMouseButtonDown(0))
            {
                var pos = (Vector2)Input.mousePosition;
                _lastPos[_mouseFingerId] = pos;
                _startPos[_mouseFingerId] = pos;
                _lockedAxis[_mouseFingerId] = SwipeAxis.None;
                OnTouchBegan?.Invoke(new TouchEvt(_mouseFingerId, pos, Vector2.zero, Vector2.zero, TouchPhase.Began, SwipeAxis.None));
            }
            else if (Input.GetMouseButton(0) && _lastPos.TryGetValue(_mouseFingerId, out var last))
            {
                var pos = (Vector2)Input.mousePosition;
                var delta = pos - last;
                var lockedAxis = GetOrDetectAxis(_mouseFingerId, pos, delta, TouchPhase.Moved);
                var lockedDelta = ApplyAxisLock(delta, lockedAxis);
                OnTouchMoved?.Invoke(new TouchEvt(_mouseFingerId, pos, delta, lockedDelta, TouchPhase.Moved, lockedAxis));
                _lastPos[_mouseFingerId] = pos;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                var pos = (Vector2)Input.mousePosition;
                var lockedAxis = _lockedAxis.TryGetValue(_mouseFingerId, out var axis) ? axis : SwipeAxis.None;
                OnTouchEnded?.Invoke(new TouchEvt(_mouseFingerId, pos, Vector2.zero, Vector2.zero, TouchPhase.Ended, lockedAxis));
                _lastPos.Remove(_mouseFingerId);
                _startPos.Remove(_mouseFingerId);
                _lockedAxis.Remove(_mouseFingerId);
            }
        }
    }

    private SwipeAxis GetOrDetectAxis(int fingerId, Vector2 currentPos, Vector2 delta, TouchPhase phase)
    {
        if (!enableAxisLocking) return SwipeAxis.None;

        // If already locked, return the locked axis
        if (_lockedAxis.TryGetValue(fingerId, out var locked) && locked != SwipeAxis.None)
            return locked;

        // Detect axis based on total movement from start
        if (_startPos.TryGetValue(fingerId, out var startPos))
        {
            Vector2 totalDelta = currentPos - startPos;
            float totalDistance = totalDelta.magnitude;

            if (totalDistance >= axisDetectionThreshold)
            {
                float absX = Mathf.Abs(totalDelta.x);
                float absY = Mathf.Abs(totalDelta.y);

                if (absX > absY)
                {
                    _lockedAxis[fingerId] = SwipeAxis.Horizontal;
                    return SwipeAxis.Horizontal;
                }
                else
                {
                    _lockedAxis[fingerId] = SwipeAxis.Vertical;
                    return SwipeAxis.Vertical;
                }
            }
        }

        return SwipeAxis.None;
    }

    private Vector2 ApplyAxisLock(Vector2 delta, SwipeAxis axis)
    {
        if (!enableAxisLocking || axis == SwipeAxis.None)
            return delta;

        if (axis == SwipeAxis.Horizontal)
            return new Vector2(delta.x, 0f);
        else // Vertical
            return new Vector2(0f, delta.y);
    }
}
