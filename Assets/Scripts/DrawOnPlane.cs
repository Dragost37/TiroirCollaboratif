using UnityEngine;
using UnityEngine.InputSystem;

public class DrawOnPlane : MonoBehaviour
{
    public int textureSize = 1024;
    public Color drawColor = Color.black;
    public float brushSize = 10f;

    private Texture2D texture;
    private Renderer rend;
    // Pour multi-touch : stocke la dernière position de chaque doigt (touchId)
    private System.Collections.Generic.Dictionary<int, Vector2?> lastDrawPositions = new System.Collections.Generic.Dictionary<int, Vector2?>();

    void Start()
    {
        rend = GetComponent<Renderer>();
        texture = new Texture2D(textureSize, textureSize);
        texture.Apply();
        rend.material.mainTexture = texture;
    }

    void Update()
    {
        // Utilisation du système de touch du New Input System
        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                int touchId = touch.touchId.ReadValue();
                // isPressed est vrai tant que le doigt est posé
                if (touch.press.isPressed)
                {
                    Vector2 touchPos = touch.position.ReadValue();
                    Ray ray = Camera.main.ScreenPointToRay(touchPos);
                    if (Physics.Raycast(ray, out RaycastHit hit))
                    {
                        if (hit.collider.gameObject == gameObject)
                        {
                            Vector2 uv = hit.textureCoord;
                            int x = (int)(uv.x * texture.width);
                            int y = (int)(uv.y * texture.height);
                            Vector2 currentPos = new Vector2(x, y);

                            Vector2? lastPos = null;
                            lastDrawPositions.TryGetValue(touchId, out lastPos);
                            if (lastPos.HasValue)
                            {
                                DrawLine(lastPos.Value, currentPos);
                            }
                            else
                            {
                                DrawCircle(x, y);
                            }
                            lastDrawPositions[touchId] = currentPos;
                            texture.Apply();
                        }
                    }
                }
                else
                {
                    // Lorsque le doigt est levé, on oublie sa dernière position
                    if (lastDrawPositions.ContainsKey(touchId))
                        lastDrawPositions.Remove(touchId);
                }
            }
        }
        else
        {
            // Optionnel : support souris en fallback (désactivé ici)
            // lastDrawPositions.Clear();
        }
    }

    void DrawCircle(int x, int y)
    {
        for (int i = - (int)brushSize; i <= brushSize; i++)
        {
            for (int j = - (int)brushSize; j <= brushSize; j++)
            {
                if (i * i + j * j <= brushSize * brushSize)
                {
                    int px = x + i;
                    int py = y + j;
                    if (px >= 0 && px < texture.width && py >= 0 && py < texture.height)
                    {
                        texture.SetPixel(px, py, drawColor);
                    }
                }
            }
        }
    }

    void DrawLine(Vector2 from, Vector2 to)
    {
        int x0 = Mathf.RoundToInt(from.x);
        int y0 = Mathf.RoundToInt(from.y);
        int x1 = Mathf.RoundToInt(to.x);
        int y1 = Mathf.RoundToInt(to.y);

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            DrawCircle(x0, y0);
            if (x0 == x1 && y0 == y1)
                break;
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }
}