namespace RadialConvert.Core.Models;

public class ConversionOption
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetExtension { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#3B82F6"; // Modern blue default
    public string IconKind { get; set; } = "image";
}
