using UnityEngine;
using System;
using System.Collections.Generic;

[Serializable] public class StepSpec{ public int id; public string targetPart; public string snapTo; public string description; }
[Serializable] public class AssemblySpec{ public string assemblyId; public string title; public List<StepSpec> steps; }

public class AssemblyManager : MonoBehaviour
{
    public string resourcePath = "Assemblies/stool"; // Resources/Assemblies/stool.json
    public Material highlightMat; private Dictionary<string, GameObject> _parts = new();
    private AssemblySpec _spec; private int _currentIndex=0; private Dictionary<GameObject, Material> _origMat = new();

    void Start(){ Load(); IndexParts(); HighlightCurrent(); }

    void Load(){ var txt = Resources.Load<TextAsset>(resourcePath); _spec = JsonUtility.FromJson<AssemblySpec>(txt.text); }

    void IndexParts(){ foreach(var go in GameObject.FindObjectsOfType<PartController>()) _parts[go.name]=go.gameObject; }

    public void ValidateStep(GameObject justSnapped)
    {
        if(_currentIndex>=_spec.steps.Count) return; var step=_spec.steps[_currentIndex]; if(justSnapped.name==step.targetPart){ _currentIndex++; HighlightCurrent(); }
    }

    void HighlightCurrent()
    {
        foreach(var kv in _origMat){ kv.Key.GetComponent<Renderer>().material = kv.Value; } _origMat.Clear();
        if(_currentIndex>=_spec.steps.Count) return; var step=_spec.steps[_currentIndex];
        if(_parts.TryGetValue(step.targetPart, out var go))
        {
            var r = go.GetComponent<Renderer>(); if(r){ _origMat[go]=r.material; r.material = highlightMat; }
        }
    }
}
