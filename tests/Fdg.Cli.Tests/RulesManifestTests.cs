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
