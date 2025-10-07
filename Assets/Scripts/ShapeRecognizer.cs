using UnityEngine;
using System.Collections.Generic;

public class ShapeRecognizer : MonoBehaviour
{
    public ObjectCreator objectCreator;

    // Analyse une forme dessinée et détecte un carré ou un rectangle
    public void AnalyzeShape(List<Vector2> points)
    {
        Debug.Log("Analyse de la forme dessinée avec " + points.Count + " points");
        if (points.Count < 10) 
        {
            Debug.Log("Forme trop petite pour être reconnue");
            return;
        }

        float minX = points[0].x, maxX = points[0].x;
        float minY = points[0].y, maxY = points[0].y;

        foreach (var p in points)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        float width = maxX - minX;
        float height = maxY - minY;

        float aspectRatio = width > height ? width / height : height / width;
        if (aspectRatio < 1.5f)
        {
            Debug.Log("Carré détecté");
            Vector2 center = new Vector2(minX + width / 2, minY + height / 2);
            objectCreator.CreateSquareObject(center, width);
        }
        else
        {
            Debug.Log("Rectangle détecté");
            Vector2 center = new Vector2(minX + width / 2, minY + height / 2);
            objectCreator.CreateRectangleObject(center, width, height);
        }
    }
}