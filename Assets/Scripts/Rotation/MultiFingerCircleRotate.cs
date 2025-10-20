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
        // libère tous les doigts détenus par cet objet
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

        // libère tous les doigts éventuellement détenus
        ReleaseAllFor(this);
        ownedPrevPositions.Clear();
        fingerMovementDistances.Clear();
        fingerStartPositions.Clear();
        stationaryFingerId = -1;
        lockedRotationAxis = RotationAxis.None;
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

        // Detect and lock rotation axis if enabled
        if (enableAxisLocking && lockedRotationAxis == RotationAxis.None)
        {
            lockedRotationAxis = DetectRotationAxis(ownedTouches);
        }

        Vector3 totalRotation = Vector3.zero;

        // Get stationary finger position as pivot
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

            // Skip the stationary finger for rotation calculation
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

            // Apply rotation based on locked axis
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
                // No axis locking - allow all rotations
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
            ApplyRotation(totalRotation);
            if (resetScript != null) resetScript.ResetActivityTimer();
        }
    }

    private void ProcessTouchOwnership()
    {
        // Claim/release par phase
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.touches[i];
            int id = t.fingerId;

            if (t.phase == TouchPhase.Began)
            {
                if (RaycastHitThisObject(t.position))
                {
                    // tente de réserver ce doigt : si déjà pris par un autre objet, on ignore
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

        // purge des touches disparues (ex: perte d’événement)
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

        // Calculate total movement from start
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

        // Check if we have a stationary finger for Z rotation
        bool hasStationaryFinger = false;
        if (touches.Count >= 2 && stationaryFingerId != -1)
        {
            // Check if stationary finger really hasn't moved much
            foreach (var t in touches)
            {
                if (t.fingerId == stationaryFingerId && fingerStartPositions.TryGetValue(t.fingerId, out var startPos))
                {
                    float stationaryMovement = Vector2.Distance(t.position, startPos);
                    if (stationaryMovement < axisDetectionThreshold * 0.5f)
                    {
                        hasStationaryFinger = true;
                        break;
                    }
                }
            }
        }

        // If one finger is stationary and other moves horizontally, it's Z rotation
        if (hasStationaryFinger && absX > absY && RotateZ)
        {
            return RotationAxis.Z;
        }
        // Otherwise, determine X or Y based on dominant movement
        else if (absY > absX && RotateX)
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
            // je ne prends que les doigts que J’AI claim
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
            EnableDuplication(true);
            EnableDrag(true);
            EnableDrawing(true);
        }

        if (!mouseActive) return;

        Vector2 currPos = (Vector2)Input.mousePosition;
        Vector2 deltaMove = currPos - lastMousePos;
        Vector3 rotation = Vector3.zero;

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
            }
        }

        // Apply rotation based on locked axis
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
            // No axis locking
            if (RotateX) rotation.x -= deltaMove.y * Sensitivity * 0.1f;
            if (RotateY) rotation.y += deltaMove.x * Sensitivity * 0.1f;
            if (RotateZ) rotation.z += deltaMove.x * Sensitivity * 0.5f;
        }

        if (rotation != Vector3.zero)
        {
            ApplyRotation(rotation);
            if (resetScript != null) resetScript.ResetActivityTimer();
        }

        lastMousePos = currPos;
    }
    #endregion
}
