using System.Xml.Linq;
using Fdg.Cli.Commands;
using Fdg.Cli.Rules;

namespace Fdg.Cli.Tests;

public sealed class RulesManifestTests
{
    [Fact]
    public void Package_props_rules_json_and_CLI_agree_on_the_protocol()
    {
        string package = Path.Combine(RepositoryRoot(), "src", "FrameworkDesignGuidelines.Package");
        string props = XDocument.Load(Path.Combine(package, "build", "FrameworkDesignGuidelines.Analyzers.props"))
            .Descendants("FdgProtocolVersion")
            .Single()
            .Value;

        Assert.Equal(FixCommand.SupportedProtocol.ToString(System.Globalization.CultureInfo.InvariantCulture), props);
        // data/rules.json is written by the generator (RulesJsonEmitter.ProtocolVersion; see EmitterTests).
        Assert.Equal(FixCommand.SupportedProtocol, RulesManifest.Load(Path.Combine(package, "data", "rules.json")).Protocol);
        Assert.Equal(FixCommand.SupportedProtocol, RulesManifest.LoadEmbedded().Protocol);
    }

    [Fact]
    public void Newer_protocol_is_rejected_with_one_line()
    {
        using var tree = new TempTree();
        string path = tree.Write(
            "rules.json",
            """{ "protocol": 2, "version": "9.0.0", "rules": [], "registeredDiagnostics": [], "breakingFixDiagnostics": [] }""");

        var e = Assert.Throws<NotSupportedException>(() => RulesManifest.LoadOrEmbedded(path));

        Assert.Equal(
            "rules.json protocol 2 is newer than this dotnet-fdg supports (1); update it with: dotnet tool update -g dotnet-fdg",
            e.Message);
    }

    /// <summary>
    /// breakingFixDiagnostics is derived from the rules' autofix values; this pins representative entries of the
    /// shipped data, so a rule set back to autofix yes by mistake is caught here.
    /// </summary>
    [Fact]
    public void Shipped_rules_json_holds_the_breaking_fix_diagnostics()
    {
        string path = Path.Combine(RepositoryRoot(), "src", "FrameworkDesignGuidelines.Package", "data", "rules.json");

        RulesManifest manifest = RulesManifest.Load(path);

        Assert.All(
            new[] { "CA1710", "CA1711", "CA1724", "CA1034", "FDG3006", "FDG4026" },
            id => Assert.Contains(id, manifest.BreakingFixDiagnostics));
        Assert.DoesNotContain("IDE0055", manifest.BreakingFixDiagnostics);
        Assert.Equal(manifest.BreakingFixDiagnostics, RulesManifest.LoadEmbedded().BreakingFixDiagnostics);
    }

    /// <summary>
    /// Only diagnostics whose every rule is autofix yes are applied: the code fixes of CA1066, CA2225 and CA1036 add
    /// public members that throw NotImplementedException, and CA2007's fix needs judgment.
    /// </summary>
    [Fact]
    public void Shipped_rules_json_fixes_only_autofix_yes_diagnostics()
    {
        string path = Path.Combine(RepositoryRoot(), "src", "FrameworkDesignGuidelines.Package", "data", "rules.json");

        RulesManifest manifest = RulesManifest.Load(path);

        Assert.All(
            new[] { "CA1066", "CA1036", "CA2225", "CA2007", "CA1710" },
            id => Assert.DoesNotContain(id, manifest.FixDiagnostics));
        Assert.All(new[] { "FDGA004", "IDE0055", "IDE0040" }, id => Assert.Contains(id, manifest.FixDiagnostics));
        Assert.Equal(manifest.FixDiagnostics, RulesManifest.LoadEmbedded().FixDiagnostics);
    }

    [Fact]
    public void Manifest_without_fix_diagnostics_computes_them_from_the_rules()
    {
        using var tree = new TempTree();
        string path = tree.Write(
            "rules.json",
            """
            { "protocol": 1, "version": "0.1.0",
              "rules": [
                { "id": "FDGA001", "strength": "DO", "text": "a", "section": "A", "diagnostics": ["IDE0055", "IDE0040"], "autofix": "yes", "severity": {} },
                { "id": "FDGA002", "strength": "DO", "text": "b", "section": "A", "diagnostics": ["IDE0040", "CA1066"], "autofix": "no", "severity": {} },
                { "id": "FDGA003", "strength": "DO", "text": "c", "section": "A", "diagnostics": ["CA1710"], "autofix": "breaking", "severity": {} },
                { "id": "FDGA004", "strength": "DO", "text": "d", "section": "A", "diagnostics": ["FDGA004"], "autofix": "yes", "severity": {} }
              ],
              "registeredDiagnostics": ["CA1066", "CA1710", "FDGA004", "IDE0040", "IDE0055"],
              "breakingFixDiagnostics": ["CA1710"] }
            """);

        RulesManifest manifest = RulesManifest.Load(path);

        Assert.Equal(["FDGA004", "IDE0055"], manifest.FixDiagnostics);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FrameworkDesignGuidelines.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("FrameworkDesignGuidelines.slnx not found above " + AppContext.BaseDirectory);
    }
}
