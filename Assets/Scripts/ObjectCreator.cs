using UnityEngine;

public class ObjectCreator : MonoBehaviour
{
    public Transform planeTransform; // le plane sur lequel on dessine
    public int textureWidth = 1024;  // largeur de la texture pour conversion

    public void CreateSquareObject(Vector2 center, float size)
    {
        // Convertir UV en position locale sur le plane
        Vector3 localPos = new Vector3(
            (center.x / textureWidth - 0.5f) * 10f,
            0f,
            (center.y / textureWidth - 0.5f) * 10f
        );

        // Position dans le monde
        Vector3 worldPos = planeTransform.TransformPoint(localPos);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = worldPos + Vector3.up * 0.5f;
        cube.transform.localScale = new Vector3(size * 0.01f, 0.1f, size * 0.01f);
        cube.GetComponent<Renderer>().material.color = Color.red;

        // Ajouter automatiquement le script PartController
        cube.AddComponent<PartController>();
    }
}
