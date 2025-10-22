using UnityEngine;

public class FingerCountActivator : MonoBehaviour
{
    public enum RuleMode { Equal, AtLeast, AtMost, BetweenInclusive }

    [Header("Source")]
    public TouchInteractable interactable;

    [Header("Règle d’activation")]
    public RuleMode ruleMode = RuleMode.AtLeast;
    public int threshold = 1;
    public int thresholdMax = 2;
    public bool invert;

    [Header("Cibles à activer/désactiver")]
    public MonoBehaviour[] logicScripts;
    public Behaviour[] visualScripts;
    public GameObject[] visualObjects;

    [Header("Debug")]
    public bool debug = true;

    private void Reset()
    {
        interactable = GetComponent<TouchInteractable>();
    }

    private void OnEnable()
    {
        if (interactable == null) interactable = GetComponent<TouchInteractable>();

        if (interactable != null)
        {
            interactable.OnFingerCountChanged.AddListener(OnFingerCountChanged);
            if (debug) Debug.Log($"[FCA:{name}#{GetInstanceID()}] OnEnable → subscribe to {interactable.name}, rule={DescribeRule()} invert={invert}");
            // synchro immédiate
            OnFingerCountChanged(interactable.CurrentFingerCount);
        }
        else
        {
            if (debug) Debug.LogWarning($"[FCA:{name}] OnEnable → NO TouchInteractable found. Forcing OFF.");
            Apply(false, reason: "no-interactable");
        }
    }

    private void OnDisable()
    {
        if (interactable != null)
        {
            interactable.OnFingerCountChanged.RemoveListener(OnFingerCountChanged);
            if (debug) Debug.Log($"[FCA:{name}#{GetInstanceID()}] OnDisable → unsubscribed");
        }
    }

    private void OnFingerCountChanged(int count)
    {
        bool pass = Evaluate(count);
        bool final = invert ? !pass : pass;

        if (debug)
            Debug.Log($"[FCA:{name}#{GetInstanceID()}] CountChanged → count={count} rule={DescribeRule()} pass={pass} invert={invert} => final={final}");

        Apply(final, reason: $"count={count}");
    }

    private void Apply(bool state, string reason)
    {
        if (logicScripts != null)
        {
            foreach (var mb in logicScripts)
            {
                if (!mb) continue;
                if (mb.enabled != state && debug)
                    Debug.Log($"[FCA:{name}#{GetInstanceID()}] Toggle LOGIC '{mb.GetType().Name}' on {mb.gameObject.name} → {state} ({reason})");
                mb.enabled = state;
            }
        }

        if (visualScripts != null)
        {
            foreach (var beh in visualScripts)
            {
                if (!beh) continue;
                if (beh.enabled != state && debug)
                    Debug.Log($"[FCA:{name}#{GetInstanceID()}] Toggle VISUAL '{beh.GetType().Name}' on {beh.gameObject.name} → {state} ({reason})");
                beh.enabled = state;
            }
        }

        if (visualObjects != null)
        {
            foreach (var go in visualObjects)
            {
                if (!go) continue;
                if (go.activeSelf != state && debug)
                    Debug.Log($"[FCA:{name}#{GetInstanceID()}] Toggle GO '{go.name}' → {state} ({reason})");
                go.SetActive(state);
            }
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

    private string DescribeRule()
    {
        return ruleMode switch
        {
            RuleMode.Equal => $"== {threshold}",
            RuleMode.AtLeast => $">= {threshold}",
            RuleMode.AtMost => $"<= {threshold}",
            RuleMode.BetweenInclusive => $"{threshold} ≤ N ≤ {thresholdMax}",
            _ => "?"
        };
    }

    public void ForceSync()
    {
        int count = interactable ? interactable.CurrentFingerCount : 0;
        OnFingerCountChanged(count);
    }

}
