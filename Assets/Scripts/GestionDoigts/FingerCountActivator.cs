using UnityEngine;

public class FingerCountActivator : MonoBehaviour
{
    public enum RuleMode
    {
        Equal,            // == N
        AtLeast,          // >= N
        AtMost,           // <= N
        BetweenInclusive  // min <= N <= max
    }

    [Header("Source")]
    [Tooltip("Composant TouchInteractable � �couter (sur le m�me objet par d�faut).")]
    public TouchInteractable interactable;

    [Header("R�gle d�activation")]
    public RuleMode ruleMode = RuleMode.AtLeast;
    [Tooltip("Seuil principal (utilis� par Equal/AtLeast/AtMost et min pour Between).")]
    public int threshold = 1;
    [Tooltip("Seuil max (utilis� seulement pour BetweenInclusive).")]
    public int thresholdMax = 2;

    [Header("Cibles � activer/d�sactiver")]
    [Tooltip("Scripts de logique � activer quand la r�gle est vraie, sinon d�sactiver.")]
    public MonoBehaviour[] logicScripts;

    [Tooltip("Objets ou scripts de feedback visuel � activer quand la r�gle est vraie, sinon d�sactiver.")]
    public Behaviour[] visualScripts; // ex: ParticleSystem (en tant que Behaviour), LineRenderer, etc.
    public GameObject[] visualObjects; // ex: halos, highlights, UI, etc.

    [Header("Options")]
    [Tooltip("Inverser le r�sultat (active quand la r�gle est FAUSSE).")]
    public bool invert;

    private void Reset()
    {
        interactable = GetComponent<TouchInteractable>();
    }

    private void Awake()
    {
        if (interactable == null) interactable = GetComponent<TouchInteractable>();
        if (interactable != null)
        {
            interactable.OnFingerCountChanged.AddListener(OnFingerCountChanged);
        }
    }

    private void OnEnable()
    {
        if (interactable != null)
            OnFingerCountChanged(interactable.CurrentFingerCount);
    }

    private void OnDestroy()
    {
        if (interactable != null)
            interactable.OnFingerCountChanged.RemoveListener(OnFingerCountChanged);
    }

    private void OnFingerCountChanged(int count)
    {
        bool pass = Evaluate(count);
        if (invert) pass = !pass;

        // Activer/D�sactiver les cibles
        if (logicScripts != null)
        {
            foreach (var mb in logicScripts)
                if (mb != null) mb.enabled = pass;
        }

        if (visualScripts != null)
        {
            foreach (var beh in visualScripts)
                if (beh != null) beh.enabled = pass;
        }

        if (visualObjects != null)
        {
            foreach (var go in visualObjects)
                if (go != null) go.SetActive(pass);
        }
    }

    private bool Evaluate(int count)
    {
        switch (ruleMode)
        {
            case RuleMode.Equal: return count == threshold;
            case RuleMode.AtLeast: return count >= threshold;
            case RuleMode.AtMost: return count <= threshold;
            case RuleMode.BetweenInclusive: return count >= threshold && count <= thresholdMax;
            default: return false;
        }
    }
}
