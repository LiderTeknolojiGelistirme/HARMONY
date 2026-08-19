using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct RosVector3
{
    public double x;
    public double y;
    public double z;

    public RosVector3(double x, double y, double z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public Vector3 ToUnityPosition(bool convertFromRos = true)
    {
        if (!convertFromRos)
            return new Vector3((float)x, (float)y, (float)z);

        return RosUnityCoordinateConverter.RosToUnityPosition(x, y, z);
    }

    public Vector3 ToUnityDirection(bool convertFromRos = true)
    {
        if (!convertFromRos)
            return new Vector3((float)x, (float)y, (float)z);

        // ROS yön vektoru eksenleri -> Unity yön vektoru eksenleri
        return new Vector3(
            (float)-y,
            (float)z,
            (float)x
        );
    }
}

public enum DefectSeverity
{
    Low,
    Medium,
    High
}

[Serializable]
public class DefectPointData
{
    public string id;
    public RosVector3 rosPosition;
    public DefectSeverity severity = DefectSeverity.Medium;
    public float iconScale = 10f;
}

[Serializable]
public class ForceVectorData
{
    public string id;
    public RosVector3 rosStart;
    public RosVector3 rosDirection;
    public float magnitude = 0.2f;
}

[Serializable]
public class SafetyZoneData
{
    public string id;
    public RosVector3 rosCenter;
    public float radius = 0.15f;
    public Color color = new Color(1f, 0.6f, 0f, 0.2f);
}

[Serializable]
public class MockRosSceneData
{
    public List<DefectPointData> defectPoints = new();
    public List<RosVector3> trajectoryPoints = new();
    public List<ForceVectorData> forceVectors = new();
    public List<SafetyZoneData> safetyZones = new();
}

