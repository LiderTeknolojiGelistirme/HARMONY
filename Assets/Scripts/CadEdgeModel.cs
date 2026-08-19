using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// CAD modelinden cikarilmis 3D kenar noktalarini yukler ve 2D'ye projekte eder.
/// Grid-based spatial hash ile hizli nearest-neighbor eslestirme saglar.
/// </summary>
public class CadEdgeModel : MonoBehaviour
{
    [Header("Edge Model")]
    [SerializeField] private string edgeFileName = "door1_edges";

    [Header("Projeksiyon")]
    [SerializeField] private float overrideFocal = 0;
    [SerializeField] private float nearClip = 0.01f;

    // 3D kenar noktalar
    private Vector3[] edgePoints3D;
    private float focalLength;
    private float cx, cy;
    private bool loaded;

    // Projeksiyon buffer'lari (GC onleme)
    private Vector2[] projectedPoints;
    private bool[] visibility;

    // Spatial hash (nearest-neighbor icin)
    private const int GRID_CELL_SIZE = 8;
    private Dictionary<int, List<int>> spatialGrid;

    /// <summary> Model yuklendi mi? </summary>
    public bool IsLoaded => loaded;

    /// <summary> 3D kenar nokta sayisi. </summary>
    public int PointCount => edgePoints3D?.Length ?? 0;

    void Awake()
    {
        LoadEdgeModel();
    }

    /// <summary> StreamingAssets'ten .bytes dosyasi yukler. </summary>
    public void LoadEdgeModel()
    {
        string path = Path.Combine(Application.streamingAssetsPath, edgeFileName + ".bytes");

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[CadEdgeModel] Dosya bulunamadi: {path}");
            return;
        }

        using (var reader = new BinaryReader(File.Open(path, FileMode.Open)))
        {
            int numPoints = reader.ReadInt32();
            edgePoints3D = new Vector3[numPoints];

            for (int i = 0; i < numPoints; i++)
            {
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                float z = reader.ReadSingle();
                edgePoints3D[i] = new Vector3(x, y, z);
            }

            focalLength = reader.ReadSingle();
            cx = reader.ReadSingle();
            cy = reader.ReadSingle();
        }

        if (overrideFocal > 0)
            focalLength = overrideFocal;

        // Buffer'lari ayir
        projectedPoints = new Vector2[edgePoints3D.Length];
        visibility = new bool[edgePoints3D.Length];
        spatialGrid = new Dictionary<int, List<int>>(1024);

        loaded = true;
        Debug.Log($"[CadEdgeModel] Yuklendi: {edgePoints3D.Length} nokta, focal={focalLength:F1}");
    }

    /// <summary>
    /// 3D kenar noktalarini verilen poz ile 2D'ye projekte eder.
    /// </summary>
    /// <param name="rotation">Nesne rotasyonu (kamera uzayinda)</param>
    /// <param name="translation">Nesne pozisyonu (kamera uzayinda)</param>
    /// <returns>Gorunen projekte nokta sayisi</returns>
    public int ProjectEdges(Quaternion rotation, Vector3 translation)
    {
        if (!loaded) return 0;

        Matrix4x4 rotMatrix = Matrix4x4.Rotate(rotation);
        int visibleCount = 0;

        for (int i = 0; i < edgePoints3D.Length; i++)
        {
            // 3D donusum: P_cam = R * P + t
            Vector3 pCam = rotMatrix.MultiplyPoint3x4(edgePoints3D[i]) + translation;

            // Kamera arkasi kontrol
            if (pCam.z < nearClip)
            {
                visibility[i] = false;
                continue;
            }

            // Perspektif projeksiyon
            float invZ = 1.0f / pCam.z;
            projectedPoints[i] = new Vector2(
                pCam.x * focalLength * invZ + cx,
                pCam.y * focalLength * invZ + cy
            );
            visibility[i] = true;
            visibleCount++;
        }

        return visibleCount;
    }

    /// <summary>
    /// Projekte edilmis CAD kenarlari ile kamera kenarlari arasindaki
    /// chamfer distance hesaplar.
    /// </summary>
    /// <param name="cameraEdges">Kamera kenar pikselleri</param>
    /// <param name="residuals">Her projekte nokta icin en yakin mesafe (output)</param>
    /// <returns>Ortalama chamfer distance</returns>
    public float ComputeChamferDistance(List<Vector2> cameraEdges, float[] residuals)
    {
        if (!loaded || cameraEdges.Count == 0) return float.MaxValue;

        // Spatial hash olustur (kamera kenarlari icin)
        BuildSpatialGrid(cameraEdges);

        float totalDist = 0;
        int count = 0;

        for (int i = 0; i < edgePoints3D.Length; i++)
        {
            if (!visibility[i]) continue;

            float minDist = FindNearestEdge(projectedPoints[i], cameraEdges);
            if (residuals != null && i < residuals.Length)
                residuals[i] = minDist;

            totalDist += minDist;
            count++;
        }

        return count > 0 ? totalDist / count : float.MaxValue;
    }

    /// <summary> Projekte edilmis noktalar (readonly erisim). </summary>
    public Vector2 GetProjectedPoint(int index) => projectedPoints[index];

    /// <summary> Nokta gorunur mu? </summary>
    public bool IsVisible(int index) => visibility[index];

    /// <summary> Kamera parametreleri. </summary>
    public float FocalLength => focalLength;
    public float Cx => cx;
    public float Cy => cy;

    private void BuildSpatialGrid(List<Vector2> points)
    {
        spatialGrid.Clear();
        for (int i = 0; i < points.Count; i++)
        {
            int key = GridKey(points[i]);
            if (!spatialGrid.ContainsKey(key))
                spatialGrid[key] = new List<int>(16);
            spatialGrid[key].Add(i);
        }
    }

    private float FindNearestEdge(Vector2 queryPoint, List<Vector2> edgePoints)
    {
        float bestDist = float.MaxValue;
        int gx = (int)(queryPoint.x / GRID_CELL_SIZE);
        int gy = (int)(queryPoint.y / GRID_CELL_SIZE);

        // 3x3 komsuluk ara
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int key = (gx + dx) * 100000 + (gy + dy);
                if (!spatialGrid.ContainsKey(key)) continue;

                var cell = spatialGrid[key];
                for (int j = 0; j < cell.Count; j++)
                {
                    Vector2 ep = edgePoints[cell[j]];
                    float dist = (queryPoint - ep).sqrMagnitude;
                    if (dist < bestDist)
                        bestDist = dist;
                }
            }
        }

        return Mathf.Sqrt(bestDist);
    }

    private int GridKey(Vector2 point)
    {
        int gx = (int)(point.x / GRID_CELL_SIZE);
        int gy = (int)(point.y / GRID_CELL_SIZE);
        return gx * 100000 + gy;
    }
}
