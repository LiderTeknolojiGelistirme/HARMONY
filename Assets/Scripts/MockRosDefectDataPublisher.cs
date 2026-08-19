using System;
using UnityEngine;

public class MockRosDefectDataPublisher : MonoBehaviour
{
    [Header("Mock Yayın Ayarları")]
    [SerializeField] private bool autoPublish = true;
    [SerializeField] private float publishInterval = 1f;
    [SerializeField] private float motionRadius = 0.35f;
    [SerializeField] private float baseRosZ = 0.45f;

    private float timer;

    public event Action<MockRosSceneData> OnSceneDataPublished;

    private void Start()
    {
        if (autoPublish)
            PublishNow();
    }

    private void Update()
    {
        if (!autoPublish)
            return;

        timer += Time.deltaTime;
        if (timer >= publishInterval)
        {
            timer = 0f;
            PublishNow();
        }
    }

    [ContextMenu("Publish Mock Data Now")]
    public void PublishNow()
    {
        MockRosSceneData data = GenerateMockData(Time.time);
        OnSceneDataPublished?.Invoke(data);
    }

    private MockRosSceneData GenerateMockData(float t)
    {
        var data = new MockRosSceneData();

        // Kusur noktaları (ROS koordinatı)
        data.defectPoints.Add(new DefectPointData
        {
            id = "defect_A",
            rosPosition = new RosVector3(0.25 + Math.Sin(t * 0.6f) * 0.08, -0.35 + Math.Cos(t * 0.4f) * 0.04, baseRosZ),
            severity = DefectSeverity.Low,
            iconScale = 10f
        });
        data.defectPoints.Add(new DefectPointData
        {
            id = "defect_B",
            rosPosition = new RosVector3(0.60 + Math.Cos(t * 0.45f) * 0.07, 0.30 + Math.Sin(t * 0.35f) * 0.05, baseRosZ + 0.06),
            severity = DefectSeverity.Medium,
            iconScale = 10f
        });
        data.defectPoints.Add(new DefectPointData
        {
            id = "defect_C",
            rosPosition = new RosVector3(0.90 + Math.Sin(t * 0.55f) * 0.06, -0.05 + Math.Cos(t * 0.5f) * 0.06, baseRosZ + 0.10),
            severity = DefectSeverity.High,
            iconScale = 10f
        });

        // Yörünge örnek noktaları (elips yol)
        int trajectoryCount = 32;
        for (int i = 0; i < trajectoryCount; i++)
        {
            float phase = (i / (float)(trajectoryCount - 1)) * Mathf.PI * 2f;
            double rosX = 0.5 + Math.Cos(phase + t * 0.3f) * motionRadius * 0.6;
            double rosY = Math.Sin(phase + t * 0.3f) * motionRadius * 0.35;
            double rosZ = baseRosZ + 0.04 * Math.Sin((phase * 2f) + t * 0.4f);
            data.trajectoryPoints.Add(new RosVector3(rosX, rosY, rosZ));
        }

        // Kuvvet vektörleri
        data.forceVectors.Add(new ForceVectorData
        {
            id = "force_A",
            rosStart = new RosVector3(0.45, -0.2, baseRosZ),
            rosDirection = new RosVector3(0.25, 0.1, 0.15),
            magnitude = 0.22f
        });
        data.forceVectors.Add(new ForceVectorData
        {
            id = "force_B",
            rosStart = new RosVector3(0.6, 0.15, baseRosZ + 0.03),
            rosDirection = new RosVector3(-0.2, -0.1, 0.12),
            magnitude = 0.18f
        });

        // Yarı saydam güvenlik bölgeleri
        data.safetyZones.Add(new SafetyZoneData
        {
            id = "safe_zone_1",
            rosCenter = new RosVector3(0.5, 0.0, baseRosZ),
            radius = 0.2f,
            color = new Color(0f, 0.75f, 1f, 0.22f)
        });
        data.safetyZones.Add(new SafetyZoneData
        {
            id = "safe_zone_2",
            rosCenter = new RosVector3(0.65, -0.2, baseRosZ + 0.02),
            radius = 0.12f,
            color = new Color(1f, 0.55f, 0f, 0.2f)
        });

        return data;
    }
}

