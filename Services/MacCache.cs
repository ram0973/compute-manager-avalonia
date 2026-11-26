using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComputerManager.Services;

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class MacCacheJsonContext : JsonSerializerContext;

/// <summary>AD не хранит MAC, поэтому запоминаем его локально (ApplicationData/ComputerManager/macs.json).</summary>
public class MacCache
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComputerManager", "macs.json");

    private Dictionary<string, string> _data = new(StringComparer.OrdinalIgnoreCase);

    public static MacCache Load()
    {
        var cache = new MacCache();
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(FilePath),
                    MacCacheJsonContext.Default.DictionaryStringString);
                if (loaded is not null)
                    cache._data = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        return cache;
    }

    public string? Get(string name) => _data.GetValueOrDefault(name);

    public void Set(string name, string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) _data.Remove(name); else _data[name] = mac;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, MacCacheJsonContext.Default.DictionaryStringString));
    }
}
