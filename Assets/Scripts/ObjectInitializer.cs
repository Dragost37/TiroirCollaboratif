using UnityEngine;
using System;
using System.Collections.Generic;

public class ObjectInitializer : MonoBehaviour
{
    // Liste des types de scripts à ajouter
    public List<MonoBehaviour> scriptTemplates;

    // Singleton simple (facultatif mais pratique)
    public static ObjectInitializer Instance;

    void Awake()
    {
        Instance = this;
    }

    // Méthode publique pour initialiser un objet
    public void ApplyScripts(GameObject target)
    {
        foreach (var scriptTemplate in scriptTemplates)
        {
            Type scriptType = scriptTemplate.GetType();

            // Vérifie que le script n’est pas déjà sur l’objet
            if (target.GetComponent(scriptType) == null)
            {
                target.AddComponent(scriptType);
            }
        }
    }
}