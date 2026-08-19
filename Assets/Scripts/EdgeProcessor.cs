using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Pure C# Sobel kenar algilama.
/// Kamera karesinden kenar piksel koordinatlarini cikarir.
/// </summary>
public class EdgeProcessor : MonoBehaviour
{
    [Header("Sobel Ayarlari")]
    [SerializeField, Range(10, 200)] private int edgeThreshold = 50;
    [SerializeField] private bool adaptiveThreshold = true;

    // Pre-allocated buffer'lar (GC pressure onleme)
    private float[] grayBuffer;
    private float[] gxBuffer;
    private float[] gyBuffer;
    private float[] magBuffer;
    private int bufWidth, bufHeight;

    // Kenar sonuclari
    private readonly List<Vector2> edgePixelsResult = new List<Vector2>(20000);

    /// <summary>
    /// Texture2D uzerinde Sobel kenar algilama yapar.
    /// </summary>
    /// <param name="texture">Girdi goruntusu</param>
    /// <param name="roiMin">Ilgi bolgesi minimum (piksel, opsiyonel)</param>
    /// <param name="roiMax">Ilgi bolgesi maximum (piksel, opsiyonel)</param>
    /// <returns>Kenar piksel koordinatlari listesi</returns>
    public List<Vector2> DetectEdges(Texture2D texture,
                                     Vector2? roiMin = null,
                                     Vector2? roiMax = null)
    {
        int w = texture.width;
        int h = texture.height;

        EnsureBuffers(w, h);

        // Grayscale donusumu
        Color32[] pixels = texture.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 c = pixels[i];
            grayBuffer[i] = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        }

        // ROI sinirlari
        int xMin = roiMin.HasValue ? Mathf.Max(1, (int)roiMin.Value.x) : 1;
        int yMin = roiMin.HasValue ? Mathf.Max(1, (int)roiMin.Value.y) : 1;
        int xMax = roiMax.HasValue ? Mathf.Min(w - 1, (int)roiMax.Value.x) : w - 1;
        int yMax = roiMax.HasValue ? Mathf.Min(h - 1, (int)roiMax.Value.y) : h - 1;

        // Adaptif esik
        float threshold = edgeThreshold;
        if (adaptiveThreshold)
        {
            float meanBrightness = 0;
            int count = 0;
            for (int y = yMin; y < yMax; y += 4)
            {
                for (int x = xMin; x < xMax; x += 4)
                {
                    meanBrightness += grayBuffer[y * w + x];
                    count++;
                }
            }
            meanBrightness /= Mathf.Max(1, count);

            // Karanlik goruntuler icin esigi dusur
            if (meanBrightness < 80)
                threshold = edgeThreshold * 0.6f;
            else if (meanBrightness > 180)
                threshold = edgeThreshold * 1.3f;
        }

        // 3x3 Sobel filtresi
        // Gx kernel: [-1 0 1; -2 0 2; -1 0 1]
        // Gy kernel: [-1 -2 -1; 0 0 0; 1 2 1]
        for (int y = yMin; y < yMax; y++)
        {
            for (int x = xMin; x < xMax; x++)
            {
                int idx = y * w + x;

                float p00 = grayBuffer[(y - 1) * w + (x - 1)];
                float p01 = grayBuffer[(y - 1) * w + x];
                float p02 = grayBuffer[(y - 1) * w + (x + 1)];
                float p10 = grayBuffer[y * w + (x - 1)];
                float p12 = grayBuffer[y * w + (x + 1)];
                float p20 = grayBuffer[(y + 1) * w + (x - 1)];
                float p21 = grayBuffer[(y + 1) * w + x];
                float p22 = grayBuffer[(y + 1) * w + (x + 1)];

                float gx = -p00 + p02 - 2f * p10 + 2f * p12 - p20 + p22;
                float gy = -p00 - 2f * p01 - p02 + p20 + 2f * p21 + p22;

                magBuffer[idx] = Mathf.Sqrt(gx * gx + gy * gy);
            }
        }

        // Threshold ve piksel toplama
        float threshSq = threshold * threshold;
        edgePixelsResult.Clear();

        for (int y = yMin; y < yMax; y++)
        {
            for (int x = xMin; x < xMax; x++)
            {
                float mag = magBuffer[y * w + x];
                if (mag > threshold)
                {
                    edgePixelsResult.Add(new Vector2(x, y));
                }
            }
        }

        return edgePixelsResult;
    }

    private void EnsureBuffers(int w, int h)
    {
        if (bufWidth == w && bufHeight == h) return;

        int size = w * h;
        grayBuffer = new float[size];
        gxBuffer = new float[size];
        gyBuffer = new float[size];
        magBuffer = new float[size];
        bufWidth = w;
        bufHeight = h;
    }
}
