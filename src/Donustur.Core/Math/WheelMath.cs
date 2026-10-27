namespace Donustur.Core.Math;

public readonly record struct WheelPoint(double X, double Y);

public static class WheelMath
{
    /// <summary>
    /// Fare koordinatına göre fare imlecinin hangi dilim üzerinde olduğunu hesaplar.
    /// Dilimler saat yönünde, üstten (90 derece ofsetle) başlar.
    /// Merkezdeki ölü bölgede veya çarkın dışındaysa -1 döner.
    /// </summary>
    public static int GetSegmentIndex(
        double centerX,
        double centerY,
        double mouseX,
        double mouseY,
        int totalSegments,
        double deadZoneRadius = 42,
        double outerRadius = 175)
    {
        if (totalSegments <= 0) return -1;

        double dx = mouseX - centerX;
        double dy = mouseY - centerY;
        double distance = System.Math.Sqrt(dx * dx + dy * dy);

        if (distance < deadZoneRadius || distance > outerRadius)
            return -1;

        // dy aşağı doğru pozitif olduğu için ekran koordinatlarında Math.Atan2(dy, dx)
        double angleRad = System.Math.Atan2(dy, dx);
        double angleDeg = angleRad * (180.0 / System.Math.PI);

        // Standart matematik: 0 derece sağ, 90 derece alt.
        // Biz ilk dilimi tam tepeye (saat 12) ortalamak istiyoruz.
        // Saat 12 açısı: -90 derece (veya 270 derece).
        // Normalize 0 - 360
        if (angleDeg < 0) angleDeg += 360.0;

        // Tepe noktayı (270°) 0° başlangıcına eşlemek için:
        // Saat yönünde: 270° -> 0, 0° -> 90, 90° -> 180, 180° -> 270.
        // clockAngle = (angleDeg + 90) % 360
        double clockAngle = (angleDeg + 90.0) % 360.0;

        double segmentAngle = 360.0 / totalSegments;
        // İlk dilimi tepeye ortalamak için yarım dilim ofset:
        double shifted = (clockAngle + (segmentAngle / 2.0)) % 360.0;

        int index = (int)(shifted / segmentAngle);
        if (index < 0) index = 0;
        if (index >= totalSegments) index = totalSegments - 1;

        return index;
    }

    /// <summary>
    /// Verilen dilimin merkez açısını (derece cinsinden) hesaplar.
    /// 0. dilim saat 12 yönündedir (tepede).
    /// </summary>
    public static double GetSegmentCenterAngle(int segmentIndex, int totalSegments)
    {
        if (totalSegments <= 0) return 0;
        double segmentAngle = 360.0 / totalSegments;
        // Saat 12 (270 derece ekran koordinatı) + index * segmentAngle
        double angle = -90.0 + (segmentIndex * segmentAngle);
        return angle;
    }

    /// <summary>
    /// Açı ve yarıçapa göre çember üzerindeki koordinatı hesaplar.
    /// </summary>
    public static WheelPoint GetPointOnCircle(double centerX, double centerY, double radius, double angleInDegrees)
    {
        double angleRad = angleInDegrees * (System.Math.PI / 180.0);
        double x = centerX + (radius * System.Math.Cos(angleRad));
        double y = centerY + (radius * System.Math.Sin(angleRad));
        return new WheelPoint(x, y);
    }
}
