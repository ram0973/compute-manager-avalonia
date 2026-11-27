using System.Security.Cryptography;
using System.Text;

namespace ComputerManager.Services;

/// <summary>
/// Скрипты из папки Scripts\ вшиты в exe (EmbeddedResource) и достаются оттуда во время работы.
/// На диске рядом с программой их нет — подменить скрипт можно только пересборкой/заменой самого exe.
/// </summary>
public static class ScriptStore
{
    public static string Read(string fileName)
    {
        var asm = typeof(ScriptStore).Assembly;
        using var stream = asm.GetManifestResourceStream("Scripts." + fileName)
            ?? throw new FileNotFoundException($"Скрипт {fileName} не вшит в программу.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\r\n");
    }

    /// <summary>UTF-8 с BOM — иначе Windows PowerShell 5 неверно читает кириллицу.</summary>
    public static byte[] ToUtf8Bom(string content) =>
        new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray();

    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
