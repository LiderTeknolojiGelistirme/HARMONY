using System;
using System.Collections.Generic;

/// <summary>
/// UR10e ileri kinematiği (forward kinematics) — Denavit-Hartenberg tabanlı.
///
/// Bu sınıf, Model-Based-Digital-Twin-Functional-Mockup-Unit deposundaki
/// <c>Cobot_Ur10e/UR10e_FK/Ur10e_FK_Source_Codes/model.py</c> dosyasının
/// (ur10e_FK.fmu'nun kaynak kodu) birebir C# karşılığıdır. FMU, UniFMU'nun
/// Python arka ucuyla çalışan bir co-simulation modelidir; gözlük üzerinde
/// doğrudan koşturulamadığı için hesap burada yeniden yazılmıştır. FMU
/// birincil referans ve doğrulama aracı olarak kalır.
///
/// Çerçeve: Sonuçlar UR denetleyicisinin "Base" çerçevesindedir (ROS'taki
/// <c>ur10e_base</c> link'i). ROS <c>world</c> çerçevesine taşımak için
/// <see cref="HarmonyRobotKinematics"/> kullanılır.
///
/// Doğrulama (03.08.2026):
///   * FMU çıktı kayıtlarına karşı (ur10e_FK_out.csv, ur10e_FK_out2.csv,
///     ur10e_FK_8agustostoplantı_out.csv, UR10eFKdeneme_out.csv — 114 satır):
///     konum farkı &lt; 1e-4 mm, kuaterniyon farkı &lt; 2.3e-8.
///   * Gerçek robot kaydına karşı (robot_joint_data_processed.xlsx, 54 örnek):
///     ortalama 1.50 mm, en kötü 2.39 mm. Hatanın çoğu flanş çerçevesinde sabit
///     bir (-1.31, -0.09, -0.48) mm ötelemesi — muhtemelen denetleyicideki
///     takım/kalibrasyon farkı. Bu çıkarıldığında kalan saçılma ortalama
///     0.60 mm, en kötü 1.15 mm; kayıttaki 3 haneli yuvarlamanın gürültü
///     tabanıyla (~0.5–1.0 mm) aynı mertebede.
///
/// Doğrulamayı yeniden koşturmak için: Tools/fk_validation/ (README'ye bakın).
///
/// UnityEngine'e bilerek bağımlı değildir; Unity dışında da derlenip test
/// edilebilsin diye tüm hesap <c>double</c> dizilerle yapılır.
/// </summary>
public static class Ur10eForwardKinematics
{
    /// <summary>Eklem sayısı (lineer eksen hariç, yalnız UR10e kolu).</summary>
    public const int JointCount = 6;

    /// <summary>Taban yüksekliği d1 [m]. model.py içinde LB.</summary>
    public const double BaseHeight = 0.181;

    /// <summary>Üst kol uzunluğu a2 [m]. DH tablosunda -a2 olarak girer.</summary>
    public const double UpperArmLength = 0.613;

    /// <summary>Ön kol uzunluğu a3 [m]. DH tablosunda -a3 olarak girer.</summary>
    public const double ForearmLength = 0.572;

    /// <summary>Omuz ötelemesi d4 [m].</summary>
    public const double ShoulderOffset = 0.174;

    /// <summary>Bilek ötelemesi d5 [m].</summary>
    public const double WristOffset = 0.120;

    /// <summary>
    /// Bilek 3 ekseninden robot flanşına (Frame 6) uzaklık d6 [m].
    /// model.py'de yanlışlıkla LTP adıyla geçer; asıl takım ötelemesi
    /// oradaki <c>T_6_Tool[2,3]</c> değeridir ve varsayılanı sıfırdır
    /// (bkz. additional_codes/model.py, d6 + LTP ayrımını orada yapıyor).
    /// </summary>
    public const double FlangeLength = 0.117;

    private const double HalfPi = Math.PI / 2.0;

    // DH tablosu (model.py "Tablo 2"): satır başına alpha_{i-1}, a_{i-1}, d_i.
    // theta ötelemesi altı eklemde de sıfır olduğu için tabloda tutulmuyor.
    private static readonly double[] DhAlpha = { HalfPi, 0.0, 0.0, HalfPi, -HalfPi, 0.0 };
    private static readonly double[] DhA = { 0.0, -UpperArmLength, -ForearmLength, 0.0, 0.0, 0.0 };
    private static readonly double[] DhD = { BaseHeight, 0.0, 0.0, ShoulderOffset, WristOffset, FlangeLength };

    /// <summary>
    /// Altı eklem açısından taban→uç dönüşüm matrisini hesaplar.
    ///
    /// Çağıran 16 elemanlı tamponu kendi sağlar; yörünge örneklenirken bu
    /// fonksiyon yüzlerce kez çağrıldığı için çöp üretmemesi önemlidir.
    /// </summary>
    /// <param name="jointAngles">q1…q6 [rad]. En az 6 eleman olmalıdır.</param>
    /// <param name="toolOffset">
    /// Flanştan takım ucuna z yönündeki uzaklık [m]. model.py'deki
    /// <c>T_6_Tool[2,3]</c> ile aynıdır; takım takılı değilse 0.
    /// </param>
    /// <param name="transform">
    /// Sonucun yazılacağı 16 elemanlı tampon; satır-öncelikli 4x4 matris
    /// (transform[row * 4 + col]).
    /// </param>
    public static void Solve(IList<double> jointAngles, double toolOffset, double[] transform)
    {
        if (jointAngles == null) throw new ArgumentNullException("jointAngles");
        if (jointAngles.Count < JointCount)
            throw new ArgumentException("En az 6 eklem açısı gerekir.", "jointAngles");
        if (transform == null || transform.Length < 16)
            throw new ArgumentException("16 elemanlı tampon gerekir.", "transform");

        SetIdentity(transform);

        for (int i = 0; i < JointCount; i++)
        {
            BuildDhTransform(DhAlpha[i], DhA[i], DhD[i], jointAngles[i], stepBuffer);
            Multiply(transform, stepBuffer, productBuffer);
            Array.Copy(productBuffer, transform, 16);
        }

        if (toolOffset == 0.0) return;

        // T_6_Tool yalnızca z ekseninde öteleme; matris çarpımı yerine
        // doğrudan son sütuna ekliyoruz.
        transform[3] += transform[2] * toolOffset;
        transform[7] += transform[6] * toolOffset;
        transform[11] += transform[10] * toolOffset;
    }

    // Solve() tek thread'den (Unity ana döngüsü) çağrılır; ara tamponlar
    // paylaşılarak kare başına ayırma sıfırlanır.
    [ThreadStatic] private static double[] stepBufferStorage;
    [ThreadStatic] private static double[] productBufferStorage;

    private static double[] stepBuffer
    {
        get { return stepBufferStorage ?? (stepBufferStorage = new double[16]); }
    }

    private static double[] productBuffer
    {
        get { return productBufferStorage ?? (productBufferStorage = new double[16]); }
    }

    /// <summary>
    /// Dönüşüm matrisinden uç nokta konumunu okur.
    /// </summary>
    /// <param name="transform">4x4 satır-öncelikli dönüşüm.</param>
    /// <param name="x">Taban çerçevesinde x [m].</param>
    /// <param name="y">Taban çerçevesinde y [m].</param>
    /// <param name="z">Taban çerçevesinde z [m].</param>
    public static void GetPosition(double[] transform, out double x, out double y, out double z)
    {
        x = transform[3];
        y = transform[7];
        z = transform[11];
    }

    /// <summary>
    /// Dönüşüm matrisini kuaterniyona çevirir. model.py'deki
    /// <c>matrix_to_quaternion</c> ile aynı dal yapısını ve aynı
    /// "qw &gt;= 0" normalleştirmesini uygular.
    /// </summary>
    /// <param name="transform">4x4 satır-öncelikli dönüşüm.</param>
    /// <param name="qx">Kuaterniyonun x bileşeni.</param>
    /// <param name="qy">Kuaterniyonun y bileşeni.</param>
    /// <param name="qz">Kuaterniyonun z bileşeni.</param>
    /// <param name="qw">Kuaterniyonun w bileşeni.</param>
    public static void GetQuaternion(double[] transform,
                                     out double qx, out double qy, out double qz, out double qw)
    {
        double r00 = transform[0], r01 = transform[1], r02 = transform[2];
        double r10 = transform[4], r11 = transform[5], r12 = transform[6];
        double r20 = transform[8], r21 = transform[9], r22 = transform[10];

        double trace = r00 + r11 + r22;

        if (trace > 0.0)
        {
            double s = Math.Sqrt(trace + 1.0) * 2.0;
            qw = 0.25 * s;
            qx = (r21 - r12) / s;
            qy = (r02 - r20) / s;
            qz = (r10 - r01) / s;
        }
        else if (r00 > r11 && r00 > r22)
        {
            double s = Math.Sqrt(1.0 + r00 - r11 - r22) * 2.0;
            qw = (r21 - r12) / s;
            qx = 0.25 * s;
            qy = (r01 + r10) / s;
            qz = (r02 + r20) / s;
        }
        else if (r11 > r22)
        {
            double s = Math.Sqrt(1.0 + r11 - r00 - r22) * 2.0;
            qw = (r02 - r20) / s;
            qx = (r01 + r10) / s;
            qy = 0.25 * s;
            qz = (r12 + r21) / s;
        }
        else
        {
            double s = Math.Sqrt(1.0 + r22 - r00 - r11) * 2.0;
            qw = (r10 - r01) / s;
            qx = (r02 + r20) / s;
            qy = (r12 + r21) / s;
            qz = 0.25 * s;
        }

        // Veri kümesinin "qw >= 0" kuralı (model.py'deki CANONICALIZATION).
        if (qw < 0.0)
        {
            qx = -qx;
            qy = -qy;
            qz = -qz;
            qw = -qw;
        }
    }

    /// <summary>
    /// Dönüşüm matrisini XYZ-Euler açılarına çevirir. model.py'deki
    /// <c>matrix_to_rpy</c> ile aynıdır; tekillik eşiği de aynı (1e-6).
    /// </summary>
    /// <param name="transform">4x4 satır-öncelikli dönüşüm.</param>
    /// <param name="roll">x ekseni etrafında dönüş [rad].</param>
    /// <param name="pitch">y ekseni etrafında dönüş [rad].</param>
    /// <param name="yaw">z ekseni etrafında dönüş [rad].</param>
    public static void GetRollPitchYaw(double[] transform,
                                       out double roll, out double pitch, out double yaw)
    {
        double r00 = transform[0];
        double r10 = transform[4];
        double r11 = transform[5];
        double r12 = transform[6];
        double r20 = transform[8], r21 = transform[9], r22 = transform[10];

        double sy = Math.Sqrt(r00 * r00 + r10 * r10);

        if (sy >= 1e-6)
        {
            roll = Math.Atan2(r21, r22);
            pitch = Math.Atan2(-r20, sy);
            yaw = Math.Atan2(r10, r00);
        }
        else
        {
            roll = Math.Atan2(-r12, r11);
            pitch = Math.Atan2(-r20, sy);
            yaw = 0.0;
        }
    }

    /// <summary>
    /// Standart DH parametrelerinden A_{i-1}^{i} homojen dönüşümünü kurar.
    /// </summary>
    /// <param name="alpha">Burulma açısı [rad].</param>
    /// <param name="a">Bağ uzunluğu [m].</param>
    /// <param name="d">Bağ ötelemesi [m].</param>
    /// <param name="theta">Eklem açısı [rad].</param>
    /// <param name="result">16 elemanlı çıkış tamponu.</param>
    private static void BuildDhTransform(double alpha, double a, double d, double theta, double[] result)
    {
        double ct = Math.Cos(theta);
        double st = Math.Sin(theta);
        double ca = Math.Cos(alpha);
        double sa = Math.Sin(alpha);

        result[0] = ct; result[1] = -st * ca; result[2] = st * sa; result[3] = a * ct;
        result[4] = st; result[5] = ct * ca; result[6] = -ct * sa; result[7] = a * st;
        result[8] = 0.0; result[9] = sa; result[10] = ca; result[11] = d;
        result[12] = 0.0; result[13] = 0.0; result[14] = 0.0; result[15] = 1.0;
    }

    /// <summary>
    /// İki 4x4 satır-öncelikli matrisi çarpar (result = left * right).
    /// </summary>
    /// <param name="left">Sol çarpan.</param>
    /// <param name="right">Sağ çarpan.</param>
    /// <param name="result">Çıkış tamponu; left veya right ile aynı olamaz.</param>
    private static void Multiply(double[] left, double[] right, double[] result)
    {
        for (int row = 0; row < 4; row++)
        {
            int r = row * 4;
            for (int col = 0; col < 4; col++)
            {
                result[r + col] = left[r] * right[col]
                                + left[r + 1] * right[4 + col]
                                + left[r + 2] * right[8 + col]
                                + left[r + 3] * right[12 + col];
            }
        }
    }

    /// <summary>
    /// 16 elemanlı tamponu birim matrise çevirir.
    /// </summary>
    /// <param name="m">Hedef tampon.</param>
    private static void SetIdentity(double[] m)
    {
        Array.Clear(m, 0, 16);
        m[0] = 1.0;
        m[5] = 1.0;
        m[10] = 1.0;
        m[15] = 1.0;
    }
}
