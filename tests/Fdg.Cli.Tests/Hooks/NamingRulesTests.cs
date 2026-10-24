using Fdg.Cli.Hooks;
using Fdg.Cli.Rules;

namespace Fdg.Cli.Tests.Hooks;

/// <summary>Which rules IDE1006 quotes, against the manifest embedded in the CLI.</summary>
public sealed class NamingRulesTests
{
    private static readonly RulesManifest Manifest = RulesManifest.LoadEmbedded();

    private static string[] Quoted(NamingSymbolKind? kind, NamingFailure? failure, string? profile = "library") =>
        StopReport.RulesFor(
                Manifest,
                "IDE1006",
                "Naming rule violation",
                "int x;",
                profile,
                kind is { } k && failure is { } f ? (k, f) : null)
            .Select(r => r.Id)
            .ToArray();

    [Fact]
    public void Missing_prefix_on_a_private_field_quotes_only_FDGA055()
    {
        Assert.Equal(["FDGA055"], Quoted(NamingSymbolKind.PrivateField, NamingFailure.Prefix));
    }

    [Fact]
    public void Lower_case_method_quotes_FDG3001_and_FDGA052()
    {
        string[] quoted = Quoted(NamingSymbolKind.Method, NamingFailure.Casing);

        Assert.Equal(2, quoted.Length);
        Assert.Contains("FDG3001", quoted);
        Assert.Contains("FDGA052", quoted);
    }

    [Fact]
    public void Upper_case_parameter_quotes_parameter_rules_only()
    {
        string[] quoted = Quoted(NamingSymbolKind.Parameter, NamingFailure.Casing);

        Assert.InRange(quoted.Length, 1, StopReport.MaxRulesPerIssue);
        Assert.All(quoted, id => Assert.Contains(id, new[] { "FDG3002", "FDG3080", "FDGA057", "FDG3005" }));
    }

    [Fact]
    public void Unreadable_location_quotes_no_rule()
    {
        Assert.Empty(Quoted(null, null));
        Assert.Empty(Quoted(null, NamingFailure.Casing));
        Assert.Empty(Quoted(NamingSymbolKind.Method, null));
    }

    [Fact]
    public void A_failure_no_rule_covers_for_the_kind_quotes_no_rule()
    {
        Assert.Empty(Quoted(NamingSymbolKind.Method, NamingFailure.Prefix));
        Assert.Empty(Quoted(NamingSymbolKind.PrivateField, NamingFailure.Casing).Except(["FDG3005", "FDGA054"]));
    }

    [Theory]
    [InlineData(NamingSymbolKind.Namespace, "FDG3030")]
    [InlineData(NamingSymbolKind.ConstField, "FDGA053")]
    [InlineData(NamingSymbolKind.ConstLocal, "FDGA053")]
    [InlineData(NamingSymbolKind.Local, "FDGA056")]
    [InlineData(NamingSymbolKind.PrivateField, "FDGA054")]
    [InlineData(NamingSymbolKind.TypeParameter, "FDG3001")]
    public void Each_symbol_kind_finds_its_rule(NamingSymbolKind kind, string expected)
    {
        Assert.Contains(expected, Quoted(kind, NamingFailure.Casing, profile: null));
    }

    [Fact]
    public void Unexpected_prefix_on_a_public_field_quotes_FDG3079()
    {
        Assert.Equal(["FDG3079"], Quoted(NamingSymbolKind.VisibleField, NamingFailure.Prefix));
    }

    [Fact]
    public void Lower_case_public_field_quotes_the_visible_field_rules()
    {
        string[] quoted = Quoted(NamingSymbolKind.VisibleField, NamingFailure.Casing);

        Assert.All(quoted, id => Assert.Contains(id, new[] { "FDG3001", "FDG3077", "FDG3079", "FDGA052" }));
    }

    [Fact]
    public void Naming_tags_apply_to_IDE1006_only_so_CA1707_keeps_its_rules()
    {
        // CA1707 and IDE1006 share FDG3079; the tags of FDG3079 must not empty the list for CA1707.
        string[] quoted = StopReport.RulesFor(
                Manifest,
                "CA1707",
                "Remove the underscores from member name Order_Id",
                "public int Order_Id;",
                "library",
                null)
            .Select(r => r.Id)
            .ToArray();

        Assert.Contains("FDG3010", quoted);
        Assert.Contains("FDG3079", quoted);
    }

    [Fact]
    public void Diagnostics_without_naming_tags_still_use_the_word_overlap_filter()
    {
        var none = new Dictionary<string, string>();
        var manifest = new RulesManifest(
            1,
            "0",
            [
                new RuleEntry("FDG1", "DO", "avoid underscores", "1", ["CA1707"], "no", none),
                new RuleEntry("FDG2", "DO", "use pascal casing", "1", ["CA1707"], "no", none),
                new RuleEntry("FDG3", "DO", "use camel casing", "1", ["CA1707"], "no", none),
            ],
            [],
            []);

        IReadOnlyList<RuleEntry> rules = StopReport.RulesFor(manifest, "CA1707", "Remove the underscores", "int a;", null, null);

        Assert.Equal(["FDG1"], rules.Select(r => r.Id));
    }

    [Fact]
    public void Every_tagged_rule_in_the_manifest_lists_IDE1006_and_known_values()
    {
        RuleEntry[] tagged = Manifest.Rules.Where(r => r.Naming is not null).ToArray();
        string[] kinds = Enum.GetNames<NamingSymbolKind>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]).ToArray();

        Assert.Equal(13, tagged.Length);
        Assert.All(tagged, r => Assert.Contains("IDE1006", r.Diagnostics));
        Assert.All(tagged.SelectMany(r => r.Naming!.Symbols), s => Assert.Contains(s, kinds));
        Assert.All(tagged.SelectMany(r => r.Naming!.Failures), f => Assert.Contains(f, new[] { "casing", "prefix" }));
    }
}
