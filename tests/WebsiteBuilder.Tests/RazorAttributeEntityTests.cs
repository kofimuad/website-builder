using System.Text.RegularExpressions;

namespace WebsiteBuilder.Tests;

/// <summary>
/// Razor components and Razor Pages disagree about HTML character references, and the disagreement
/// is silent.
///
/// In a .cshtml the markup is written through verbatim, so the browser decodes `&#10;` into a
/// newline. In a .razor the compiler turns a literal attribute into a C# string and the renderer
/// HTML-encodes it on the way out, so the `&` becomes `&amp;` and the user reads the characters
/// `&#10;` on screen. Onboarding's "What do you offer?" textarea shipped that way and showed
/// `Drain clearing&#10;Leak repair&#10;Bathroom fitting` as its placeholder.
///
/// Text content is unaffected — that path emits raw markup, which is why `&mdash;` in a paragraph
/// has always been fine. Only attribute values are wrong, so only attribute values are checked.
///
/// The fix is to write the real character from a C# expression: placeholder="@("a\nb")".
/// </summary>
public class RazorAttributeEntityTests
{
    // An attribute value, in double quotes, containing something shaped like a character reference.
    // Razor expressions (`@(...)`) produce their value at runtime and are not matched by this,
    // which is exactly the distinction being drawn.
    private static readonly Regex EntityInAttribute =
        new(@"[\w:@-]+\s*=\s*""[^""]*&(?:#\d+|#[xX][0-9a-fA-F]+|[a-zA-Z][a-zA-Z0-9]{1,31});[^""]*""",
            RegexOptions.Compiled);

    [Fact]
    public void No_html_entities_in_razor_component_attributes()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(SourceRoot(), "*.razor", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (EntityInAttribute.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "HTML character references in a .razor attribute render literally. Use a C# expression "
            + "instead, e.g. placeholder=\"@(\"a\\nb\")\". Offending lines:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The guard is worthless if it silently scans nothing, so prove it can see the components and
    /// that the pattern still fires on the shape that shipped.
    /// </summary>
    [Fact]
    public void The_guard_scans_real_files_and_still_detects_the_original_bug()
    {
        var components = Directory.GetFiles(SourceRoot(), "*.razor", SearchOption.AllDirectories);
        Assert.NotEmpty(components);

        Assert.Matches(EntityInAttribute, @"placeholder=""Drain clearing&#10;Leak repair""");
        Assert.Matches(EntityInAttribute, @"placeholder=""e.g. &quot;friendlier&quot;""");

        // The corrected form, and ordinary text content, must not trip it.
        Assert.DoesNotMatch(EntityInAttribute, @"placeholder=""@(""Drain clearing\nLeak repair"")""");
        Assert.DoesNotMatch(EntityInAttribute, @"<p class=""hint"">In your own words &mdash; plumber.</p>");
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }
}
