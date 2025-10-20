using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PinchToScale : MonoBehaviour
{
    [Header("Target")]
    public Transform Target;

    [Header("Scale Settings")]
    public int MinFingerCount = 2;
    public float Sensitivity = 1.0f;
    public float MinScale = 0.1f;
    public float MaxScale = 10.0f;
    public bool UniformScale = true;

    [Header("Auto-assign Target")]
    public bool autoAssignIfNull = true;
    public bool useRootAsTarget = false;

    [Header("Components to disable during scaling")]
    public Behaviour[] componentsToDisable;

    [Header("Debug")]
    public bool showDebugLogs = false;


    private static readonly Dictionary<int, PinchToScale> s_FingerOwners = new();

    private static bool TryClaimFinger(int fingerId, PinchToScale owner)
    {
        if (s_FingerOwners.TryGetValue(fingerId, out var current))
            return current == owner;
        s_FingerOwners[fingerId] = owner;
        return true;
    }

    private static void ReleaseFinger(int fingerId, PinchToScale owner)
    {
        if (s_FingerOwners.TryGetValue(fingerId, out var current) && current == owner)
            s_FingerOwners.Remove(fingerId);
    }

    private static void ReleaseAllFor(PinchToScale owner)
    {
        var toFree = new List<int>();
        foreach (var kv in s_FingerOwners)
            if (kv.Value == owner) toFree.Add(kv.Key);
        foreach (var id in toFree) s_FingerOwners.Remove(id);
    }


    private readonly Dictionary<int, Vector2> ownedPrevPositions = new();
    private float previousFingerDistance = 0f;
    private Vector3 initialScale;
    private Camera mainCamera;
    private resetToOriPos resetScript;
    private bool isScaling = false;
    private bool componentsDisabled = false;


    private bool mouseActive = false;
    private Vector2 lastMousePos;
    private Vector2 mouseAnchorPos;
    private float mouseInitialDistance;

    void Reset()
    {
        Target = transform;
    }

    void Start()
    {
        mainCamera = Camera.main;

        if (autoAssignIfNull && !Target)
            Target = useRootAsTarget ? transform.root : transform;

        if (Target)
        {
            initialScale = Target.localScale;
            resetScript = Target.GetComponent<resetToOriPos>();
        }
    }

    void OnDisable()
    {
        EnableComponents(true);
        ReleaseAllFor(this);
        ownedPrevPositions.Clear();
        isScaling = false;
        previousFingerDistance = 0f;
    }

    void Update()
    {
#if UNITY_EDITOR
        HandleMouseSimulation();
        if (mouseActive) return;
#endif
        ProcessTouchOwnership();

        var ownedTouches = GetOwnedActiveTouches();

        if (ownedTouches.Count < MinFingerCount)
        {
            EnableComponents(true);
            isScaling = false;
            previousFingerDistance = 0f;
            return;
        }


        float currentDistance = CalculateAverageFingerDistance(ownedTouches);

        if (currentDistance <= 0f)
        {
            previousFingerDistance = 0f;
            return;
        }

        if (!isScaling || previousFingerDistance <= 0f)
        {

            isScaling = true;
            previousFingerDistance = currentDistance;
            EnableComponents(false);

            if (showDebugLogs)
                Debug.Log($"[PinchToScale] Started scaling with {ownedTouches.Count} fingers, initial distance: {currentDistance}");

            return;
        }


        float distanceRatio = currentDistance / previousFingerDistance;

        if (showDebugLogs)
            Debug.Log($"[PinchToScale] Distance ratio: {distanceRatio}, current: {currentDistance}, prev: {previousFingerDistance}");

        ApplyScale(distanceRatio);

        if (resetScript != null)
            resetScript.ResetActivityTimer();

        previousFingerDistance = currentDistance;


        foreach (var t in ownedTouches)
            ownedPrevPositions[t.fingerId] = t.position;
    }

    private void ProcessTouchOwnership()
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.touches[i];
            int id = t.fingerId;

            if (t.phase == TouchPhase.Began)
            {
                if (RaycastHitThisObject(t.position))
                {
                    if (TryClaimFinger(id, this))
                    {
                        ownedPrevPositions[id] = t.position;

                        if (showDebugLogs)
                            Debug.Log($"[PinchToScale] Claimed finger {id}");
                    }
                }
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                if (ownedPrevPositions.ContainsKey(id))
                {
                    ownedPrevPositions.Remove(id);
                    ReleaseFinger(id, this);

                    if (showDebugLogs)
                        Debug.Log($"[PinchToScale] Released finger {id}");
                }
            }
        }


        List<int> toRemove = null;
        foreach (int ownedId in ownedPrevPositions.Keys)
        {
            bool exists = false;
            for (int i = 0; i < Input.touchCount; i++)
                if (Input.touches[i].fingerId == ownedId) { exists = true; break; }
            if (!exists) (toRemove ??= new List<int>()).Add(ownedId);
        }
        if (toRemove != null)
        {
            foreach (int id in toRemove)
            {
                ownedPrevPositions.Remove(id);
                ReleaseFinger(id, this);
            }
        }
    }

    private List<Touch> GetOwnedActiveTouches()
    {
        var list = new List<Touch>();
        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.touches[i];
            if (s_FingerOwners.TryGetValue(t.fingerId, out var owner) && owner == this)
                if (ownedPrevPositions.ContainsKey(t.fingerId))
                    list.Add(t);
        }
        return list;
    }

    private bool RaycastHitThisObject(Vector2 screenPos)
    {
        if (!mainCamera) mainCamera = Camera.main;
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out var hit) && hit.collider && hit.collider.gameObject == gameObject;
    }

    private float CalculateAverageFingerDistance(List<Touch> touches)
    {
        if (touches == null || touches.Count < 2) return 0f;

        float totalDistance = 0f;
        int pairCount = 0;


        for (int i = 0; i < touches.Count; i++)
        {
            for (int j = i + 1; j < touches.Count; j++)
            {
                totalDistance += Vector2.Distance(touches[i].position, touches[j].position);
                pairCount++;
            }
        }

        return pairCount > 0 ? totalDistance / pairCount : 0f;
    }

    private void ApplyScale(float ratio)
    {
        if (!Target) return;

        Vector3 currentScale = Target.localScale;
        Vector3 newScale;

        if (UniformScale)
        {
            newScale = currentScale * ratio;
        }
        else
        {
            newScale = new Vector3(
                currentScale.x * ratio,
                currentScale.y * ratio,
                currentScale.z
            );
        }

        newScale.x = Mathf.Clamp(newScale.x, MinScale, MaxScale);
        newScale.y = Mathf.Clamp(newScale.y, MinScale, MaxScale);
        newScale.z = Mathf.Clamp(newScale.z, MinScale, MaxScale);

        Target.localScale = newScale;
    }

    private void EnableComponents(bool enable)
    {
        if (componentsToDisable == null) return;

        if (enable && !componentsDisabled) return;
        if (!enable && componentsDisabled) return;

        foreach (var comp in componentsToDisable)
            if (comp) comp.enabled = enable;

        componentsDisabled = !enable;
    }

    #region Mouse Simulation (Editor)
    private void HandleMouseSimulation()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (RaycastHitThisObject(Input.mousePosition))
            {
                mouseActive = true;
                lastMousePos = Input.mousePosition;
                mouseAnchorPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                mouseInitialDistance = Vector2.Distance(lastMousePos, mouseAnchorPos);

                EnableComponents(false);

                if (showDebugLogs)
                    Debug.Log($"[PinchToScale] Mouse scaling started, initial distance: {mouseInitialDistance}");
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            if (mouseActive)
            {
                mouseActive = false;
                EnableComponents(true);

                if (showDebugLogs)
                    Debug.Log("[PinchToScale] Mouse scaling ended");
            }
        }

        if (mouseActive)
        {
            Vector2 currentMousePos = Input.mousePosition;
            float currentDistance = Vector2.Distance(currentMousePos, mouseAnchorPos);

            if (mouseInitialDistance > 0f && currentDistance > 0f)
            {
                float ratio = currentDistance / mouseInitialDistance;
                ApplyScale(ratio);

                if (resetScript != null)
                    resetScript.ResetActivityTimer();

                mouseInitialDistance = currentDistance;
            }

            lastMousePos = currentMousePos;
        }
    }
    #endregion
}
