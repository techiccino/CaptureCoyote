using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public static class StylePresetCatalog
{
    private static readonly IReadOnlyList<StylePresetDefinition> Presets =
    [
        new StylePresetDefinition
        {
            Kind = StylePresetKind.BugReport,
            DisplayName = "Bug Report",
            Description = "Higher-contrast callouts for bugs, QA notes, and issue triage.",
            AccentColor = new ArgbColor(255, 224, 80, 80),
            StrokeColor = new ArgbColor(255, 224, 80, 80),
            FillColor = ArgbColor.Transparent,
            StrokeThickness = 5,
            Opacity = 1,
            FontSize = 28,
            BlurStrength = 16
        },
        new StylePresetDefinition
        {
            Kind = StylePresetKind.Documentation,
            DisplayName = "Documentation",
            Description = "Balanced defaults for clean docs, specs, and release notes.",
            AccentColor = ArgbColor.Accent,
            StrokeColor = ArgbColor.Accent,
            FillColor = ArgbColor.Transparent,
            StrokeThickness = 4,
            Opacity = 1,
            FontSize = 26,
            BlurStrength = 12
        },
        new StylePresetDefinition
        {
            Kind = StylePresetKind.Tutorial,
            DisplayName = "Tutorial",
            Description = "Warmer callouts that read clearly in walkthroughs and training.",
            AccentColor = new ArgbColor(255, 245, 166, 35),
            StrokeColor = new ArgbColor(255, 245, 166, 35),
            FillColor = ArgbColor.Transparent,
            StrokeThickness = 5,
            Opacity = 1,
            FontSize = 30,
            BlurStrength = 14
        }
    ];

    public static IReadOnlyList<StylePresetDefinition> All => Presets;

    public static StylePresetDefinition Get(StylePresetKind kind)
    {
        return Presets.FirstOrDefault(preset => preset.Kind == kind) ?? Presets[1];
    }
}
