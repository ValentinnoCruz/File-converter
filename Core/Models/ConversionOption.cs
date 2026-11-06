namespace RadialConvert.Core.Models;

public class ConversionOption
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetExtension { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#FF6619"; // O-Tangerine Citrus Orange
    public string Category { get; set; } = "Image";
}
