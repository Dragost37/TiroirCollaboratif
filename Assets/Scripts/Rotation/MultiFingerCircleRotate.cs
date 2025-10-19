using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MultiFingerCircleRotate : MonoBehaviour
{
    [Header("Target")]
    public Transform Target;

    [Header("Auto-assign Target")]
    public bool autoAssignIfNull = true;
    public bool autoAssignOnSelect = true;
    public bool useRootAsTarget = false;

    [Header("Gesture settings")]
    [Tooltip("Exige exactement 2 doigts pour interagir.")]
    public bool RequireExactlyTwoFingers = true;
    public float Sensitivity = 1.0f;
    public bool UseWorldSpace = true;

    [Header("Axes activés")]
    public bool RotateX = true;  // pitch via translation du pilote (verticale)
    public bool RotateY = true;  // yaw via TWIST (angle pilote autour du pivot)
    public bool RotateZ = true;  // roll via translation du pilote (horizontale)

    [Header("Pivot/Pilote")]
    [Tooltip("Nb de frames à observer avant de figer le pivot (si UseFirstFingerAsPivot=false).")]
    public int PivotDetectFrames = 3;
    [Tooltip("Jitter max (px) toléré pour considérer un doigt immobile.")]
    public float PivotJitterPx = 3f;
    [Tooltip("Si activé : le 1er doigt posé devient pivot, le 2e devient pilote (recommandé).")]
    public bool UseFirstFingerAsPivot = true;

    [Header("Verrouillage X/Z")]
    [Tooltip("Seuil (px) pour décider l’axe X/Z (dominance du delta du pilote).")]
    public float AxisLockThresholdPx = 2f;
    [Range(0f,1f)] public float AxisUnlockHysteresis = 0.35f; // un peu plus dur (propre)

    [Header("Yaw (twist autour du pivot)")]
    [Tooltip("Variation d’angle minimale (degrés) avant de déclencher le yaw.")]
    public float TwistThresholdDeg = 2.0f;
    [Tooltip("Gain yaw par degré de twist.")]
    public float TwistYawGain = 0.75f;

    [Header("Confort & stabilité")]
    [Tooltip("Lissage (0=brut, 1=très lissé)")]
    [Range(0f,1f)] public float SmoothFactor = 0.35f;
    [Tooltip("Zone morte en pixels avant de bouger")]
    public float DeadZonePx = 3f;
    [Tooltip("Vitesse max par frame (deg) pour éviter les à-coups)")]
    public float MaxDegPerFrame = 8f;

    [Header("Gain adaptatif")]
    public bool AdaptiveGain = true;
    public float GainPerMeter = 0.15f; // +15%/m

    [Header("Contraintes d'angles")]
    public bool ClampPitch = true;
    public float PitchMinDeg = -80f;
    public float PitchMaxDeg =  80f;

    [Header("Gesture detection extras")]
    public float MinRadiusPixels = 10f;
    public bool SimulateWithMouse = true; // (pivot/pilote Y difficile à simuler à la souris)

    [Header("Dessin à désactiver pendant la rotation")]
    public Behaviour[] drawingToolsToDisable;

    [Header("Debug UI")]
    public bool ShowOverlay = false;

    // ----- Ownership global -----
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
        foreach (var kv in s_FingerOwners) if (kv.Value == owner) toFree.Add(kv.Key);
        foreach (var id in toFree) s_FingerOwners.Remove(id);
    }

    // ----- Etat instance -----
    private readonly Dictionary<int, Vector2> ownedPrevPositions = new();
    private Camera mainCamera;
    private resetToOriPos resetScript;

    // Sous-arbre à bloquer
    private PartDuplicator[] _duplicators;
    private DraggablePart[]  _draggables;
    private bool _dupDisabled, _dragDisabled, _drawDisabled;

    // Pivot / pilote
    private int _pivotId = -1, _pilotId = -1;
    private int _pivotDetectCounter = 0;
    private float _accDeltaA = 0f, _accDeltaB = 0f;

    // Verrou X/Z pendant le geste
    private enum AxisXZ { None, X, Z }
    private AxisXZ _axisLockXZ = AxisXZ.None;

    // Centre et cache
    private Vector2 _prevCenter = Vector2.zero;

    // Confort: EMA + deadzone
    private Vector2 _pilotDeltaSmoothed = Vector2.zero;
    private float   _twistSmoothedDeg   = 0f;

    // Clamp pitch accumulation (World space simple)
    private float _pitchAccum = 0f;

    void Reset() { Target = transform; }

    void Start()
    {
        mainCamera = Camera.main;
        if (autoAssignIfNull && !Target) AssignTarget(useRootAsTarget ? transform.root : transform);
        else TryRefreshResetScript();
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
        ResetGestureState();
    }

    void Update()
    {
#if UNITY_EDITOR
        if (SimulateWithMouse)
        {
            HandleMouseSimulation(); // NB: la souris ne simule pas fidèlement pivot/pilote
            return;
        }
#endif
        ProcessTouchOwnership();

        var touches = GetOwnedActiveTouches();

        if (RequireExactlyTwoFingers)
        {
            if (touches.Count != 2) { EndGestureIdle(touches); return; }
        }
        else
        {
            if (touches.Count < 2) { EndGestureIdle(touches); return; }
        }

        Vector2 center = ComputeCenter(touches);
        if (!CenterHasEnoughRadius(touches, center))
        {
            EndGestureIdleWithCenter(center);
            return;
        }

        // Geste valide -> bloquer sous-fonctionnalités
        EnableDuplication(false);
        EnableDrag(false);
        EnableDrawing(false);

        // ----- Déterminer pivot / pilote -----
        Touch tA = touches[0];
        Touch tB = touches[1];

        // Init prev pos si besoin
        if (!ownedPrevPositions.ContainsKey(tA.fingerId)) ownedPrevPositions[tA.fingerId] = tA.position;
        if (!ownedPrevPositions.ContainsKey(tB.fingerId)) ownedPrevPositions[tB.fingerId] = tB.position;

        Vector2 prevA = ownedPrevPositions[tA.fingerId];
        Vector2 prevB = ownedPrevPositions[tB.fingerId];
        Vector2 dA = tA.position - prevA;
        Vector2 dB = tB.position - prevB;

        if (_pivotId < 0 || _pilotId < 0)
        {
            if (UseFirstFingerAsPivot)
            {
                // 1er doigt = pivot, 2e = pilote
                _pivotId = tA.fingerId;
                _pilotId = tB.fingerId;
            }
            else
            {
                // phase d’observation courte (moins mobile = pivot)
                _accDeltaA += dA.magnitude;
                _accDeltaB += dB.magnitude;
                _pivotDetectCounter++;

                if (_pivotDetectCounter >= PivotDetectFrames)
                {
                    bool aIsPivot = _accDeltaA <= _accDeltaB && _accDeltaA <= PivotJitterPx * _pivotDetectCounter;
                    bool bIsPivot = _accDeltaB <  _accDeltaA && _accDeltaB <= PivotJitterPx * _pivotDetectCounter;

                    if (aIsPivot || bIsPivot)
                    {
                        _pivotId = aIsPivot ? tA.fingerId : tB.fingerId;
                        _pilotId = aIsPivot ? tB.fingerId : tA.fingerId;
                    }
                    else
                    {
                        // à défaut, choisir le moins mobile
                        if (_accDeltaA <= _accDeltaB) { _pivotId = tA.fingerId; _pilotId = tB.fingerId; }
                        else                           { _pivotId = tB.fingerId; _pilotId = tA.fingerId; }
                    }
                }
            }
        }

        // Si pivot/pilote définis, récupérer leurs deltas
        Vector2 pivotPrev = prevA, pilotPrev = prevB, pivotNow = tA.position, pilotNow = tB.position;
        if (_pivotId == tB.fingerId) { pivotPrev = prevB; pivotNow = tB.position; pilotPrev = prevA; pilotNow = tA.position; }

        Vector3 totalRotation = Vector3.zero;

        // ----- 1) Yaw via twist du pilote autour du pivot (avec lissage/zone morte/cap) -----
        if (RotateY)
        {
            Vector2 prevVec = pilotPrev - pivotPrev;
            Vector2 currVec = pilotNow  - pivotNow;

            if (prevVec.sqrMagnitude > 1e-3f && currVec.sqrMagnitude > 1e-3f)
            {
                float aPrev = Mathf.Atan2(prevVec.y, prevVec.x) * Mathf.Rad2Deg;
                float aCurr = Mathf.Atan2(currVec.y, currVec.x) * Mathf.Rad2Deg;
                float dAdeg = Mathf.DeltaAngle(aPrev, aCurr);

                // EMA + deadzone + cap
                _twistSmoothedDeg = Ema(_twistSmoothedDeg, dAdeg, SmoothFactor);
                float twistOut = (Mathf.Abs(_twistSmoothedDeg) >= TwistThresholdDeg) ? _twistSmoothedDeg : 0f;

                if (twistOut != 0f)
                {
                    float g = GetAdaptiveGain();
                    float deg = twistOut * TwistYawGain * Sensitivity * g;
                    totalRotation.y += Mathf.Clamp(deg, -MaxDegPerFrame, +MaxDegPerFrame);
                }
            }
        }

        // ----- 2) Pitch/Roll via translation du pilote + VERROU X/Z (projections pures) -----
        Vector2 rawPilotDelta = pilotNow - pilotPrev;
        _pilotDeltaSmoothed = Ema(_pilotDeltaSmoothed, rawPilotDelta, SmoothFactor);

        Vector2 pilotDelta = new Vector2(
            Mathf.Abs(_pilotDeltaSmoothed.x) >= DeadZonePx ? _pilotDeltaSmoothed.x : 0f,
            Mathf.Abs(_pilotDeltaSmoothed.y) >= DeadZonePx ? _pilotDeltaSmoothed.y : 0f
        );

        if (pilotDelta.sqrMagnitude > 0.001f)
        {
            if (_axisLockXZ == AxisXZ.None && pilotDelta.magnitude >= AxisLockThresholdPx)
            {
                float ax = Mathf.Abs(pilotDelta.x);
                float ay = Mathf.Abs(pilotDelta.y);
                _axisLockXZ = (ax > ay) ? AxisXZ.Z : AxisXZ.X;
            }
            else if (_axisLockXZ != AxisXZ.None)
            {
                float ax = Mathf.Abs(pilotDelta.x);
                float ay = Mathf.Abs(pilotDelta.y);
                if (_axisLockXZ == AxisXZ.X)
                {
                    if (ax > ay * (1f + AxisUnlockHysteresis)) _axisLockXZ = AxisXZ.Z;
                }
                else if (_axisLockXZ == AxisXZ.Z)
                {
                    if (ay > ax * (1f + AxisUnlockHysteresis)) _axisLockXZ = AxisXZ.X;
                }
            }

            float g = GetAdaptiveGain();

            if (_axisLockXZ == AxisXZ.X && RotateX)
            {
                float deg = -pilotDelta.y * Sensitivity * 0.1f * g;
                totalRotation.x += Mathf.Clamp(deg, -MaxDegPerFrame, +MaxDegPerFrame);
            }

            if (_axisLockXZ == AxisXZ.Z && RotateZ)
            {
                float deg =  pilotDelta.x * Sensitivity * 0.1f * g;
                totalRotation.z += Mathf.Clamp(deg, -MaxDegPerFrame, +MaxDegPerFrame);
            }
        }

        // Appliquer
        if (totalRotation != Vector3.zero)
        {
            ApplyRotation(totalRotation);
            if (resetScript != null) resetScript.ResetActivityTimer();
        }

        // Mises à jour des prev
        ownedPrevPositions[tA.fingerId] = tA.position;
        ownedPrevPositions[tB.fingerId] = tB.position;
        _prevCenter = center;
    }

    // --------- Ownership & helpers ----------
    private void ProcessTouchOwnership()
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.touches[i];
            int id = t.fingerId;

            if (t.phase == TouchPhase.Began)
            {
                bool alreadyOwning = ownedPrevPositions.Count > 0;
                if (RaycastHitThisObject(t.position) || alreadyOwning)
                {
                    if (TryClaimFinger(id, this))
                    {
                        if (autoAssignOnSelect && !alreadyOwning)
                        {
                            var newTarget = useRootAsTarget ? transform.root : transform;
                            if (Target != newTarget) AssignTarget(newTarget);
                        }
                        ownedPrevPositions[id] = t.position;

                        // Reset d’un nouveau geste quand on passe à 1 puis 2 doigts
                        if (ownedPrevPositions.Count <= 2) ResetGestureState();
                    }
                }
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                if (ownedPrevPositions.ContainsKey(id))
                    ownedPrevPositions.Remove(id);
                ReleaseFinger(id, this);

                if (ownedPrevPositions.Count < 2) EndGestureIdle(new List<Touch>());
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

        if (Physics.Raycast(ray, out var hit3D) && hit3D.collider)
        {
            if (hit3D.transform == transform || hit3D.transform.IsChildOf(transform))
                return true;
        }

        var hit2D = Physics2D.GetRayIntersection(ray);
        if (hit2D.collider)
        {
            if (hit2D.transform == transform || hit2D.transform.IsChildOf(transform))
                return true;
        }
        return false;
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

    private float GetAdaptiveGain()
    {
        if (!AdaptiveGain || !mainCamera || !Target) return 1f;
        float dist = Vector3.Distance(mainCamera.transform.position, Target.position);
        return 1f + dist * GainPerMeter;
    }

    private void ApplyRotation(Vector3 rotation)
    {
        if (!Target) return;

        // Clamp pitch (X) si demandé
        if (ClampPitch && rotation.x != 0f)
        {
            float next = _pitchAccum + rotation.x;
            float allowed = Mathf.Clamp(next, PitchMinDeg, PitchMaxDeg) - _pitchAccum;
            rotation.x = allowed;
            _pitchAccum += allowed;
        }

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

    public void AssignTarget(Transform t) { Target = t; TryRefreshResetScript(); }
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

    private void EndGestureIdle(List<Touch> ownedTouches)
    {
        EnableDuplication(true);
        EnableDrag(true);
        EnableDrawing(true);
        _prevCenter = ComputeCenter(ownedTouches);
        ResetGestureState();
    }
    private void EndGestureIdleWithCenter(Vector2 center)
    {
        EnableDuplication(true);
        EnableDrag(true);
        EnableDrawing(true);
        _prevCenter = center;
        ResetGestureState();
    }
    private void ResetGestureState()
    {
        _pivotId = _pilotId = -1;
        _pivotDetectCounter = 0;
        _accDeltaA = _accDeltaB = 0f;
        _axisLockXZ = AxisXZ.None;

        _pilotDeltaSmoothed = Vector2.zero;
        _twistSmoothedDeg = 0f;
        // On peut aussi reset _pitchAccum si tu veux re-clamper par geste :
        // _pitchAccum = 0f;
    }

    #region Mouse simulation (éditeur)
    private Vector2 lastMousePos;
    private bool mouseActive = false;
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
                EnableDuplication(false);
                EnableDrag(false);
                EnableDrawing(false);
                ResetGestureState();
            }
            else mouseActive = false;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            mouseActive = false;
            EnableDuplication(true);
            EnableDrag(true);
            EnableDrawing(true);
            ResetGestureState();
        }

        if (!mouseActive) return;

        // La souris ne simule pas pivot/pilote correctement.
        // On applique juste X/Z avec verrou + confort.
        Vector2 curr = (Vector2)Input.mousePosition;
        Vector2 raw = curr - lastMousePos;

        _pilotDeltaSmoothed = Ema(_pilotDeltaSmoothed, raw, SmoothFactor);
        Vector2 d = new Vector2(
            Mathf.Abs(_pilotDeltaSmoothed.x) >= DeadZonePx ? _pilotDeltaSmoothed.x : 0f,
            Mathf.Abs(_pilotDeltaSmoothed.y) >= DeadZonePx ? _pilotDeltaSmoothed.y : 0f
        );

        if (_axisLockXZ == AxisXZ.None && d.magnitude >= AxisLockThresholdPx)
            _axisLockXZ = (Mathf.Abs(d.x) > Mathf.Abs(d.y)) ? AxisXZ.Z : AxisXZ.X;

        Vector3 rot = Vector3.zero;
        float g = GetAdaptiveGain();

        if (_axisLockXZ == AxisXZ.X && RotateX)
            rot.x += Mathf.Clamp(-d.y * Sensitivity * 0.1f * g, -MaxDegPerFrame, +MaxDegPerFrame);
        if (_axisLockXZ == AxisXZ.Z && RotateZ)
            rot.z += Mathf.Clamp( d.x * Sensitivity * 0.1f * g, -MaxDegPerFrame, +MaxDegPerFrame);

        if (rot != Vector3.zero) ApplyRotation(rot);
        lastMousePos = curr;
    }
    #endregion

    // -------- EMA helpers ----------
    private static float Ema(float current, float target, float factor)
        => Mathf.Lerp(current, target, 1f - Mathf.Pow(1f - Mathf.Clamp01(factor), 1f + Time.deltaTime * 60f));
    private static Vector2 Ema(Vector2 current, Vector2 target, float factor)
        => new Vector2(Ema(current.x, target.x, factor), Ema(current.y, target.y, factor));

    // -------- Overlay ----------
    void OnGUI()
    {
        if (!ShowOverlay) return;
        string axis = _axisLockXZ.ToString(); // None/X/Z
        string txt = $"Pivot:{_pivotId}  Pilot:{_pilotId}  Axis:{axis}";
        GUI.Label(new Rect(10,10,600,24), txt);
    }
}