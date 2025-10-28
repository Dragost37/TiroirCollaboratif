using UnityEngine;
using System.Collections.Generic;

public class ShapeRecognizer : MonoBehaviour
{
    public ObjectCreator objectCreator;
    public GameObject radialMenu;

    [Header("Seuils de détection")]
    [Range(0f, 1f)] public float circleToleranceFactor = 0.25f;  // Tolérance pour cercle
    [Range(0f, 50f)] public float minCornerAngle = 40f;          // Angle minimum pour un coin de triangle

    public void AnalyzeShape(List<Vector2> points)
    {
        if (points == null || points.Count < 3)
        {
            Debug.Log("Pas assez de points pour analyser");
            return;
        }

        // Calcul de la bounding box et longueur totale du tracé
        float minX = points[0].x, maxX = points[0].x;
        float minY = points[0].y, maxY = points[0].y;
        float totalLength = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            totalLength += Vector2.Distance(points[i - 1], points[i]);

            if (points[i].x < minX) minX = points[i].x;
            if (points[i].x > maxX) maxX = points[i].x;
            if (points[i].y < minY) minY = points[i].y;
            if (points[i].y > maxY) maxY = points[i].y;
        }

        float width = maxX - minX;
        float height = maxY - minY;

        // Forme trop petite
        if (width < 0.05f && height < 0.05f)
            return;

        // Calcul du centre
        Vector2 center = new Vector2(minX + width / 2, minY + height / 2);

        // Détection de trait en se basant sur la longueur et la diagonale
        float diagonal = Mathf.Sqrt(width * width + height * height);
        float straightness = diagonal / totalLength; // proche de 1 = ligne droite

        if (straightness > 0.95f) // seuil ajustable
        {
            Debug.Log("Trait détecté (même en diagonale)");
            radialMenu.SetActive(true);
            return;
        }

        // Autres formes
        float aspectRatio = width > height ? width / height : height / width;

        if (aspectRatio < 1.5f)
        {
            Debug.Log("Carré détecté");
            objectCreator.CreateSquareObject(center, width);
        }
        else
        {
            Debug.Log("Rectangle détecté");
            objectCreator.CreateRectangleObject(center, width, height);
        }
    }
}