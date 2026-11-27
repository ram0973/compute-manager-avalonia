using System.Reflection;
using System.Security.Cryptography;

namespace ComputerManager.Services;

/// <summary>
/// Проверка целостности файлов из папки Install. SHA-256 каждого файла вшивается в exe при сборке
/// (см. цель PinInstallHashes в .csproj), перед использованием файл сверяется с вшитым хэшем.
/// Подмена PsExec64.exe или 7-zip.msi после сборки обнаруживается и блокируется.
/// </summary>
public static class InstallIntegrity
{
    private const string Prefix = "InstallSha256:";

    private static readonly Dictionary<string, string> Pinned = typeof(InstallIntegrity).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Where(a => a.Key.StartsWith(Prefix, StringComparison.Ordinal) && !string.IsNullOrEmpty(a.Value))
        .GroupBy(a => a.Key[Prefix.Length..], StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Value!, StringComparer.OrdinalIgnoreCase);

    /// <summary>null — файл на месте и совпадает с вшитым хэшем; иначе текст ошибки.</summary>
    public static string? Verify(string fullPath, out string sha256)
    {
        sha256 = "";
        var name = Path.GetFileName(fullPath);

        if (!File.Exists(fullPath)) return $"Не найден файл: {fullPath}";

        if (!Pinned.TryGetValue(name, out var expected))
            return $"{name}: хэш не вшит в программу. Положите файл в Install\\ и пересоберите проект.";

        using var fs = File.OpenRead(fullPath);
        var actual = Convert.ToHexString(SHA256.HashData(fs));

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return $"{name}: ФАЙЛ ИЗМЕНЁН после сборки (SHA-256 {actual[..12]}… вместо {expected[..12]}…). Использование заблокировано.";

        sha256 = actual;
        return null;
    }
}
