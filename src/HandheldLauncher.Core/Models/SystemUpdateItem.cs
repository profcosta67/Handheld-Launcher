namespace HandheldLauncher.Core.Models;

public class SystemUpdateItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
    public string StatusText { get; set; } = "Disponível";
}