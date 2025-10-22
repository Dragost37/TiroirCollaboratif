using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


public class ObjectCreator : MonoBehaviour
{
    public Transform planeTransform; // le plane sur lequel on dessine
    public int textureWidth = 1024;  // largeur de la texture pour conversion
    public GameObject woodPrefab; // Prefab désactivé à dupliquer
    public GameObject screwPrefab; // Prefab désactivé à dupliquer
    public GameObject gameObjectScripts;
    public Material material;

    public void CreateSquareObject(Vector2 center, float size)
    {
        // Convertir UV en position locale sur le plane sans effet miroir
        Vector3 localPos = new Vector3(
            5f - (center.x / textureWidth) * 10f,
            0f,
            5f - (center.y / textureWidth) * 10f
        );

        // Position dans le monde
        Vector3 worldPos = planeTransform.TransformPoint(localPos);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = worldPos + Vector3.up * 0.5f;
        cube.transform.localScale = new Vector3(size * 0.01f, 0.1f, size * 0.01f);
        cube.GetComponent<Renderer>().material = material;

        // Ajouter les scripts communs si nécessaire
        CopyAllScripts(gameObjectScripts, cube);
    }

    public void CreateRectangleObject(Vector2 center, float width, float height)
    {
        // Convertir UV en position locale sur le plane sans effet miroir
        Vector3 localPos = new Vector3(
            5f - (center.x / textureWidth) * 10f,
            0f,
            5f - (center.y / textureWidth) * 10f
        );

        // Position dans le monde
        Vector3 worldPos = planeTransform.TransformPoint(localPos);

        GameObject rectangle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rectangle.transform.position = worldPos + Vector3.up * 0.5f;
        rectangle.transform.localScale = new Vector3(width * 0.01f, 0.1f, height * 0.01f);
        rectangle.GetComponent<Renderer>().material = material;

        // Ajouter les scripts communs si nécessaire
        CopyAllScripts(gameObjectScripts, rectangle);
    }

    public void CreateScrewObject()
    {
        if (screwPrefab == null)
        {
            Debug.LogWarning("Le prefab 'screw' n'est pas assigné dans l'inspecteur !");
            return;
        }

        GameObject newScrew = Instantiate(screwPrefab, Vector3.zero, Quaternion.identity);

        newScrew.SetActive(true);

        newScrew.transform.position = new Vector3(0, 0.5f, 0);
        newScrew.transform.rotation = Quaternion.Euler(90, 0, 0);

        // Ajouter les scripts communs si nécessaire
        CopyAllScripts(gameObjectScripts, newScrew);
    }
    
    public void CreateWoodObject()
    {
        if (woodPrefab == null)
        {
            Debug.LogWarning("Le prefab 'wood' n'est pas assigné dans l'inspecteur !");
            return;
        }

        GameObject newWood = Instantiate(woodPrefab, Vector3.zero, Quaternion.identity);

        newWood.SetActive(true);

        newWood.transform.position = new Vector3(0, 0.5f, 0);
        newWood.transform.rotation = Quaternion.Euler(90, 0, 0);

        // Ajouter les scripts communs si nécessaire
        CopyAllScripts(gameObjectScripts, newWood);
    }
    private void CopyAllScripts(GameObject source, GameObject target)
    {
        if (!source || !target) return;

        var srcBehaviours = source.GetComponents<MonoBehaviour>();

        // 1) Créer TOUTES les instances (pas de filtre anti-doublon)
        var map = new Dictionary<Component, Component>(srcBehaviours.Length);
        foreach (var src in srcBehaviours)
        {
            if (!src) continue;
            var dst = target.AddComponent(src.GetType());
            map[src] = dst;
        }

        // 2) Copier champs sérialisés en REMAPPANT les références
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var src in srcBehaviours)
        {
            if (!src) continue;
            var dst = map[src];
            var type = src.GetType();

            foreach (var f in type.GetFields(flags))
            {
                bool isSerialized = f.IsPublic || System.Attribute.IsDefined(f, typeof(SerializeField));
                if (!isSerialized) continue;

                object value = f.GetValue(src);
                f.SetValue(dst, RemapValue(value, source, target, map));
            }
        }

        // 3) Post-fix : binder les FCA du CUBE sur SON TouchInteractable, puis resync
        var targetTI = target.GetComponent<TouchInteractable>();
        var fcas = target.GetComponents<FingerCountActivator>();
        foreach (var fca in fcas)
        {
            if (targetTI) fca.interactable = targetTI;
            // éteindre tes "modes" par défaut (évite le ON initial)
            if (fca.logicScripts != null)
                foreach (var mb in fca.logicScripts) if (mb) mb.enabled = false;

            fca.enabled = true;      // s'assure qu'il est actif
            fca.ForceSync();         // <— IMPORTANT : applique l'état (count=0 à la création)
        }

        // 4) DEBUG : imprime ce qu’il y a vraiment sur le target
        Debug.Log($"[CopyAllScripts] Target '{target.name}' → TI={(targetTI ? targetTI.name : "none")}  FCA count={fcas.Length}");
        int i = 0;
        foreach (var fca in fcas)
            Debug.Log($"[CopyAllScripts]   FCA[{i++}] rule={fca.ruleMode} thr={fca.threshold}/{fca.thresholdMax} interactable={(fca.interactable ? fca.interactable.name : "null")}");
    }

    private object RemapValue(object value, GameObject srcGO, GameObject dstGO, Dictionary<Component, Component> map)
    {
        if (value == null) return null;

        if (value is Component c)
        {
            if (map.TryGetValue(c, out var mapped)) return mapped;
            if (c.gameObject == srcGO)
            {
                var dstComp = dstGO.GetComponent(c.GetType());
                if (dstComp) return dstComp;
            }
            return value;
        }

        if (value is GameObject go) return go == srcGO ? dstGO : value;

        if (value is Object[] arr)
        {
            var elemType = value.GetType().GetElementType();
            var newArr = (Object[])System.Array.CreateInstance(elemType, arr.Length);
            for (int i = 0; i < arr.Length; i++)
                newArr[i] = (Object)RemapValue(arr[i], srcGO, dstGO, map);
            return newArr;
        }

        var t = value.GetType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elemType = t.GetGenericArguments()[0];
            if (typeof(Object).IsAssignableFrom(elemType))
            {
                var srcList = (IList)value;
                var dstList = (IList)System.Activator.CreateInstance(t);
                for (int i = 0; i < srcList.Count; i++)
                    dstList.Add(RemapValue(srcList[i], srcGO, dstGO, map));
                return dstList;
            }
        }

        return value;
    }

}
