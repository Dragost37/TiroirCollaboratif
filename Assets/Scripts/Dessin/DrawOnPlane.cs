using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(Renderer))]
public class DrawOnPlane : MonoBehaviour
{
    [Header("Brush")]
    public int textureSize = 1024;
    public Color drawColor = Color.black;
    public float brushSize = 2f;

    [Header("Texture Source")]
    public Texture sourceTexture;                  // texture de base à cloner (facultatif)
    public bool keepSourceSize = true;
    public string texturePropertyName = "_MainTex";

    [Header("Auto Clear")]
    public float clearDelay = 3f;

    private Renderer rend;
    private Texture2D texture;
    private Color[] basePixels;                    // <<< snapshot de la texture d'origine

    private Dictionary<int, Vector2?> lastDrawPositions = new Dictionary<int, Vector2?>();
    public Dictionary<int, List<Vector2>> drawPaths = new Dictionary<int, List<Vector2>>();

    public ShapeRecognizer shapeRecognizer;

    private float lastDrawTime = -999f;

    void Start()
    {
        rend = GetComponent<Renderer>();
        var mat = rend.material;

        // 1) base
        Texture baseTex = sourceTexture != null ? sourceTexture : mat.GetTexture(texturePropertyName);

        // 2) size
        int w = textureSize, h = textureSize;
        if (keepSourceSize && baseTex != null)
        {
            if (baseTex is Texture2D t2d) { w = Mathf.Max(1, t2d.width); h = Mathf.Max(1, t2d.height); }
            else if (baseTex is RenderTexture rt) { w = Mathf.Max(1, rt.width); h = Mathf.Max(1, rt.height); }
        }

        // 3) writable copy
        if (baseTex != null)
            texture = CreateWritableCopy(baseTex, w, h);
        else
        {
            texture = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            FillTexture(texture, Color.white);     // si pas de base, fond blanc pour éviter la transparence
            texture.Apply();
        }

        // 4) assign & snapshot
        mat.SetTexture(texturePropertyName, texture);
        basePixels = texture.GetPixels();          // <<< on mémorise l'état initial (le visuel du board)
    }

    void Update()
    {
        HandleTouchInput();
        HandleMouseInput();

        // auto reset au bout de clearDelay
        if (Time.time - lastDrawTime > clearDelay && lastDrawTime > 0)
        {
            RestoreBaseTexture();                  // <<< rétablit la texture d'origine (pas transparente)
            lastDrawTime = -999f;
        }
    }

    // --- Inputs ---
    void HandleTouchInput()
    {
        if (Touchscreen.current == null) return;

        foreach (var touch in Touchscreen.current.touches)
        {
            int touchId = touch.touchId.ReadValue();
            if (touch.press.isPressed)
            {
                Vector2 pos = touch.position.ReadValue();
                ProcessDrawing(touchId, pos);
                lastDrawTime = Time.time;
            }
            else
            {
                if (drawPaths.ContainsKey(touchId))
                {
                    shapeRecognizer?.AnalyzeShape(drawPaths[touchId]);
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
            Vector2 pos = Mouse.current.position.ReadValue();
            ProcessDrawing(mouseId, pos);
            lastDrawTime = Time.time;
        }
        else
        {
            if (drawPaths.ContainsKey(mouseId))
            {
                shapeRecognizer?.AnalyzeShape(drawPaths[mouseId]);
                drawPaths.Remove(mouseId);
            }
            lastDrawPositions.Remove(mouseId);
        }
    }

    // --- Drawing ---
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

            if (lastDrawPositions.TryGetValue(id, out var lastPos) && lastPos.HasValue)
                DrawLine(lastPos.Value, currentPos);
            else
                DrawCircle(x, y);

            lastDrawPositions[id] = currentPos;
            texture.Apply();
        }
    }

    void DrawCircle(int x, int y)
    {
        int r = Mathf.CeilToInt(brushSize);
        int r2 = r * r;
        for (int i = -r; i <= r; i++)
            for (int j = -r; j <= r; j++)
                if (i * i + j * j <= r2)
                {
                    int px = x + i, py = y + j;
                    if (px >= 0 && px < texture.width && py >= 0 && py < texture.height)
                        texture.SetPixel(px, py, drawColor);
                }
    }

    void DrawLine(Vector2 from, Vector2 to)
    {
        int x0 = Mathf.RoundToInt(from.x), y0 = Mathf.RoundToInt(from.y);
        int x1 = Mathf.RoundToInt(to.x), y1 = Mathf.RoundToInt(to.y);
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
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

    // --- Reset helpers ---
    void RestoreBaseTexture()
    {
        if (basePixels == null || basePixels.Length == 0) return;
        texture.SetPixels(basePixels);
        texture.Apply();
    }

    public void ResetBoardNow() => RestoreBaseTexture(); // call depuis UI/événement si besoin

    // --- Utils ---
    Texture2D CreateWritableCopy(Texture src, int width, int height)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    void FillTexture(Texture2D tex, Color c)
    {
        var cols = new Color[tex.width * tex.height];
        for (int i = 0; i < cols.Length; i++) cols[i] = c;
        tex.SetPixels(cols);
    }
}
