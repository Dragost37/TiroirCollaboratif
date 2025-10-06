using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MultiFingerCircleRotate : MonoBehaviour
{
    [Header("Target")]
    public Transform Target;

    [Header("Gesture settings")]
    [Tooltip("Nombre minimum de doigts requis pour déclencher la rotation")]
    public int MinFingerCount = 2;
    [Tooltip("Sensibilité: multiplier l'angle calculé")]
    public float Sensitivity = 1.0f;
    [Tooltip("Seuil d'alignement (0..1). 1 = mouvement parfaitement tangent.")]
    [Range(0f, 1f)]
    public float TangentAlignmentThreshold = 0.7f;
    [Tooltip("Rotation autour de quels axes (X, Y, Z)")]
    public bool RotateX = true;
    public bool RotateY = true;
    public bool RotateZ = true;
    [Tooltip("Appliquer la rotation en espace monde (true) ou espace local du Target (false)")]
    public bool UseWorldSpace = true;

    [Header("Gesture detection extras")]
    [Tooltip("Rayon minimum (en pixels) du centre pour prendre en compte un doigt (évite trop petit rayon)")]
    public float MinRadiusPixels = 10f;
    [Tooltip("Si activé, on peut tester la rotation dans l'éditeur avec la souris")]
    public bool SimulateWithMouse = true;


    private Dictionary<int, Vector2> ownedPrevPositions = new Dictionary<int, Vector2>();
    private Vector2 prevCenter = Vector2.zero;
    private Camera mainCamera;

    private resetToOriPos resetScript;


    private Vector2 lastMousePos;
    private bool mouseActive = false;
    private Vector2 mouseScreenCenter;

    void Reset()
    {
        Target = transform;
    }

    void Start()
    {
        if (Target == null) Target = transform;
        mainCamera = Camera.main;

        if (Target != null)
        {
            resetScript = Target.GetComponent<resetToOriPos>();
        }
    }

    void Update()
    {
        if (Application.isEditor && SimulateWithMouse)
        {
            HandleMouseSimulation();
            return;
        }


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


        foreach (Touch t in ownedTouches)
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

            if (RotateX)
            {
                totalRotation.x -= deltaMove.y * Sensitivity * 0.1f;
            }

            if (RotateY)
            {
                totalRotation.y += deltaMove.x * Sensitivity * 0.1f;
            }

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
                    {
                        totalRotation.z += delta * Sensitivity * 0.5f;
                    }
                }
            }

            ownedPrevPositions[id] = currPos;
        }

        prevCenter = center;

        if (totalRotation != Vector3.zero)
        {
            ApplyRotation(totalRotation);

            if (resetScript != null)
            {
                resetScript.ResetActivityTimer();
            }
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

                    if (!ownedPrevPositions.ContainsKey(id))
                        ownedPrevPositions.Add(id, t.position);
                    else
                        ownedPrevPositions[id] = t.position;
                }
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {

                if (ownedPrevPositions.ContainsKey(id))
                    ownedPrevPositions.Remove(id);
            }

        }


        List<int> toRemove = null;
        foreach (int ownedId in ownedPrevPositions.Keys)
        {
            bool exists = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Input.touches[i].fingerId == ownedId) { exists = true; break; }
            }
            if (!exists)
            {
                if (toRemove == null) toRemove = new List<int>();
                toRemove.Add(ownedId);
            }
        }

        if (toRemove != null)
        {
            foreach (int id in toRemove) ownedPrevPositions.Remove(id);
        }
    }


    private List<Touch> GetOwnedActiveTouches()
    {
        List<Touch> list = new List<Touch>();
        if (Input.touchCount == 0) return list;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.touches[i];
            if (ownedPrevPositions.ContainsKey(t.fingerId))
                list.Add(t);
        }
        return list;
    }

    private bool RaycastHitThisObject(Vector2 screenPos)
    {
        if (mainCamera == null) mainCamera = Camera.main;
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            return hit.collider != null && hit.collider.gameObject == gameObject;
        }
        return false;
    }

    private Vector2 ComputeCenter(List<Touch> touches)
    {
        Vector2 sum = Vector2.zero;
        if (touches == null || touches.Count == 0) return sum;
        foreach (Touch t in touches)
        {
            sum += t.position;
        }
        return sum
    }

    private bool CenterHasEnoughRadius(List<Touch> touches, Vector2 center)
    {
        if (touches == null || touches.Count == 0) return false;
        float sum = 0f;
        foreach (Touch t in touches)
        {
            sum += Vector2.Distance(t.position, center);
        }
        float avg = sum
        return avg >= MinRadiusPixels;
    }

    private void ApplyRotation(Vector3 rotation)
    {
        if (Target == null) return;

        if (UseWorldSpace)
        {
            if (rotation.x != 0) Target.Rotate(Vector3.right, rotation.x, Space.World);
            if (rotation.y != 0) Target.Rotate(Vector3.up, rotation.y, Space.World);
            if (rotation.z != 0) Target.Rotate(Vector3.forward, rotation.z, Space.World);
        }
        else
        {
            if (rotation.x != 0) Target.Rotate(Vector3.right, rotation.x, Space.Self);
            if (rotation.y != 0) Target.Rotate(Vector3.up, rotation.y, Space.Self);
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
                mouseScreenCenter = new Vector2(Screen.width
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

        Vector2 currPos = Input.mousePosition;
        Vector2 deltaMove = currPos - lastMousePos;
        Vector3 rotation = Vector3.zero;

        if (RotateX)
        {
            rotation.x -= deltaMove.y * Sensitivity * 0.1f;
        }

        if (RotateY)
        {
            rotation.y += deltaMove.x * Sensitivity * 0.1f;
        }

        if (RotateZ)
        {
            Vector2 prevVec = lastMousePos - mouseScreenCenter;
            Vector2 currVec = currPos - mouseScreenCenter;

            if (prevVec.sqrMagnitude > 0.0001f && currVec.sqrMagnitude > 0.0001f)
            {
                float anglePrev = Mathf.Atan2(prevVec.y, prevVec.x);
                float angleCurr = Mathf.Atan2(currVec.y, currVec.x);
                float delta = Mathf.DeltaAngle(anglePrev * Mathf.Rad2Deg, angleCurr * Mathf.Rad2Deg);

                Vector2 tangent = new Vector2(-currVec.y, currVec.x).normalized;
                float align = Mathf.Abs(Vector2.Dot(deltaMove.normalized, tangent));

                if (align >= TangentAlignmentThreshold)
                {
                    rotation.z += delta * Sensitivity * 0.5f;
                }
            }
        }

        if (rotation != Vector3.zero)
        {
            ApplyRotation(rotation);
            if (resetScript != null)
            {
                resetScript.ResetActivityTimer();
            }
        }

        lastMousePos = currPos;
    }
    #endregion
}
