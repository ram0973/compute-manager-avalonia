namespace ComputerManager.Models;

/// <summary>Строка журнала (класс, а не record: нужна ссылочная идентичность для прокрутки списка).</summary>
public sealed class LogEntry(string text)
{
    public string Text { get; } = text;
}
