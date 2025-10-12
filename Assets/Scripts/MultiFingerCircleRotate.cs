using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MultiFingerCircleRotate : MonoBehaviour
{
    [Header("Target")]
    public Transform Target;

    [Header("Gesture settings")]
    public int MinFingerCount = 2;
    public float Sensitivity = 1.0f;
    [Range(0f, 1f)] public float TangentAlignmentThreshold = 0.7f;
    public bool RotateX = true;
    public bool RotateY = true;
    public bool RotateZ = true;
    public bool UseWorldSpace = true;

    [Header("Gesture detection extras")]
    public float MinRadiusPixels = 10f;
    public bool SimulateWithMouse = true;

    private readonly Dictionary<int, Vector2> ownedPrevPositions = new Dictionary<int, Vector2>();
    private Vector2 prevCenter = Vector2.zero;
    private Camera mainCamera;
    private resetToOriPos resetScript;

    // Mouse simulation
    private Vector2 lastMousePos;
    private bool mouseActive = false;
    private Vector2 mouseScreenCenter;

    void Reset()
    {
        Target = transform;
    }

    void Start()
    {
        if (!Target) Target = transform;
        mainCamera = Camera.main;
        if (Target) resetScript = Target.GetComponent<resetToOriPos>();
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

        List<Touch> ownedTouches = GetOwnedActiveTouches();
        if (ownedTouches.Count < MinFingerCount)
        {
            prevCenter = ComputeCenter(ownedTouches);
            return;
        }

        Vector2 center = ComputeCenter(ownedTouches);
        if (!CenterHasEnoughRadius(ownedTouches, center))
        {
            prevCenter = center;
            return;
        }

        Vector3 totalRotation = Vector3.zero;

        foreach (var t in ownedTouches)
        {
            int id = t.fingerId;
            if (!ownedPrevPositions.ContainsKey(id))
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

            if (RotateX) totalRotation.x -= deltaMove.y * Sensitivity * 0.1f;
            if (RotateY) totalRotation.y += deltaMove.x * Sensitivity * 0.1f;

            if (RotateZ)
            {
                Vector2 prevVec = prevPos - prevCenter;
                Vector2 currVec = currPos - center;

                if (prevVec.sqrMagnitude > 0.0001f && currVec.sqrMagnitude > 0.0001f)
                {
                    float anglePrev = Mathf.Atan2(prevVec.y, prevVec.x);
                    float angleCurr = Mathf.Atan2(currVec.y, currVec.x);
                    float delta = Mathf.DeltaAngle(anglePrev * Mathf.Rad2Deg, angleCurr * Mathf.Rad2Deg);

                    Vector2 tangent = new Vector2(-currVec.y, currVec.x).normalized;
                    float align = Mathf.Abs(Vector2.Dot(deltaMove.normalized, tangent));
                    if (align >= TangentAlignmentThreshold)
                        totalRotation.z += delta * Sensitivity * 0.5f;
                }
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
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.touches[i];
            int id = t.fingerId;

            if (t.phase == TouchPhase.Began)
            {
                if (RaycastHitThisObject(t.position))
                    ownedPrevPositions[id] = t.position;
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                if (ownedPrevPositions.ContainsKey(id))
                    ownedPrevPositions.Remove(id);
            }
        }

        // purge des touches disparues
        List<int> toRemove = null;
        foreach (int ownedId in ownedPrevPositions.Keys)
        {
            bool exists = false;
            for (int i = 0; i < Input.touchCount; i++)
                if (Input.touches[i].fingerId == ownedId) { exists = true; break; }
            if (!exists)
            {
                (toRemove ??= new List<int>()).Add(ownedId);
            }
        }
        if (toRemove != null) foreach (int id in toRemove) ownedPrevPositions.Remove(id);
    }

    private List<Touch> GetOwnedActiveTouches()
    {
        var list = new List<Touch>();
        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.touches[i];
            if (ownedPrevPositions.ContainsKey(t.fingerId)) list.Add(t);
        }
        return list;
    }

    private bool RaycastHitThisObject(Vector2 screenPos)
    {
        if (!mainCamera) mainCamera = Camera.main;
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out var hit) && hit.collider && hit.collider.gameObject == gameObject;
    }

    // ✅ retourne le CENTRE (moyenne), pas la somme
    private Vector2 ComputeCenter(List<Touch> touches)
    {
        if (touches == null || touches.Count == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero;
        foreach (var t in touches) sum += t.position;
        return sum / touches.Count;
    }

    // ✅ compare la MOYENNE du rayon au seuil
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

    #region Mouse simulation (editor)
    private void HandleMouseSimulation()
    {
        if (!SimulateWithMouse) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (RaycastHitThisObject(Input.mousePosition))
            {
                mouseActive = true;
                lastMousePos = Input.mousePosition;

                // ❌ new Vector2(Screen.width) -> ❎
                // ✅ centre de l'écran :
                mouseScreenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                // (alternative : centre de l'objet à l'écran)
                // var sp = mainCamera.WorldToScreenPoint(Target ? Target.position : transform.position);
                // mouseScreenCenter = new Vector2(sp.x, sp.y);
            }
            else
            {
                mouseActive = false;
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            mouseActive = false;
        }

        if (!mouseActive) return;

        Vector2 currPos = (Vector2)Input.mousePosition;
        Vector2 deltaMove = currPos - lastMousePos;
        Vector3 rotation = Vector3.zero;

        if (RotateX) rotation.x -= deltaMove.y * Sensitivity * 0.1f;
        if (RotateY) rotation.y += deltaMove.x * Sensitivity * 0.1f;

        if (RotateZ)
        {
            Vector2 prevVec = lastMousePos - mouseScreenCenter;
            Vector2 currVec = currPos      - mouseScreenCenter;

            if (prevVec.sqrMagnitude > 0.0001f && currVec.sqrMagnitude > 0.0001f)
            {
                float anglePrev = Mathf.Atan2(prevVec.y, prevVec.x);
                float angleCurr = Mathf.Atan2(currVec.y, currVec.x);
                float delta = Mathf.DeltaAngle(anglePrev * Mathf.Rad2Deg, angleCurr * Mathf.Rad2Deg);

                Vector2 tangent = new Vector2(-currVec.y, currVec.x).normalized;
                float align = Mathf.Abs(Vector2.Dot(deltaMove.normalized, tangent));
                if (align >= TangentAlignmentThreshold)
                    rotation.z += delta * Sensitivity * 0.5f;
            }
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
