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
    public bool RotateX = true;
    public bool RotateY = true;
    public bool RotateZ = true;
    public bool UseWorldSpace = true;

    [Header("Z-Axis Settings (Pilot-based)")]
    [Tooltip("Smoothing factor for Z rotation (0=raw, 1=very smooth)")]
    [Range(0f, 1f)] public float ZSmoothFactor = 0.35f;
    [Tooltip("Dead zone in pixels before Z rotation activates")]
    public float ZDeadZonePx = 3f;
    [Tooltip("Max Z rotation speed per frame (degrees)")]
    public float ZMaxDegPerFrame = 8f;
    [Tooltip("Enable adaptive gain based on camera distance")]
    public bool AdaptiveGain = true;
    [Tooltip("Gain increase per meter of distance")]
    public float GainPerMeter = 0.15f;

    [Header("Axis Locking")]
    public bool enableAxisLocking = true;
    [Tooltip("Minimum movement to detect rotation axis")]
    public float axisDetectionThreshold = 15f;
    [Tooltip("Maximum movement for a finger to be considered stationary")]
    public float stationaryThreshold = 30f;
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

    // --- Ownership global des doigts (un doigt -> un rotateur) ---
    private static readonly Dictionary<int, MultiFingerCircleRotate> s_FingerOwners = new();

    private static bool TryClaimFinger(int fingerId, MultiFingerCircleRotate owner)
    {
        if (s_FingerOwners.TryGetValue(fingerId, out var current))
            return current == owner; // déjà à moi -> ok
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

    // --- Etat instance ---
    private readonly Dictionary<int, Vector2> ownedPrevPositions = new();
    private readonly Dictionary<int, float> fingerMovementDistances = new();
    private readonly Dictionary<int, Vector2> fingerStartPositions = new();
    private Vector2 prevCenter = Vector2.zero;
    private int stationaryFingerId = -1;
    private RotationAxis lockedRotationAxis = RotationAxis.None;

    // Z-axis pilot smoothing
    private Vector2 _pilotDeltaSmoothed = Vector2.zero;

    // Snapping state
    private Vector3 accumulatedRotation = Vector3.zero;
    private Vector3 snappedTargetRotation = Vector3.zero;
    private bool hasSetInitialSnap = false;

    private Camera mainCamera;
    private resetToOriPos resetScript;

    // Mouse simulation
    private Vector2 lastMousePos;
    private bool mouseActive = false;
    private Vector2 mouseScreenCenter;

    // À bloquer pendant la rotation (local au sous-arbre)
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
        _pilotDeltaSmoothed = Vector2.zero;
    }

    void Update()
    {
        if (showDebugLogs)
            Debug.Log($"[Update Start] Input.touchCount={Input.touchCount}, SimulateWithMouse={SimulateWithMouse}");

#if UNITY_EDITOR
        // Only use mouse simulation if no actual touches are detected (allows touchscreen to work)
        if (SimulateWithMouse && Input.touchCount == 0)
        {
            HandleMouseSimulation();
            return;
        }
#endif
        ProcessTouchOwnership();

        var ownedTouches = GetOwnedActiveTouches();

        if (showDebugLogs)
            Debug.Log($"[After Processing] ownedTouches.Count={ownedTouches.Count}, MinFingerCount={MinFingerCount}");

        if (ownedTouches.Count < MinFingerCount)
        {
            EnableDuplication(true);
            EnableDrag(true);
            EnableDrawing(true);
            prevCenter = ComputeCenter(ownedTouches);
            fingerMovementDistances.Clear();
            fingerStartPositions.Clear();
            stationaryFingerId = -1;
            lockedRotationAxis = RotationAxis.None;
            accumulatedRotation = Vector3.zero;
            hasSetInitialSnap = false;
            _pilotDeltaSmoothed = Vector2.zero;
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
            _pilotDeltaSmoothed = Vector2.zero;
            return;
        }

        // rotation valide en cours -> bloque dup/drag/dessin pour CE sous-arbre uniquement
        EnableDuplication(false);
        EnableDrag(false);
        EnableDrawing(false);

        // Track movement distances to find stationary finger
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

        // Find the stationary finger (the one that moved the least)
        if (ownedTouches.Count == 2)
        {
            float minDist = float.MaxValue;
            float maxDist = 0f;
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
                    if (dist > maxDist)
                    {
                        maxDist = dist;
                    }
                }
            }

            // Only set stationary if there's a clear difference
            if (minId >= 0 && maxDist > minDist * 1.5f)
            {
                stationaryFingerId = minId;
                if (showDebugLogs)
                    Debug.Log($"[Stationary Detection] Finger {minId} is stationary (moved {minDist:F1}px), other moved {maxDist:F1}px");
            }
            else if (showDebugLogs)
            {
                Debug.Log($"[Stationary Detection] No clear stationary finger: min={minDist:F1}px, max={maxDist:F1}px");
            }
        }

        // Detect and lock rotation axis if enabled
        if (enableAxisLocking && lockedRotationAxis == RotationAxis.None)
        {
            lockedRotationAxis = DetectRotationAxis(ownedTouches);
            if (showDebugLogs && lockedRotationAxis != RotationAxis.None)
                Debug.Log($"[MultiFingerCircleRotate] Locked to {lockedRotationAxis} axis rotation, Stationary finger: {stationaryFingerId}");
        }

        if (showDebugLogs)
            Debug.Log($"[Debug] Touch count: {ownedTouches.Count}, Stationary finger: {stationaryFingerId}, Locked axis: {lockedRotationAxis}");

        Vector3 totalRotation = Vector3.zero;

        // Get stationary finger position as pivot for Z rotation
        Vector2 pivotPos = Vector2.zero;
        bool hasPivot = false;
        foreach (var t in ownedTouches)
        {
            if (t.fingerId == stationaryFingerId)
            {
                pivotPos = t.position;
                hasPivot = true;
                if (showDebugLogs)
                    Debug.Log($"[Debug] Found pivot at position: {pivotPos}");
                break;
            }
        }

        // Special handling for Z rotation with 2 fingers (one stationary)
        if (ownedTouches.Count == 2 && RotateZ && stationaryFingerId >= 0)
        {
            // Find the moving finger
            Touch movingFinger = default;
            bool foundMoving = false;

            foreach (var t in ownedTouches)
            {
                if (t.fingerId != stationaryFingerId)
                {
                    movingFinger = t;
                    foundMoving = true;
                    break;
                }
            }

            if (foundMoving && ownedPrevPositions.ContainsKey(movingFinger.fingerId))
            {
                Vector2 prevPos = ownedPrevPositions[movingFinger.fingerId];
                Vector2 currPos = movingFinger.position;
                Vector2 deltaMove = currPos - prevPos;

                if (showDebugLogs)
                    Debug.Log($"[Z-Debug] Moving finger {movingFinger.fingerId}: deltaMove={deltaMove}, magnitude={deltaMove.magnitude:F2}");

                // Use pilot-based horizontal translation for Z rotation
                Vector2 rawPilotDelta = deltaMove;
                _pilotDeltaSmoothed = Ema(_pilotDeltaSmoothed, rawPilotDelta, ZSmoothFactor);

                float pilotDeltaX = Mathf.Abs(_pilotDeltaSmoothed.x) >= ZDeadZonePx ? _pilotDeltaSmoothed.x : 0f;

                if (showDebugLogs)
                    Debug.Log($"[Z-Axis] Raw delta: {rawPilotDelta.x:F2}, Smoothed: {_pilotDeltaSmoothed.x:F2}, After deadzone: {pilotDeltaX:F2}");

                if (Mathf.Abs(pilotDeltaX) > 0.001f)
                {
                    float g = GetAdaptiveGain();
                    float deg = pilotDeltaX * Sensitivity * 0.1f * g;
                    float clampedDeg = Mathf.Clamp(deg, -ZMaxDegPerFrame, +ZMaxDegPerFrame);
                    totalRotation.z += clampedDeg;

                    if (showDebugLogs)
                        Debug.Log($"[Z-Axis] Gain: {g:F2}, Deg: {deg:F2}, Clamped: {clampedDeg:F2}, Stationary: {stationaryFingerId}, Moving: {movingFinger.fingerId}");
                }
            }
        }

        // Handle X and Y rotation for all fingers
        foreach (var t in ownedTouches)
        {
            int id = t.fingerId;

            if (!ownedPrevPositions.ContainsKey(id))
            {
                ownedPrevPositions[id] = t.position;
                continue;
            }

            // Skip the stationary finger for X/Y rotation
            if (id == stationaryFingerId && ownedTouches.Count == 2)
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

            // Apply X and Y rotation based on locked axis or all axes
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
                        // Z is handled separately above
                        break;
                }
            }
            else
            {
                // No axis locking - allow X and Y rotations
                if (RotateX) totalRotation.x -= deltaMove.y * Sensitivity * 0.1f;
                if (RotateY) totalRotation.y += deltaMove.x * Sensitivity * 0.1f;
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
        if (showDebugLogs)
            Debug.Log($"[ProcessTouchOwnership] Processing {Input.touchCount} touches");

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.touches[i];
            int id = t.fingerId;

            if (t.phase == TouchPhase.Began)
            {
                bool hitObject = RaycastHitThisObject(t.position);
                if (showDebugLogs)
                    Debug.Log($"[Touch Began] Finger {id} at {t.position}, Hit object: {hitObject}");

                if (hitObject)
                {
                    bool claimed = TryClaimFinger(id, this);
                    if (showDebugLogs)
                        Debug.Log($"[Touch Began] Finger {id} claimed: {claimed}");

                    if (claimed)
                    {
                        if (autoAssignOnSelect)
                        {
                            var newTarget = useRootAsTarget ? transform.root : transform;
                            if (Target != newTarget) AssignTarget(newTarget);
                        }
                        ownedPrevPositions[id] = t.position;
                        fingerStartPositions[id] = t.position;

                        if (showDebugLogs)
                            Debug.Log($"[Touch Began] Finger {id} added to owned positions. Total owned: {ownedPrevPositions.Count}");
                    }
                }
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                if (showDebugLogs)
                    Debug.Log($"[Touch Ended] Finger {id}");

                if (ownedPrevPositions.ContainsKey(id))
                    ownedPrevPositions.Remove(id);
                fingerStartPositions.Remove(id);
                ReleaseFinger(id, this);
            }
        }

        // purge des touches disparues (ex: perte d'événement)
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

        // For Z rotation, we need exactly 2 fingers where one is stationary
        if (touches.Count == 2 && RotateZ)
        {
            // Check each finger's movement from start
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

            // Check if one finger is stationary and the other has moved
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

            // If we have a stationary finger and a moving finger
            if (stationaryIndex >= 0 && movingIndex >= 0)
            {
                Vector2 movingDelta = movements[movingIndex];
                float zAbsX = Mathf.Abs(movingDelta.x);
                float zAbsY = Mathf.Abs(movingDelta.y);

                if (showDebugLogs)
                    Debug.Log($"[MultiFingerCircleRotate] Z motion: absX={zAbsX:F1}, absY={zAbsY:F1}");

                // If movement is predominantly horizontal, it's Z rotation
                if (zAbsX > zAbsY)
                {
                    if (showDebugLogs)
                        Debug.Log("[MultiFingerCircleRotate] Detected Z rotation!");
                    return RotationAxis.Z;
                }
            }
        }

        // For X or Y rotation, calculate average movement
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

        // Wait until enough movement to determine axis
        if (totalDistance < axisDetectionThreshold)
            return RotationAxis.None;

        float absX = Mathf.Abs(totalDelta.x);
        float absY = Mathf.Abs(totalDelta.y);

        // Determine X or Y based on dominant movement
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
        if (!mainCamera)
        {
            if (showDebugLogs)
                Debug.LogWarning("[RaycastHitThisObject] No main camera found!");
            return false;
        }

        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        bool hit = Physics.Raycast(ray, out var hitInfo) && hitInfo.collider && hitInfo.collider.gameObject == gameObject;

        if (showDebugLogs && Physics.Raycast(ray, out hitInfo))
            Debug.Log($"[Raycast] Hit: {hitInfo.collider.gameObject.name}, This: {gameObject.name}, Match: {hit}");

        return hit;
    }

    private Vector2 ComputeCenter(List<Touch> touches)
    {
        if (touches == null || touches.Count == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero;
        foreach (var t in touches) sum += t.position;
        Vector2 center = sum / touches.Count;

        if (showDebugLogs)
            Debug.Log($"[ComputeCenter] {touches.Count} touches, center: {center}");

        return center;
    }

    private bool CenterHasEnoughRadius(List<Touch> touches, Vector2 center)
    {
        if (touches == null || touches.Count == 0) return false;
        float sum = 0f;
        foreach (var t in touches) sum += Vector2.Distance(t.position, center);
        float avg = sum / touches.Count;
        bool hasEnough = avg >= MinRadiusPixels;

        if (showDebugLogs)
            Debug.Log($"[CenterHasEnoughRadius] Avg radius: {avg:F1}px, Required: {MinRadiusPixels}px, Result: {hasEnough}");

        return hasEnough;
    }

    private float GetAdaptiveGain()
    {
        if (!AdaptiveGain || !mainCamera || !Target) return 1f;
        float dist = Vector3.Distance(mainCamera.transform.position, Target.position);
        return 1f + dist * GainPerMeter;
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
        }
    }

    private float SnapToNearest(float angle, float snapValue)
    {
        // Normalize angle to 0-360 range
        angle = angle % 360f;
        if (angle < 0) angle += 360f;

        // Snap to nearest multiple of snapValue
        float snapped = Mathf.Round(angle / snapValue) * snapValue;
        return snapped;
    }

    // ---------- Auto-assign helpers ----------
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

    // ---------- Toggles (local au sous-arbre) ----------
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

    // EMA helper
    private static float Ema(float current, float target, float factor)
    {
        return Mathf.Lerp(current, target, 1f - Mathf.Pow(1f - Mathf.Clamp01(factor), 1f + Time.deltaTime * 60f));
    }

    private static Vector2 Ema(Vector2 current, Vector2 target, float factor)
    {
        return new Vector2(Ema(current.x, target.x, factor), Ema(current.y, target.y, factor));
    }

    #region Mouse simulation (editor)
    // NOTE: Mouse simulation cannot properly simulate 2-finger Z rotation!
    // To test Z rotation, you need to:
    // 1. Build to device, OR
    // 2. Set SimulateWithMouse = false and use Unity Remote, OR
    // 3. Use a touch simulator plugin
    private void HandleMouseSimulation()
    {
        if (!SimulateWithMouse || Input.touchCount > 0) return;

        if (showDebugLogs)
            Debug.Log("[Mouse Simulation] Mouse simulation is active - Z rotation with 2 fingers cannot be tested!");

        if (Input.GetMouseButtonDown(0))
        {
            bool hitObject = RaycastHitThisObject(Input.mousePosition);
            if (showDebugLogs)
                Debug.Log($"[Mouse Down] Position: {Input.mousePosition}, Hit object: {hitObject}");

            if (hitObject)
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
                _pilotDeltaSmoothed = Vector2.zero;

                EnableDuplication(false);
                EnableDrag(false);
                EnableDrawing(false);

                if (showDebugLogs)
                    Debug.Log("[Mouse Down] Mouse rotation started");
            }
            else
            {
                mouseActive = false;
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            if (showDebugLogs)
                Debug.Log("[Mouse Up] Mouse rotation ended");

            mouseActive = false;
            lockedRotationAxis = RotationAxis.None;
            accumulatedRotation = Vector3.zero;
            hasSetInitialSnap = false;
            _pilotDeltaSmoothed = Vector2.zero;
            EnableDuplication(true);
            EnableDrag(true);
            EnableDrawing(true);
        }

        if (!mouseActive) return;

        Vector2 currPos = (Vector2)Input.mousePosition;
        Vector2 deltaMove = currPos - lastMousePos;
        Vector3 rotation = Vector3.zero;

        if (showDebugLogs && deltaMove.magnitude > 0.1f)
            Debug.Log($"[Mouse Move] Delta: {deltaMove}, Magnitude: {deltaMove.magnitude:F2}");

        // Detect axis on first significant movement
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

                if (showDebugLogs)
                    Debug.Log($"[Mouse Axis Lock] Locked to {lockedRotationAxis} (absX={absX:F1}, absY={absY:F1})");
            }
        }

        // Apply rotation based on locked axis
        if (enableAxisLocking)
        {
            switch (lockedRotationAxis)
            {
                case RotationAxis.X:
                    if (RotateX)
                    {
                        rotation.x -= deltaMove.y * Sensitivity * 0.1f;
                        if (showDebugLogs && Mathf.Abs(rotation.x) > 0.01f)
                            Debug.Log($"[Mouse X Rotation] {rotation.x:F2} degrees");
                    }
                    break;
                case RotationAxis.Y:
                    if (RotateY)
                    {
                        rotation.y += deltaMove.x * Sensitivity * 0.1f;
                        if (showDebugLogs && Mathf.Abs(rotation.y) > 0.01f)
                            Debug.Log($"[Mouse Y Rotation] {rotation.y:F2} degrees");
                    }
                    break;
                case RotationAxis.Z:
                    if (RotateZ)
                    {
                        Vector2 rawPilotDelta = deltaMove;
                        _pilotDeltaSmoothed = Ema(_pilotDeltaSmoothed, rawPilotDelta, ZSmoothFactor);
                        float pilotDeltaX = Mathf.Abs(_pilotDeltaSmoothed.x) >= ZDeadZonePx ? _pilotDeltaSmoothed.x : 0f;
                        if (Mathf.Abs(pilotDeltaX) > 0.001f)
                        {
                            float g = GetAdaptiveGain();
                            float deg = pilotDeltaX * Sensitivity * 0.1f * g;
                            float clampedDeg = Mathf.Clamp(deg, -ZMaxDegPerFrame, +ZMaxDegPerFrame);
                            rotation.z += clampedDeg;

                            if (showDebugLogs)
                                Debug.Log($"[Z-Axis Mouse] Delta: {pilotDeltaX:F2}, Gain: {g:F2}, Deg: {clampedDeg:F2}");
                        }
                    }
                    break;
            }
        }
        else
        {
            // No axis locking
            if (RotateX) rotation.x -= deltaMove.y * Sensitivity * 0.1f;
            if (RotateY) rotation.y += deltaMove.x * Sensitivity * 0.1f;
            if (RotateZ)
            {
                Vector2 rawPilotDelta = deltaMove;
                _pilotDeltaSmoothed = Ema(_pilotDeltaSmoothed, rawPilotDelta, ZSmoothFactor);
                float pilotDeltaX = Mathf.Abs(_pilotDeltaSmoothed.x) >= ZDeadZonePx ? _pilotDeltaSmoothed.x : 0f;
                if (Mathf.Abs(pilotDeltaX) > 0.001f)
                {
                    float g = GetAdaptiveGain();
                    float deg = pilotDeltaX * Sensitivity * 0.1f * g;
                    float clampedDeg = Mathf.Clamp(deg, -ZMaxDegPerFrame, +ZMaxDegPerFrame);
                    rotation.z += clampedDeg;

                    if (showDebugLogs)
                        Debug.Log($"[Z-Axis Mouse NoLock] Delta: {pilotDeltaX:F2}, Gain: {g:F2}, Deg: {clampedDeg:F2}");
                }
            }
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
