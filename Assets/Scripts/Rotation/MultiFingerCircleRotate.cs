using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MultiFingerCircleRotate : MonoBehaviour
{
    public enum RotationAxis { None, X, Y, Z }

    [Header("Target")]
    public Transform Target;

    [Header("Auto-assign Target")]
    public bool autoAssignIfNull = true;
    public bool autoAssignOnSelect = true;
    public bool useRootAsTarget = false;

    [Header("Gesture settings")]
    public int MinFingerCount = 2;
    public float Sensitivity = 1.0f;
    [Range(0f, 1f)] public float TangentAlignmentThreshold = 0.7f;
    public bool RotateX = true;
    public bool RotateY = true;
    public bool RotateZ = true;
    public bool UseWorldSpace = true;

    [Header("Axis Locking")]
    public bool enableAxisLocking = true;
    [Tooltip("Minimum movement to detect rotation axis")]
    public float axisDetectionThreshold = 15f;
    [Tooltip("Maximum movement for a finger to be considered stationary")]
    public float stationaryThreshold = 20f;
    public bool showDebugLogs = false;

    [Header("Rotation Snapping")]
    public bool enableSnapping = false;
    [Tooltip("Snap rotation to this angle increment (in degrees)")]
    public float snapAngle = 15f;

    [Header("Gesture detection extras")]
    public float MinRadiusPixels = 10f;
    public bool SimulateWithMouse = true;

    [Header("Dessin à désactiver pendant la rotation")]
    [Tooltip("Drag ici les scripts de dessin (Painter, LineDrawer, etc.) à couper pendant la rotation.")]
    public Behaviour[] drawingToolsToDisable;


    private static readonly Dictionary<int, MultiFingerCircleRotate> s_FingerOwners = new();

    private static bool TryClaimFinger(int fingerId, MultiFingerCircleRotate owner)
    {
        if (s_FingerOwners.TryGetValue(fingerId, out var current))
            return current == owner;
        s_FingerOwners[fingerId] = owner;
        return true;
    }

    private static void ReleaseFinger(int fingerId, MultiFingerCircleRotate owner)
    {
        if (s_FingerOwners.TryGetValue(fingerId, out var current) && current == owner)
            s_FingerOwners.Remove(fingerId);
    }

    private static void ReleaseAllFor(MultiFingerCircleRotate owner)
    {

        var toFree = new List<int>();
        foreach (var kv in s_FingerOwners)
            if (kv.Value == owner) toFree.Add(kv.Key);
        foreach (var id in toFree) s_FingerOwners.Remove(id);
    }


    private readonly Dictionary<int, Vector2> ownedPrevPositions = new();
    private readonly Dictionary<int, float> fingerMovementDistances = new();
    private readonly Dictionary<int, Vector2> fingerStartPositions = new();
    private Vector2 prevCenter = Vector2.zero;
    private int stationaryFingerId = -1;
    private RotationAxis lockedRotationAxis = RotationAxis.None;


    private Vector3 accumulatedRotation = Vector3.zero;
    private Vector3 snappedTargetRotation = Vector3.zero;
    private bool hasSetInitialSnap = false;

    private Camera mainCamera;
    private resetToOriPos resetScript;


    private Vector2 lastMousePos;
    private bool mouseActive = false;
    private Vector2 mouseScreenCenter;


    private PartDuplicator[] _duplicators;
    private DraggablePart[]  _draggables;
    private bool _dupDisabled  = false;
    private bool _dragDisabled = false;
    private bool _drawDisabled = false;

    void Reset() { Target = transform; }

    void Start()
    {
        mainCamera = Camera.main;

        if (autoAssignIfNull && !Target)
            AssignTarget(useRootAsTarget ? transform.root : transform);
        else
            TryRefreshResetScript();

        _duplicators = GetComponentsInChildren<PartDuplicator>(true);
        _draggables  = GetComponentsInChildren<DraggablePart>(true);
    }

    void OnDisable()
    {
        EnableDuplication(true);
        EnableDrag(true);
        EnableDrawing(true);


        ReleaseAllFor(this);
        ownedPrevPositions.Clear();
        fingerMovementDistances.Clear();
        fingerStartPositions.Clear();
        stationaryFingerId = -1;
        lockedRotationAxis = RotationAxis.None;
        accumulatedRotation = Vector3.zero;
        hasSetInitialSnap = false;
    }

    void Update()
    {
#if UNITY_EDITOR
        if (SimulateWithMouse)
        {
            HandleMouseSimulation();
            return;
        }
#endif
        ProcessTouchOwnership();

        var ownedTouches = GetOwnedActiveTouches();

        if (ownedTouches.Count < MinFingerCount)
        {
            EnableDuplication(true);
            EnableDrag(true);
            EnableDrawing(true);
            prevCenter = ComputeCenter(ownedTouches);
            return;
        }

        Vector2 center = ComputeCenter(ownedTouches);
        if (!CenterHasEnoughRadius(ownedTouches, center))
        {
            EnableDuplication(true);
            EnableDrag(true);
            EnableDrawing(true);
            prevCenter = center;
            fingerMovementDistances.Clear();
            fingerStartPositions.Clear();
            stationaryFingerId = -1;
            lockedRotationAxis = RotationAxis.None;
            accumulatedRotation = Vector3.zero;
            hasSetInitialSnap = false;
            return;
        }


        EnableDuplication(false);
        EnableDrag(false);
        EnableDrawing(false);


        foreach (var t in ownedTouches)
        {
            int id = t.fingerId;
            if (!ownedPrevPositions.ContainsKey(id))
            {
                ownedPrevPositions[id] = t.position;
                fingerMovementDistances[id] = 0f;
                fingerStartPositions[id] = t.position;
            }
            else
            {
                Vector2 prevPos = ownedPrevPositions[id];
                Vector2 currPos = t.position;
                float moveDist = Vector2.Distance(currPos, prevPos);

                if (!fingerMovementDistances.ContainsKey(id))
                    fingerMovementDistances[id] = moveDist;
                else
                    fingerMovementDistances[id] += moveDist;
            }
        }


        if (ownedTouches.Count >= 2)
        {
            float minDist = float.MaxValue;
            int minId = -1;
            foreach (var t in ownedTouches)
            {
                if (fingerMovementDistances.TryGetValue(t.fingerId, out float dist))
                {
                    if (dist < minDist)
                    {
                        minDist = dist;
                        minId = t.fingerId;
                    }
                }
            }
            stationaryFingerId = minId;
        }


        if (enableAxisLocking && lockedRotationAxis == RotationAxis.None)
        {
            lockedRotationAxis = DetectRotationAxis(ownedTouches);
            if (showDebugLogs && lockedRotationAxis != RotationAxis.None)
                Debug.Log($"[MultiFingerCircleRotate] Locked to {lockedRotationAxis} axis rotation");
        }

        Vector3 totalRotation = Vector3.zero;


        Vector2 pivotPos = Vector2.zero;
        bool hasPivot = false;
        foreach (var t in ownedTouches)
        {
            if (t.fingerId == stationaryFingerId)
            {
                pivotPos = t.position;
                hasPivot = true;
                break;
            }
        }

        foreach (var t in ownedTouches)
        {
            int id = t.fingerId;

            if (!ownedPrevPositions.ContainsKey(id))
            {
                ownedPrevPositions[id] = t.position;
                continue;
            }


            if (id == stationaryFingerId)
            {
                ownedPrevPositions[id] = t.position;
                continue;
            }

            Vector2 prevPos = ownedPrevPositions[id];
            Vector2 currPos = t.position;
            Vector2 deltaMove = currPos - prevPos;

            if (deltaMove.sqrMagnitude < 0.01f)
            {
                ownedPrevPositions[id] = currPos;
                continue;
            }


            if (enableAxisLocking)
            {
                switch (lockedRotationAxis)
                {
                    case RotationAxis.X:
                        if (RotateX) totalRotation.x -= deltaMove.y * Sensitivity * 0.1f;
                        break;
                    case RotationAxis.Y:
                        if (RotateY) totalRotation.y += deltaMove.x * Sensitivity * 0.1f;
                        break;
                    case RotationAxis.Z:
                        if (RotateZ && hasPivot)
                            totalRotation.z += deltaMove.x * Sensitivity * 0.5f;
                        break;
                }
            }
            else
            {

                if (RotateX) totalRotation.x -= deltaMove.y * Sensitivity * 0.1f;
                if (RotateY) totalRotation.y += deltaMove.x * Sensitivity * 0.1f;
                if (RotateZ && hasPivot)
                    totalRotation.z += deltaMove.x * Sensitivity * 0.5f;
            }

            ownedPrevPositions[id] = currPos;
        }

        prevCenter = center;

        if (totalRotation != Vector3.zero)
        {
            if (enableSnapping && Target != null)
            {
                ApplyIncrementalSnapping(totalRotation);
            }
            else
            {
                ApplyRotation(totalRotation);
            }

            if (resetScript != null) resetScript.ResetActivityTimer();
        }
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
                        if (autoAssignOnSelect)
                        {
                            var newTarget = useRootAsTarget ? transform.root : transform;
                            if (Target != newTarget) AssignTarget(newTarget);
                        }
                        ownedPrevPositions[id] = t.position;
                        fingerStartPositions[id] = t.position;
                    }
                }
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                if (ownedPrevPositions.ContainsKey(id))
                    ownedPrevPositions.Remove(id);
                fingerStartPositions.Remove(id);
                ReleaseFinger(id, this);
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
                fingerStartPositions.Remove(id);
                ReleaseFinger(id, this);
            }
        }
    }

    private RotationAxis DetectRotationAxis(List<Touch> touches)
    {
        if (touches.Count < MinFingerCount) return RotationAxis.None;


        if (touches.Count == 2 && RotateZ)
        {

            Vector2[] movements = new Vector2[2];
            float[] distances = new float[2];

            for (int i = 0; i < 2; i++)
            {
                if (fingerStartPositions.TryGetValue(touches[i].fingerId, out var startPos))
                {
                    movements[i] = touches[i].position - startPos;
                    distances[i] = movements[i].magnitude;
                }
            }

            if (showDebugLogs)
                Debug.Log($"[MultiFingerCircleRotate] Z check: Finger0 dist={distances[0]:F1}, Finger1 dist={distances[1]:F1}");


            int stationaryIndex = -1;
            int movingIndex = -1;

            if (distances[0] < stationaryThreshold && distances[1] >= axisDetectionThreshold)
            {
                stationaryIndex = 0;
                movingIndex = 1;
            }
            else if (distances[1] < stationaryThreshold && distances[0] >= axisDetectionThreshold)
            {
                stationaryIndex = 1;
                movingIndex = 0;
            }


            if (stationaryIndex >= 0 && movingIndex >= 0)
            {
                Vector2 movingDelta = movements[movingIndex];
                float zAbsX = Mathf.Abs(movingDelta.x);
                float zAbsY = Mathf.Abs(movingDelta.y);

                if (showDebugLogs)
                    Debug.Log($"[MultiFingerCircleRotate] Z motion: absX={zAbsX:F1}, absY={zAbsY:F1}");


                if (zAbsX > zAbsY)
                {
                    if (showDebugLogs)
                        Debug.Log("[MultiFingerCircleRotate] Detected Z rotation!");
                    return RotationAxis.Z;
                }
            }
        }


        Vector2 totalDelta = Vector2.zero;
        int validCount = 0;

        foreach (var t in touches)
        {
            if (fingerStartPositions.TryGetValue(t.fingerId, out var startPos))
            {
                totalDelta += t.position - startPos;
                validCount++;
            }
        }

        if (validCount == 0) return RotationAxis.None;

        totalDelta /= validCount;
        float totalDistance = totalDelta.magnitude;


        if (totalDistance < axisDetectionThreshold)
            return RotationAxis.None;

        float absX = Mathf.Abs(totalDelta.x);
        float absY = Mathf.Abs(totalDelta.y);


        if (absY > absX && RotateX)
        {
            return RotationAxis.X;
        }
        else if (absX > absY && RotateY)
        {
            return RotationAxis.Y;
        }

        return RotationAxis.None;
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

    private Vector2 ComputeCenter(List<Touch> touches)
    {
        if (touches == null || touches.Count == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero;
        foreach (var t in touches) sum += t.position;
        return sum / touches.Count;
    }

    private bool CenterHasEnoughRadius(List<Touch> touches, Vector2 center)
    {
        if (touches == null || touches.Count == 0) return false;
        float sum = 0f;
        foreach (var t in touches) sum += Vector2.Distance(t.position, center);
        float avg = sum / touches.Count;
        return avg >= MinRadiusPixels;
    }

    private void ApplyRotation(Vector3 rotation)
    {
        if (!Target) return;

        if (UseWorldSpace)
        {
            if (rotation.x != 0) Target.Rotate(Vector3.right,   rotation.x, Space.World);
            if (rotation.y != 0) Target.Rotate(Vector3.up,      rotation.y, Space.World);
            if (rotation.z != 0) Target.Rotate(Vector3.forward, rotation.z, Space.World);
        }
        else
        {
            if (rotation.x != 0) Target.Rotate(Vector3.right,   rotation.x, Space.Self);
            if (rotation.y != 0) Target.Rotate(Vector3.up,      rotation.y, Space.Self);
            if (rotation.z != 0) Target.Rotate(Vector3.forward, rotation.z, Space.Self);
        }
    }

    private void ApplyIncrementalSnapping(Vector3 rotationDelta)
    {
        if (!Target || snapAngle <= 0) return;

        // Initialize snapped target on first rotation
        if (!hasSetInitialSnap)
        {
            Vector3 currentRotation = Target.localEulerAngles;
            snappedTargetRotation = new Vector3(
                SnapToNearest(currentRotation.x, snapAngle),
                SnapToNearest(currentRotation.y, snapAngle),
                SnapToNearest(currentRotation.z, snapAngle)
            );
            Target.localEulerAngles = snappedTargetRotation;
            hasSetInitialSnap = true;
            accumulatedRotation = Vector3.zero;
            return;
        }

        // Accumulate rotation
        accumulatedRotation += rotationDelta;

        // Calculate what the new rotation would be
        Vector3 potentialRotation = snappedTargetRotation + accumulatedRotation;

        // Snap to nearest angle for each axis
        Vector3 newSnappedRotation = new Vector3(
            SnapToNearest(potentialRotation.x, snapAngle),
            SnapToNearest(potentialRotation.y, snapAngle),
            SnapToNearest(potentialRotation.z, snapAngle)
        );

        // Check if any axis changed to a new snap position
        if (newSnappedRotation != snappedTargetRotation)
        {
            snappedTargetRotation = newSnappedRotation;
            Target.localEulerAngles = snappedTargetRotation;

            // Reset accumulation after snap
            accumulatedRotation = Vector3.zero;

            if (showDebugLogs)
                Debug.Log($"[Snap] Snapped to: {snappedTargetRotation}");
        }
    }

    private float SnapToNearest(float angle, float snapValue)
    {

        angle = angle % 360f;
        if (angle < 0) angle += 360f;


        float snapped = Mathf.Round(angle / snapValue) * snapValue;
        return snapped;
    }


    public void AssignTarget(Transform t)
    {
        Target = t;
        TryRefreshResetScript();
    }

    private void TryRefreshResetScript()
    {
        resetScript = null;
        if (Target) resetScript = Target.GetComponent<resetToOriPos>();
    }


    private void EnableDuplication(bool enable)
    {
        if (_duplicators == null || _duplicators.Length == 0)
            _duplicators = GetComponentsInChildren<PartDuplicator>(true);
        if (_duplicators == null) return;

        if (enable && !_dupDisabled) return;
        if (!enable && _dupDisabled) return;

        foreach (var d in _duplicators) if (d) d.enabled = enable;
        _dupDisabled = !enable;
    }

    private void EnableDrag(bool enable)
    {
        if (_draggables == null || _draggables.Length == 0)
            _draggables = GetComponentsInChildren<DraggablePart>(true);
        if (_draggables == null) return;

        if (enable && !_dragDisabled) return;
        if (!enable && _dragDisabled) return;

        foreach (var g in _draggables) if (g) g.enabled = enable;
        _dragDisabled = !enable;
    }

    private void EnableDrawing(bool enable)
    {
        if (drawingToolsToDisable == null) return;

        if (enable && !_drawDisabled) return;
        if (!enable && _drawDisabled) return;

        foreach (var b in drawingToolsToDisable) if (b) b.enabled = enable;
        _drawDisabled = !enable;
    }

    #region Mouse simulation (editor)
    private void HandleMouseSimulation()
    {
        if (!SimulateWithMouse) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (RaycastHitThisObject(Input.mousePosition))
            {
                if (autoAssignOnSelect)
                {
                    var newTarget = useRootAsTarget ? transform.root : transform;
                    if (Target != newTarget) AssignTarget(newTarget);
                }

                mouseActive = true;
                lastMousePos = Input.mousePosition;
                mouseScreenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                lockedRotationAxis = RotationAxis.None;

                EnableDuplication(false);
                EnableDrag(false);
                EnableDrawing(false);
            }
            else
            {
                mouseActive = false;
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            mouseActive = false;
            lockedRotationAxis = RotationAxis.None;
            accumulatedRotation = Vector3.zero;
            hasSetInitialSnap = false;
            EnableDuplication(true);
            EnableDrag(true);
            EnableDrawing(true);
        }

        if (!mouseActive) return;

        Vector2 currPos = (Vector2)Input.mousePosition;
        Vector2 deltaMove = currPos - lastMousePos;
        Vector3 rotation = Vector3.zero;


        if (enableAxisLocking && lockedRotationAxis == RotationAxis.None)
        {
            Vector2 totalMove = currPos - lastMousePos;
            float totalDist = totalMove.magnitude;

            if (totalDist >= axisDetectionThreshold)
            {
                float absX = Mathf.Abs(totalMove.x);
                float absY = Mathf.Abs(totalMove.y);

                if (absY > absX && RotateX)
                    lockedRotationAxis = RotationAxis.X;
                else if (absX > absY && RotateY)
                    lockedRotationAxis = RotationAxis.Y;
            }
        }


        if (enableAxisLocking)
        {
            switch (lockedRotationAxis)
            {
                case RotationAxis.X:
                    if (RotateX) rotation.x -= deltaMove.y * Sensitivity * 0.1f;
                    break;
                case RotationAxis.Y:
                    if (RotateY) rotation.y += deltaMove.x * Sensitivity * 0.1f;
                    break;
                case RotationAxis.Z:
                    if (RotateZ) rotation.z += deltaMove.x * Sensitivity * 0.5f;
                    break;
            }
        }
        else
        {

            if (RotateX) rotation.x -= deltaMove.y * Sensitivity * 0.1f;
            if (RotateY) rotation.y += deltaMove.x * Sensitivity * 0.1f;
            if (RotateZ) rotation.z += deltaMove.x * Sensitivity * 0.5f;
        }

        if (rotation != Vector3.zero)
        {
            if (enableSnapping && Target != null)
            {
                ApplyIncrementalSnapping(rotation);
            }
            else
            {
                ApplyRotation(rotation);
            }

            if (resetScript != null) resetScript.ResetActivityTimer();
        }

        lastMousePos = currPos;
    }
    #endregion
}
