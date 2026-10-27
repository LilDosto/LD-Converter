namespace Donustur.Core.Utils;

public static class SafePaths
{
    public static string EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    public static string GetUniquePath(string directory, string desiredFileName)
    {
        EnsureDirectory(directory);
        string targetPath = Path.Combine(directory, desiredFileName);
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return targetPath;
        }

        string nameWithoutExt = Path.GetFileNameWithoutExtension(desiredFileName);
        string ext = Path.GetExtension(desiredFileName);
        int counter = 1;

        do
        {
            targetPath = Path.Combine(directory, $"{nameWithoutExt} ({counter}){ext}");
            counter++;
        } while (File.Exists(targetPath) || Directory.Exists(targetPath));

        return targetPath;
    }
}
