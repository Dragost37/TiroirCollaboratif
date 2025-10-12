using UnityEngine;
using System;
using System.Collections.Generic;

[Serializable]
public class StepSpec
{
    public int id;
    public string targetPart;  // Nom attendu de la pièce (GameObject.name)
    public string snapTo;      // Optionnel : nom du SnapPoint visé (si tu veux l'utiliser plus tard)
    public string description;
}

[Serializable]
public class AssemblySpec
{
    public string assemblyId;
    public string title;
    public List<StepSpec> steps;
}

public class AssemblyManager : MonoBehaviour
{
    [Header("Chargement")]
    [Tooltip("Chemin dans Resources sans l'extension. Ex: Resources/Assemblies/stool.json -> Assemblies/stool")]
    public string resourcePath = "Assemblies/stool"; // Resources/Assemblies/stool.json

    [Header("Feedback visuel")]
    public Material highlightMat;

    // Index des pièces (par nom). Attention: si plusieurs objets ont le même nom, le dernier gagne.
    private readonly Dictionary<string, GameObject> _parts = new();

    private AssemblySpec _spec;
    private int _currentIndex = 0;

    // Restauration des matériaux originaux : par Renderer
    private readonly Dictionary<Renderer, Material[]> _origMats = new();

    void Start()
    {
        Load();
        IndexParts();
        HighlightCurrent();
    }

    private void Load()
    {
        var txt = Resources.Load<TextAsset>(resourcePath);
        if (txt == null)
        {
            Debug.LogError($"[AssemblyManager] Impossible de charger '{resourcePath}'. " +
                           "Vérifie le chemin et que le fichier est bien dans un dossier 'Resources'.");
            _spec = new AssemblySpec { steps = new List<StepSpec>() };
            return;
        }

        try
        {
            _spec = JsonUtility.FromJson<AssemblySpec>(txt.text);
            if (_spec == null || _spec.steps == null)
            {
                Debug.LogError("[AssemblyManager] JSON invalide ou vide.");
                _spec = new AssemblySpec { steps = new List<StepSpec>() };
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AssemblyManager] Erreur de parsing JSON : {ex.Message}");
            _spec = new AssemblySpec { steps = new List<StepSpec>() };
        }
    }

    private void IndexParts()
    {
        _parts.Clear();

        // 👉 On indexe les pièces via DraggablePart (nouveau script)
        foreach (var dp in GameObject.FindObjectsOfType<DraggablePart>())
        {
            var go = dp.gameObject;
            if (_parts.ContainsKey(go.name))
            {
                Debug.LogWarning($"[AssemblyManager] Plusieurs objets nommés '{go.name}'. " +
                                 "Le dernier trouvé écrase les précédents.");
            }
            _parts[go.name] = go;
        }

        // (Optionnel) rétro-compat si d’anciens prefabs ont encore PartController :
        // foreach (var pc in GameObject.FindObjectsOfType<PartController>())
        // {
        //     var go = pc.gameObject;
        //     if (!_parts.ContainsKey(go.name))
        //         _parts[go.name] = go;
        // }
    }

    public void ValidateStep(GameObject justSnapped)
    {
        if (_spec == null || _spec.steps == null || _spec.steps.Count == 0) return;
        if (_currentIndex >= _spec.steps.Count) return;

        var step = _spec.steps[_currentIndex];

        // 1) Si on a l'objet référencé dans l’index et qu’il matche par référence, on valide.
        if (_parts.TryGetValue(step.targetPart, out var expectedGO))
        {
            if (expectedGO == justSnapped)
            {
                _currentIndex++;
                HighlightCurrent();
                return;
            }
        }

        // 2) Fallback : si les noms matchent strictement (utile si l’index n’a pas trouvé ou si clones)
        if (justSnapped != null && justSnapped.name == step.targetPart)
        {
            _currentIndex++;
            HighlightCurrent();
        }
    }

    private void HighlightCurrent()
    {
        // Restaure tous les rendus précédemment modifiés
        RestoreAll();

        if (_spec == null || _spec.steps == null || _currentIndex >= _spec.steps.Count) return;

        var step = _spec.steps[_currentIndex];

        if (!_parts.TryGetValue(step.targetPart, out var go) || go == null)
        {
            Debug.LogWarning($"[AssemblyManager] Pièce '{step.targetPart}' introuvable dans la scène.");
            return;
        }

        if (highlightMat == null)
        {
            Debug.LogWarning("[AssemblyManager] highlightMat n'est pas assigné.");
            return;
        }

        // On met en surbrillance tous les Renderers de l’objet (incluant enfants)
        var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: false);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            // Sauvegarde profonde des matériaux
            _origMats[r] = r.materials;
            // Remplace tout par le matériau de highlight
            var newMats = new Material[r.materials.Length];
            for (int i = 0; i < newMats.Length; i++) newMats[i] = highlightMat;
            r.materials = newMats;
        }
    }

    private void RestoreAll()
    {
        foreach (var kv in _origMats)
        {
            var r = kv.Key;
            if (r) r.materials = kv.Value;
        }
        _origMats.Clear();
    }

    // (Optionnel) pour naviguer manuellement
    public void NextStep()
    {
        if (_spec == null || _spec.steps == null) return;
        if (_currentIndex < _spec.steps.Count - 1)
        {
            _currentIndex++;
            HighlightCurrent();
        }
    }

    public void PreviousStep()
    {
        if (_currentIndex > 0)
        {
            _currentIndex--;
            HighlightCurrent();
        }
    }

    // (Optionnel) Reset depuis le début
    public void ResetSteps()
    {
        _currentIndex = 0;
        HighlightCurrent();
    }
}
