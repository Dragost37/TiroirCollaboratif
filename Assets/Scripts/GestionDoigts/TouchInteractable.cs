using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class IntEvent : UnityEvent<int> { }

public class TouchInteractable : MonoBehaviour
{
    // Ensemble des doigts actuellement pos�s sur CET objet
    private readonly HashSet<int> _fingers = new();

    [Tooltip("�v�nement �mis quand le nombre de doigts change (valeur = count actuel).")]
    public IntEvent OnFingerCountChanged = new();

    public int CurrentFingerCount => _fingers.Count;

    /// <summary>Appel� par TouchManager quand un nouveau doigt touche cet objet.</summary>
    public void AddFinger(int fingerId)
    {
        if (_fingers.Add(fingerId))
        {
            OnFingerCountChanged.Invoke(_fingers.Count);
        }
    }

    /// <summary>Appel� par TouchManager quand le doigt quitte l��cran/objet.</summary>
    public void RemoveFinger(int fingerId)
    {
        if (_fingers.Remove(fingerId))
        {
            OnFingerCountChanged.Invoke(_fingers.Count);
        }
    }

    /// <summary>Force la remise � z�ro (utile si vous d�sactivez l�objet � chaud).</summary>
    public void ClearAllFingers()
    {
        if (_fingers.Count > 0)
        {
            _fingers.Clear();
            OnFingerCountChanged.Invoke(0);
        }
    }
}
