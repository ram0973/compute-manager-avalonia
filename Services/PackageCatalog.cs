using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using ComputerManager.Models;

namespace ComputerManager.Services;

public record PackageDto(string Name, string Installer, string Sha256, string Arguments,
    string UninstallPattern, string UninstallArguments, string KillProcesses);

/// <summary>
/// Каталог пакетов Install\packages.json. SHA-256 каждого установщика хранится в каталоге (а не вшит в exe),
/// поэтому добавить программу можно без пересборки: «Добавить» в окне «Установка программ» сам посчитает хэш.
/// Берегите права на запись в папку Install — каталог и есть «доверенный список».
/// </summary>
public static class PackageCatalog
{
    public static readonly string CatalogPath = Path.Combine(AppPaths.InstallDir, "packages.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<PackageInfo> Load()
    {
        if (!File.Exists(CatalogPath))
        {
            // первый запуск: если 7-zip.msi уже лежит в Install\7-zip — заводим пакет автоматически
            var list = new List<PackageInfo>();
            var msi = Path.Combine("7-zip", "7-zip.msi");
            if (File.Exists(Path.Combine(AppPaths.InstallDir, msi)))
            {
                list.Add(new PackageInfo
                {
                    Name = "7-Zip",
                    Installer = msi,
                    Sha256 = Sha256Of(Path.Combine(AppPaths.InstallDir, msi)),
                    UninstallPattern = "7-Zip*",
                    KillProcesses = "7zFM,7zG",
                });
                try { Save(list); } catch { /* нет прав на запись — будет работать из памяти */ }
            }
            return list;
        }

        var dtos = JsonSerializer.Deserialize<List<PackageDto>>(File.ReadAllText(CatalogPath), Options) ?? [];
        return dtos.Select(d => new PackageInfo
        {
            Name = d.Name ?? "",
            Installer = d.Installer ?? "",
            Sha256 = d.Sha256 ?? "",
            Arguments = d.Arguments ?? "",
            UninstallPattern = d.UninstallPattern ?? "",
            UninstallArguments = string.IsNullOrWhiteSpace(d.UninstallArguments) ? "/S" : d.UninstallArguments,
            KillProcesses = d.KillProcesses ?? "",
        }).ToList();
    }

    public static void Save(IEnumerable<PackageInfo> packages)
    {
        Directory.CreateDirectory(AppPaths.InstallDir);
        var dtos = packages.Select(p => new PackageDto(p.Name, p.Installer, p.Sha256, p.Arguments,
            p.UninstallPattern, p.UninstallArguments, p.KillProcesses));
        File.WriteAllText(CatalogPath, JsonSerializer.Serialize(dtos, Options));
    }

    public static string Sha256Of(string fullPath)
    {
        using var fs = File.OpenRead(fullPath);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    /// <summary>null — файл на месте, хэш совпадает; иначе текст ошибки.</summary>
    public static string? Verify(PackageInfo p)
    {
        var ext = Path.GetExtension(p.Installer).ToLowerInvariant();
        if (ext is not (".msi" or ".exe")) return $"{p.Name}: поддерживаются только .msi и .exe";
        if (!File.Exists(p.FullPath)) return $"{p.Name}: нет файла {p.FullPath}";
        if (string.IsNullOrWhiteSpace(p.Sha256)) return $"{p.Name}: в каталоге не задан SHA-256 (кнопка «Пересчитать хэш»)";

        var actual = Sha256Of(p.FullPath);
        return actual.Equals(p.Sha256.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{p.Name}: ХЭШ НЕ СОВПАДАЕТ — файл изменён (в каталоге {p.Sha256[..Math.Min(12, p.Sha256.Length)]}…, на диске {actual[..12]}…)";
    }

    /// <summary>Обновляет колонку «Статус хэша».</summary>
    public static void Refresh(PackageInfo p)
    {
        p.HashStatus = Verify(p) is null ? "✔ ок" :
            !File.Exists(p.FullPath) ? "✘ нет файла" :
            string.IsNullOrWhiteSpace(p.Sha256) ? "✘ хэш не задан" : "✘ не совпадает";
    }
}
