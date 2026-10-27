namespace Donustur.Core.Utils;

public static class FileUtils
{
    private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB" };

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "-" + FormatBytes(-bytes);
        if (bytes == 0) return "0 B";

        int mag = (int)System.Math.Log(bytes, 1024);
        if (mag >= SizeSuffixes.Length) mag = SizeSuffixes.Length - 1;

        decimal adjustedSize = (decimal)bytes / (1L << (mag * 10));
        return string.Format("{0:n1} {1}", adjustedSize, SizeSuffixes[mag]);
    }

    public static string FormatPercentChange(long initialBytes, long finalBytes)
    {
        if (initialBytes <= 0) return "0%";
        double diff = (double)(finalBytes - initialBytes) / initialBytes * 100.0;
        if (diff > 0)
            return $"+{diff:F1}%";
        return $"{diff:F1}%";
    }

    /// <summary>
    /// Verilen dosya veya klasör yollarının toplam bayt boyutunu hesaplar.
    /// </summary>
    public static long CalculateTotalBytes(IEnumerable<string> paths)
    {
        long total = 0;
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    total += new FileInfo(path).Length;
                }
                else if (Directory.Exists(path))
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            total += new FileInfo(file).Length;
                        }
                        catch
                        {
                            // Erişilemeyen alt dosyaları atla
                        }
                    }
                }
            }
            catch
            {
                // Erişilemeyen yolları atla
            }
        }
        return total;
    }
}
