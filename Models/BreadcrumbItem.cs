namespace DropAndForget.Models;

public class BreadcrumbItem
{
    public string Label { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public bool IsCurrent { get; set; }
}
