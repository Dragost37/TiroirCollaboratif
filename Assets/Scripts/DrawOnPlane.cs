using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(Renderer))]
public class DrawOnPlane : MonoBehaviour
{
    public int textureSize = 1024;
    public Color drawColor = Color.black;
    public float brushSize = 2f;

    private Texture2D texture;
    private Renderer rend;

    private Dictionary<int, Vector2?> lastDrawPositions = new Dictionary<int, Vector2?>();
    public Dictionary<int, List<Vector2>> drawPaths = new Dictionary<int, List<Vector2>>();

    public ShapeRecognizer shapeRecognizer;

    void Start()
    {
        rend = GetComponent<Renderer>();
        texture = new Texture2D(textureSize, textureSize);
        texture.Apply();
        rend.material.mainTexture = texture;
    }

    void Update()
    {
        HandleTouchInput();
        HandleMouseInput();
    }

    void HandleTouchInput()
    {
        if (Touchscreen.current == null) return;

        foreach (var touch in Touchscreen.current.touches)
        {
            int touchId = touch.touchId.ReadValue();
            if (touch.press.isPressed)
            {
                Vector2 touchPos = touch.position.ReadValue();
                ProcessDrawing(touchId, touchPos);
            }
            else
            {
                // Lorsque le doigt est levé, lancer l'analyse de la forme
                if (drawPaths.ContainsKey(touchId))
                {
                    shapeRecognizer.AnalyzeShape(drawPaths[touchId]);
                    drawPaths.Remove(touchId);
                }
                lastDrawPositions.Remove(touchId);
            }
        }
    }

    void HandleMouseInput()
    {
        int mouseId = -1;
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            ProcessDrawing(mouseId, mousePos);
        }
        else
        {
            // Lorsque le bouton souris est relâché, lancer l'analyse de la forme
            if (drawPaths.ContainsKey(mouseId))
            {
                shapeRecognizer.AnalyzeShape(drawPaths[mouseId]);
                drawPaths.Remove(mouseId);
            }
            lastDrawPositions.Remove(mouseId);
        }
    }

    void ProcessDrawing(int id, Vector2 screenPos)
    {
        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.gameObject == gameObject)
        {
            Vector2 uv = hit.textureCoord;
            int x = (int)(uv.x * texture.width);
            int y = (int)(uv.y * texture.height);
            Vector2 currentPos = new Vector2(x, y);

            if (!drawPaths.ContainsKey(id)) drawPaths[id] = new List<Vector2>();
            drawPaths[id].Add(currentPos);

            Vector2? lastPos = null;
            lastDrawPositions.TryGetValue(id, out lastPos);
            if (lastPos.HasValue) DrawLine(lastPos.Value, currentPos);
            else DrawCircle(x, y);

            lastDrawPositions[id] = currentPos;
            texture.Apply();
        }
    }

    void DrawCircle(int x, int y)
    {
        for (int i = -(int)brushSize; i <= brushSize; i++)
        {
            for (int j = -(int)brushSize; j <= brushSize; j++)
            {
                if (i * i + j * j <= brushSize * brushSize)
                {
                    int px = x + i;
                    int py = y + j;
                    if (px >= 0 && px < texture.width && py >= 0 && py < texture.height)
                        texture.SetPixel(px, py, drawColor);
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
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }
}