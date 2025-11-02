using UnityEngine;

/// <summary>
/// Définit un point d'accroche pour les pièces DraggablePart.
/// </summary>
public class SnapPoint : MonoBehaviour
{
    [Header("Identification")]
    [Tooltip("Doit correspondre au champ compatibleSnapTag dans la pièce DraggablePart.")]
    public string snapTag;

    [Header("État")]
    [Tooltip("Indique si ce point est déjà occupé par une pièce.")]
    public bool occupied;

    /// <summary>
    /// Appelée lorsqu'une pièce s'accroche à ce SnapPoint.
    /// </summary>
    public void OnSnapped(GameObject partGO)
    {
        occupied = true;

        // joue un son si un AudioSource est attaché
        var audio = GetComponent<AudioSource>();
        if (audio != null)
            audio.Play();
    }

    /// <summary>
    /// Libère ce point (si la pièce est retirée, par exemple).
    /// </summary>
    public void Release()
    {
        occupied = false;
    }

#if UNITY_EDITOR
    // Pour mieux visualiser le snap point dans l'éditeur
    private void OnDrawGizmos()
    {
        Gizmos.color = occupied ? Color.red : Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.03f);
    }
#endif
}
