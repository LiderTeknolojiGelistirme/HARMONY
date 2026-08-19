using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Levenberg-Marquardt / Gauss-Newton iteratif poz rafine etme.
/// CAD kenar projeksiyonlarini kamera kenarlariyla eslestirerek
/// chamfer distance'i minimize eder.
/// </summary>
public class PoseRefiner : MonoBehaviour
{
    [Header("Optimizasyon Ayarlari")]
    [SerializeField, Range(1, 15)] private int maxIterations = 7;
    [SerializeField] private float convergenceThreshold = 0.001f;
    [SerializeField] private float initialLambda = 0.01f;
    [SerializeField] private float lambdaScaleUp = 10f;
    [SerializeField] private float lambdaScaleDown = 0.1f;

    [Header("Jacobian")]
    [SerializeField] private float epsilonTranslation = 1e-4f;
    [SerializeField] private float epsilonRotation = 1e-3f;

    [Header("Guvenlik")]
    [SerializeField] private float maxStepTranslation = 0.1f;
    [SerializeField] private float maxStepRotation = 0.15f;
    [SerializeField] private int minEdgePixels = 20;

    // Buffer'lar (GC onleme)
    private float[] residuals;
    private float[] residualsPerturbed;
    private float[] jacobian;       // N x 6
    private float[] jtj;            // 6 x 6
    private float[] jtr;            // 6
    private float[] delta;          // 6
    private float[] cholL;          // 6 x 6

    /// <summary> Son rafine etmenin sonuc chamfer distance'i. </summary>
    public float LastChamferDistance { get; private set; }

    /// <summary> Son rafine etmede yapilan iterasyon sayisi. </summary>
    public int LastIterations { get; private set; }

    /// <summary>
    /// Poz rafine etme. Baslangic pozundan iteratif olarak
    /// CAD-kamera kenar eslesme hatasini minimize eder.
    /// </summary>
    /// <param name="initialPos">Baslangic pozisyonu</param>
    /// <param name="initialRot">Baslangic rotasyonu</param>
    /// <param name="cadModel">CAD kenar modeli</param>
    /// <param name="cameraEdges">Kamera kenar pikselleri</param>
    /// <param name="refinedPos">Rafine edilmis pozisyon (output)</param>
    /// <param name="refinedRot">Rafine edilmis rotasyon (output)</param>
    /// <returns>Basarili mi?</returns>
    public bool Refine(Vector3 initialPos, Quaternion initialRot,
                       CadEdgeModel cadModel, List<Vector2> cameraEdges,
                       out Vector3 refinedPos, out Quaternion refinedRot)
    {
        refinedPos = initialPos;
        refinedRot = initialRot;
        LastChamferDistance = float.MaxValue;
        LastIterations = 0;

        if (cadModel == null || !cadModel.IsLoaded ||
            cameraEdges == null || cameraEdges.Count < minEdgePixels)
            return false;

        int N = cadModel.PointCount;
        EnsureBuffers(N);

        // Mevcut poz parametreleri: [rx, ry, rz, tx, ty, tz]
        // rx,ry,rz = axis-angle perturbation (baslangicta 0)
        Vector3 currentPos = initialPos;
        Quaternion currentRot = initialRot;
        float lambda = initialLambda;

        // Baslangic maliyeti
        cadModel.ProjectEdges(currentRot, currentPos);
        float currentCost = cadModel.ComputeChamferDistance(cameraEdges, residuals);

        for (int iter = 0; iter < maxIterations; iter++)
        {
            LastIterations = iter + 1;

            // Sayisal Jacobian hesapla (6 perturbation)
            ComputeJacobian(cadModel, cameraEdges, currentPos, currentRot, N);

            // JᵀJ ve Jᵀr hesapla
            ComputeJtJAndJtr(N);

            // Levenberg-Marquardt damping: (JᵀJ + λI) δ = -Jᵀr
            for (int i = 0; i < 6; i++)
                jtj[i * 6 + i] += lambda;

            // 6x6 sistem coz (Cholesky)
            if (!SolveCholesky6x6(jtj, jtr, delta))
            {
                lambda *= lambdaScaleUp;
                continue;
            }

            // Adim buyuklugu sinirla
            ClampStep(delta);

            // Pozu guncelle
            Vector3 newPos = currentPos;
            newPos.x -= delta[3];
            newPos.y -= delta[4];
            newPos.z -= delta[5];

            // Axis-angle -> quaternion perturbation
            Vector3 axisAngle = new Vector3(-delta[0], -delta[1], -delta[2]);
            float angle = axisAngle.magnitude;
            Quaternion deltaRot = Quaternion.identity;
            if (angle > 1e-8f)
                deltaRot = Quaternion.AngleAxis(
                    angle * Mathf.Rad2Deg,
                    axisAngle / angle
                );
            Quaternion newRot = deltaRot * currentRot;

            // Yeni maliyet
            cadModel.ProjectEdges(newRot, newPos);
            float newCost = cadModel.ComputeChamferDistance(cameraEdges, residualsPerturbed);

            if (newCost < currentCost)
            {
                currentPos = newPos;
                currentRot = newRot;
                currentCost = newCost;
                lambda *= lambdaScaleDown;

                // Convergence kontrol
                float stepNorm = 0;
                for (int i = 0; i < 6; i++)
                    stepNorm += delta[i] * delta[i];
                if (Mathf.Sqrt(stepNorm) < convergenceThreshold)
                    break;
            }
            else
            {
                lambda *= lambdaScaleUp;
            }
        }

        refinedPos = currentPos;
        refinedRot = currentRot;
        LastChamferDistance = currentCost;
        return currentCost < float.MaxValue;
    }

    private void ComputeJacobian(CadEdgeModel cadModel, List<Vector2> cameraEdges,
                                  Vector3 pos, Quaternion rot, int N)
    {
        float[] epsilons = {
            epsilonRotation, epsilonRotation, epsilonRotation,
            epsilonTranslation, epsilonTranslation, epsilonTranslation
        };

        for (int j = 0; j < 6; j++)
        {
            float eps = epsilons[j];

            // Perturbation uygula
            Vector3 pPos = pos;
            Quaternion pRot = rot;

            if (j < 3)
            {
                // Rotasyon perturbation (axis-angle)
                Vector3 axis = Vector3.zero;
                axis[j] = 1;
                Quaternion dq = Quaternion.AngleAxis(eps * Mathf.Rad2Deg, axis);
                pRot = dq * rot;
            }
            else
            {
                // Translation perturbation
                Vector3 dt = Vector3.zero;
                dt[j - 3] = eps;
                pPos = pos + dt;
            }

            // Perturbed residuals
            cadModel.ProjectEdges(pRot, pPos);
            cadModel.ComputeChamferDistance(cameraEdges, residualsPerturbed);

            // J[:, j] = (r_perturbed - r) / epsilon
            float invEps = 1.0f / eps;
            for (int i = 0; i < N; i++)
            {
                jacobian[i * 6 + j] = (residualsPerturbed[i] - residuals[i]) * invEps;
            }
        }

        // Orijinal pozla tekrar projekte et
        cadModel.ProjectEdges(rot, pos);
    }

    private void ComputeJtJAndJtr(int N)
    {
        // JᵀJ (6x6) ve Jᵀr (6x1)
        for (int i = 0; i < 6; i++)
        {
            jtr[i] = 0;
            for (int j = 0; j < 6; j++)
                jtj[i * 6 + j] = 0;
        }

        for (int k = 0; k < N; k++)
        {
            float r = residuals[k];
            for (int i = 0; i < 6; i++)
            {
                float ji = jacobian[k * 6 + i];
                jtr[i] += ji * r;
                for (int j = i; j < 6; j++)
                {
                    float val = ji * jacobian[k * 6 + j];
                    jtj[i * 6 + j] += val;
                    if (i != j)
                        jtj[j * 6 + i] += val;
                }
            }
        }
    }

    private bool SolveCholesky6x6(float[] A, float[] b, float[] x)
    {
        // Cholesky decomposition: A = LLᵀ
        const int n = 6;
        System.Array.Clear(cholL, 0, n * n);

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                float sum = 0;
                for (int k = 0; k < j; k++)
                    sum += cholL[i * n + k] * cholL[j * n + k];

                if (i == j)
                {
                    float val = A[i * n + i] - sum;
                    if (val <= 0) return false; // Pozitif tanitsizlik yok
                    cholL[i * n + j] = Mathf.Sqrt(val);
                }
                else
                {
                    cholL[i * n + j] = (A[i * n + j] - sum) / cholL[j * n + j];
                }
            }
        }

        // Ly = b (ileri yerine koyma)
        float[] y = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0;
            for (int k = 0; k < i; k++)
                sum += cholL[i * n + k] * y[k];
            y[i] = (b[i] - sum) / cholL[i * n + i];
        }

        // Lᵀx = y (geri yerine koyma)
        for (int i = n - 1; i >= 0; i--)
        {
            float sum = 0;
            for (int k = i + 1; k < n; k++)
                sum += cholL[k * n + i] * x[k];
            x[i] = (y[i] - sum) / cholL[i * n + i];
        }

        return true;
    }

    private void ClampStep(float[] step)
    {
        // Rotasyon adim sinirla
        for (int i = 0; i < 3; i++)
            step[i] = Mathf.Clamp(step[i], -maxStepRotation, maxStepRotation);
        // Translation adim sinirla
        for (int i = 3; i < 6; i++)
            step[i] = Mathf.Clamp(step[i], -maxStepTranslation, maxStepTranslation);
    }

    private void EnsureBuffers(int N)
    {
        if (residuals != null && residuals.Length >= N) return;

        residuals = new float[N];
        residualsPerturbed = new float[N];
        jacobian = new float[N * 6];
        jtj = new float[36];
        jtr = new float[6];
        delta = new float[6];
        cholL = new float[36];
    }
}
