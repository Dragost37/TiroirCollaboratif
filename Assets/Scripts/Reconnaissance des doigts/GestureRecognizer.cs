using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Reconnaît uniquement le geste de déplacement à un doigt (pan).
/// </summary>
public class GestureRecognizer : MonoBehaviour
{
    [Header("Cible à déplacer")]
    public Transform target;

    [Header("Vitesse de déplacement")]
    [Tooltip("Facteur d’amplification du mouvement à l’écran vers le monde.")]
    public float moveSpeed = 0.005f;

    public event Action<Vector2> OnPan;

    private readonly Dictionary<int, Vector2> _positions = new();

    void OnEnable()
    {
        var mt = MultiTouchManager.Instance;
        if (mt != null)
        {
            mt.OnTouchBegan += B;
            mt.OnTouchMoved += M;
            mt.OnTouchEnded += E;
        }
        else
        {
            Debug.LogWarning("[GestureRecognizer] MultiTouchManager.Instance est null.");
        }
    }

    void OnDisable()
    {
        var mt = MultiTouchManager.Instance;
        if (mt != null)
        {
            mt.OnTouchBegan -= B;
            mt.OnTouchMoved -= M;
            mt.OnTouchEnded -= E;
        }
    }

    void B(MultiTouchManager.TouchEvt e)
    {
        _positions[e.fingerId] = e.position;
    }

    void M(MultiTouchManager.TouchEvt e)
    {
        _positions[e.fingerId] = e.position;
        Process();
    }

    void E(MultiTouchManager.TouchEvt e)
    {
        _positions.Remove(e.fingerId);
    }

    void Process()
    {
        // Un seul doigt : déplacement (pan)
        if (_positions.Count == 1)
        {
            foreach (var kv in _positions)
            {
                var delta = DeltaForFinger(kv.Key);
                OnPan?.Invoke(delta);
                ApplyPan(delta);
            }
        }
    }

    Vector2 DeltaForFinger(int fingerId)
    {
        foreach (var t in Input.touches)
            if (t.fingerId == fingerId)
                return t.deltaPosition;
        return Vector2.zero;
    }

    void ApplyPan(Vector2 delta)
    {
        if (!target) return;
        var cam = Camera.main;
        if (!cam) return;

        // Conversion du mouvement écran -> mouvement monde
        var move = new Vector3(delta.x * moveSpeed, delta.y * moveSpeed, 0);
        target.Translate(move, Space.World);
    }
}
