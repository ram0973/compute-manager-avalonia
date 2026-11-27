using CommunityToolkit.Mvvm.ComponentModel;

namespace ComputerManager.Models;

public partial class UserInfo : ObservableObject
{
    public required string Login { get; init; }
    public string? FullName { get; init; }
    public string? Phone { get; init; }
    public string? Mobile { get; init; }

    /// <summary>ПК, где пользователь найден после поиска («Найти ПК, где залогинен»).</summary>
    [ObservableProperty] public partial string? Computers { get; set; }
}
