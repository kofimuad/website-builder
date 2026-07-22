using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Tests;

public class ThemePresetTests
{
    [Fact]
    public void There_are_at_least_eight_presets_with_unique_ids()
    {
        Assert.True(ThemePresetCatalog.Presets.Count >= 8);
        Assert.Equal(ThemePresetCatalog.Presets.Count, ThemePresetCatalog.Presets.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Every_preset_meets_WCAG_AA_on_the_section_templates()
    {
        var failures = new List<string>();

        foreach (var preset in ThemePresetCatalog.Presets)
        {
            var p = preset.Theme.Palette;

            // The contrast pairs the templates actually rely on for text and buttons.
            Check(preset.Id, "text on background", p.Text, p.Background, failures);
            Check(preset.Id, "text on surface", p.Text, p.Surface, failures);
            Check(preset.Id, "muted text on background", p.MutedText, p.Background, failures);
            Check(preset.Id, "muted text on surface", p.MutedText, p.Surface, failures);
            Check(preset.Id, "white on primary button", "#ffffff", p.Primary, failures);
            Check(preset.Id, "primary link on background", p.Primary, p.Background, failures);
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static void Check(string presetId, string pair, string fg, string bg, List<string> failures)
    {
        var ratio = Wcag.ContrastRatio(fg, bg);
        if (ratio < Wcag.AaNormal)
        {
            failures.Add($"{presetId}: {pair} ({fg} on {bg}) is {ratio:0.00}:1, needs {Wcag.AaNormal:0.0}:1");
        }
    }

    [Fact]
    public void By_id_returns_the_preset_and_falls_back_for_unknown_ids()
    {
        Assert.Equal("navy", ThemePresetCatalog.ById("navy").Id);
        Assert.NotNull(ThemePresetCatalog.ById("does-not-exist"));
    }

    [Fact]
    public void Presets_are_visually_distinct_so_another_look_is_actually_different()
    {
        var primaries = ThemePresetCatalog.Presets.Select(p => p.Theme.Palette.Primary).ToList();
        Assert.Equal(primaries.Count, primaries.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_cloned_theme_is_independent_of_the_catalog()
    {
        var preset = ThemePresetCatalog.ById("navy");
        var clone = preset.Theme.Clone();

        clone.Palette.Primary = "#000000";

        Assert.NotEqual("#000000", ThemePresetCatalog.ById("navy").Theme.Palette.Primary);
    }

    [Fact]
    public void Applying_a_preset_changes_the_look_but_not_the_copy()
    {
        // What "try another look" does: swap the theme, leave every section untouched.
        var draft = new SiteDefinition
        {
            Theme = ThemePresetCatalog.ById("evergreen").Theme.Clone(),
            Sections = [new HeroSection { Headline = "My words" }, new AboutSection { Heading = "About", Body = "My story" }],
        };
        var sectionsBefore = draft.Sections;

        draft.Theme = ThemePresetCatalog.ById("berry").Theme.Clone();

        Assert.Same(sectionsBefore, draft.Sections);
        Assert.Equal("My words", ((HeroSection)draft.Sections[0]).Headline);
        Assert.Equal(ThemePresetCatalog.ById("berry").Theme.Palette.Primary, draft.Theme.Palette.Primary);
    }
}
