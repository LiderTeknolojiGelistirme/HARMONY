using UnityEngine;

/// <summary>
/// ROS2 (REP-103) ile Unity koordinat sistemi arasında pozisyon ve yönelim dönüşümü.
/// Şu an projede kullanılmıyor; ileride geometry_msgs/Pose, Point, TransformStamped vb.
/// kullanıldığında bu metodlar çağrılabilir.
/// </summary>
/// <remarks>
/// ROS (sağ el): +X ileri, +Y sol, +Z yukarı.
/// Unity (sol el): +X sağ, +Y yukarı, +Z ileri.
/// </remarks>
public static class RosUnityCoordinateConverter
{
    // --- Pozisyon: ROS (x,y,z) <-> Unity Vector3 ---

    /// <summary>ROS pozisyonunu Unity Vector3'e çevirir. (ROS: X ileri, Y sol, Z yukarı → Unity: X sağ, Y yukarı, Z ileri)</summary>
    public static Vector3 RosToUnityPosition(double rosX, double rosY, double rosZ)
    {
        return new Vector3(
            (float)-rosY,  // ROS sol → Unity sağ (negatif)
            (float)rosZ,   // ROS yukarı → Unity yukarı
            (float)rosX    // ROS ileri → Unity ileri
        );
    }

    /// <summary>ROS Vector3 benzeri (x,y,z) değerlerini Unity Vector3'e çevirir.</summary>
    public static Vector3 RosToUnityPosition(Vector3 rosPosition)
    {
        return RosToUnityPosition(rosPosition.x, rosPosition.y, rosPosition.z);
    }

    /// <summary>Unity Vector3'ü ROS (x,y,z) pozisyonuna çevirir.</summary>
    public static void UnityToRosPosition(Vector3 unityPosition, out double rosX, out double rosY, out double rosZ)
    {
        rosX = unityPosition.z;
        rosY = -unityPosition.x;
        rosZ = unityPosition.y;
    }

    /// <summary>Unity Vector3'ü ROS tarafında kullanılabilecek Vector3 benzeri değerlere çevirir (x,y,z).</summary>
    public static Vector3 UnityToRosPositionVector(Vector3 unityPosition)
    {
        return new Vector3(unityPosition.z, -unityPosition.x, unityPosition.y);
    }

    // --- Yönelim (Quaternion): ROS (x,y,z,w) <-> Unity Quaternion ---

    /// <summary>ROS quaternion'ını (x,y,z,w) Unity Quaternion'a çevirir. Eksen eşlemesi pozisyonla tutarlıdır.</summary>
    public static Quaternion RosToUnityQuaternion(double rosQx, double rosQy, double rosQz, double rosQw)
    {
        // ROS eksenleri -> Unity eksenleri: aynı fiziksel dönüşü korumak için
        // eksen vektörleri dönüşümü: Unity.x=-ROS.y, Unity.y=ROS.z, Unity.z=ROS.x
        return new Quaternion(
            (float)-rosQy,
            (float)rosQz,
            (float)rosQx,
            (float)rosQw
        );
    }

    /// <summary>Unity Quaternion'ı ROS (x,y,z,w) quaternion'ına çevirir.</summary>
    public static void UnityToRosQuaternion(Quaternion unityQuat, out double rosQx, out double rosQy, out double rosQz, out double rosQw)
    {
        // Tutarlı ters dönüşüm: Unity q = (-rosQy, rosQz, rosQx, rosQw)
        rosQx = unityQuat.z;
        rosQy = -unityQuat.x;
        rosQz = unityQuat.y;
        rosQw = unityQuat.w;
    }

    /// <summary>Unity Quaternion'dan ROS tarafında kullanılabilecek (x,y,z,w) değerlerini döndürür.</summary>
    public static (double x, double y, double z, double w) UnityToRosQuaternionTuple(Quaternion unityQuat)
    {
        UnityToRosQuaternion(unityQuat, out double x, out double y, out double z, out double w);
        return (x, y, z, w);
    }

    // --- Pose (pozisyon + yönelim) tek seferde ---

    /// <summary>ROS pose'unu (pozisyon x,y,z ve quaternion x,y,z,w) Unity position + rotation olarak döndürür.</summary>
    public static void RosToUnityPose(
        double posX, double posY, double posZ,
        double qx, double qy, double qz, double qw,
        out Vector3 unityPosition, out Quaternion unityRotation)
    {
        unityPosition = RosToUnityPosition(posX, posY, posZ);
        unityRotation = RosToUnityQuaternion(qx, qy, qz, qw);
    }

    /// <summary>Unity Transform değerlerini ROS pose'una çevirir (pozisyon + quaternion).</summary>
    public static void UnityToRosPose(Transform t, out Vector3 rosPosition, out Quaternion rosRotation)
    {
        rosPosition = UnityToRosPositionVector(t.position);
        var (qx, qy, qz, qw) = UnityToRosQuaternionTuple(t.rotation);
        rosRotation = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
    }
}
