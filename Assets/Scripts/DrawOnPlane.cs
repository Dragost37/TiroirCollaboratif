using UnityEngine;
using UnityEngine.InputSystem;

public class DrawOnPlane : MonoBehaviour
{
    public int textureSize = 1024;
    public Color drawColor = Color.black;
    public float brushSize = 10f;

    private Texture2D texture;
    private Renderer rend;
    private Vector2? lastDrawPosition = null;

    void Start()
    {
        rend = GetComponent<Renderer>();
        texture = new Texture2D(textureSize, textureSize);
        texture.Apply();
        rend.material.mainTexture = texture;
    }

    void Update()
    {
        if (Mouse.current.leftButton.isPressed)
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.gameObject == gameObject)
                {
                    Vector2 uv = hit.textureCoord;
                    int x = (int)(uv.x * texture.width);
                    int y = (int)(uv.y * texture.height);
                    Vector2 currentPos = new Vector2(x, y);

                    if (lastDrawPosition.HasValue)
                    {
                        DrawLine(lastDrawPosition.Value, currentPos);
                    }
                    else
                    {
                        DrawCircle(x, y);
                    }
                    lastDrawPosition = currentPos;
                    texture.Apply();
                }
            }
        }
        else
        {
            lastDrawPosition = null;
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