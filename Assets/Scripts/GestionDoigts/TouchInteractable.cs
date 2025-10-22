using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable] public class IntEvent : UnityEvent<int> { }

public class TouchInteractable : MonoBehaviour
{
    [Header("Debug")]
    public bool debug = true;

    private readonly HashSet<int> _fingers = new();

    [Tooltip("Événement émis quand le nombre de doigts change (valeur = count actuel).")]
    public IntEvent OnFingerCountChanged = new();

    public int CurrentFingerCount => _fingers.Count;

    public void AddFinger(int fingerId)
    {
        if (_fingers.Add(fingerId))
        {
            if (debug) Debug.Log($"[TouchInteractable:{name}] AddFinger {fingerId} → count={_fingers.Count}");
            OnFingerCountChanged.Invoke(_fingers.Count);
        }
        else
        {
            if (debug) Debug.Log($"[TouchInteractable:{name}] AddFinger ignored (already present) id={fingerId} → count={_fingers.Count}");
        }
    }

    public void RemoveFinger(int fingerId)
    {
        if (_fingers.Remove(fingerId))
        {
            if (debug) Debug.Log($"[TouchInteractable:{name}] RemoveFinger {fingerId} → count={_fingers.Count}");
            OnFingerCountChanged.Invoke(_fingers.Count);
        }
        else
        {
            if (debug) Debug.Log($"[TouchInteractable:{name}] RemoveFinger ignored (unknown) id={fingerId} → count={_fingers.Count}");
        }
    }

    public void ClearAllFingers()
    {
        if (_fingers.Count > 0)
        {
            if (debug) Debug.Log($"[TouchInteractable:{name}] ClearAllFingers (had {_fingers.Count})");
            _fingers.Clear();
            OnFingerCountChanged.Invoke(0);
        }
    }

    private void OnEnable()
    {
        if (debug) Debug.Log($"[TouchInteractable:{name}] OnEnable → count={_fingers.Count}");
    }

    private void OnDisable()
    {
        if (debug) Debug.Log($"[TouchInteractable:{name}] OnDisable");
    }
}
