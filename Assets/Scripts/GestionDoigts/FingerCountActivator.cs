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
    [Tooltip("Composant TouchInteractable à écouter (sur le même objet par défaut).")]
    public TouchInteractable interactable;

    [Header("Règle d’activation")]
    public RuleMode ruleMode = RuleMode.AtLeast;
    [Tooltip("Seuil principal (utilisé par Equal/AtLeast/AtMost et min pour Between).")]
    public int threshold = 1;
    [Tooltip("Seuil max (utilisé seulement pour BetweenInclusive).")]
    public int thresholdMax = 2;

    [Header("Cibles à activer/désactiver")]
    [Tooltip("Scripts de logique à activer quand la règle est vraie, sinon désactiver.")]
    public MonoBehaviour[] logicScripts;

    [Tooltip("Objets ou scripts de feedback visuel à activer quand la règle est vraie, sinon désactiver.")]
    public Behaviour[] visualScripts; // ex: ParticleSystem (en tant que Behaviour), LineRenderer, etc.
    public GameObject[] visualObjects; // ex: halos, highlights, UI, etc.

    [Header("Options")]
    [Tooltip("Inverser le résultat (active quand la règle est FAUSSE).")]
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

        // Activer/Désactiver les cibles
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
