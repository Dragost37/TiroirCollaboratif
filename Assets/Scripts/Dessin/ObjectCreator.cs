using UnityEngine;

public class ObjectCreator : MonoBehaviour
{
    public Transform planeTransform; // le plane sur lequel on dessine
    public int textureWidth = 1024;  // largeur de la texture pour conversion
    public GameObject woodPrefab; // Prefab désactivé à dupliquer
    public GameObject screwPrefab; // Prefab désactivé à dupliquer
    public GameObject gameObjectScripts;

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
        cube.GetComponent<Renderer>().material.color = Color.red;

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
        rectangle.GetComponent<Renderer>().material.color = Color.blue;

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
        if (source == null || target == null)
            return;

        MonoBehaviour[] sourceScripts = source.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour sourceScript in sourceScripts)
        {
            if (sourceScript == null)
                continue;

            System.Type type = sourceScript.GetType();
            if (target.GetComponent(type) != null)
                continue;

            MonoBehaviour targetScript = (MonoBehaviour)target.AddComponent(type);

            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.IsPublic || field.GetCustomAttributes(typeof(SerializeField), true).Length > 0)
                {
                    field.SetValue(targetScript, field.GetValue(sourceScript));
                }
            }
        }
    }
}
