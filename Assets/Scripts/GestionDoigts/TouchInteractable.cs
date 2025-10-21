using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class IntEvent : UnityEvent<int> { }

public class TouchInteractable : MonoBehaviour
{
    // Ensemble des doigts actuellement posés sur CET objet
    private readonly HashSet<int> _fingers = new();

    [Tooltip("Événement émis quand le nombre de doigts change (valeur = count actuel).")]
    public IntEvent OnFingerCountChanged = new();

    public int CurrentFingerCount => _fingers.Count;

    /// <summary>Appelé par TouchManager quand un nouveau doigt touche cet objet.</summary>
    public void AddFinger(int fingerId)
    {
        if (_fingers.Add(fingerId))
        {
            OnFingerCountChanged.Invoke(_fingers.Count);
        }
    }

    /// <summary>Appelé par TouchManager quand le doigt quitte l’écran/objet.</summary>
    public void RemoveFinger(int fingerId)
    {
        if (_fingers.Remove(fingerId))
        {
            OnFingerCountChanged.Invoke(_fingers.Count);
        }
    }

    /// <summary>Force la remise à zéro (utile si vous désactivez l’objet à chaud).</summary>
    public void ClearAllFingers()
    {
        if (_fingers.Count > 0)
        {
            _fingers.Clear();
            OnFingerCountChanged.Invoke(0);
        }
    }
}
