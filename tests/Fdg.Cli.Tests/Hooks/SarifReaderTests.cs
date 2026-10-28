using Fdg.Cli.Hooks;

namespace Fdg.Cli.Tests.Hooks;

public sealed class SarifReaderTests
{
    [Fact]
    public void Sarif_start_column_is_read_and_defaults_to_1()
    {
        using var tree = new TempTree();
        string log = tree.Write(
            "build.sarif",
            """
            { "version": "2.1.0", "runs": [ { "results": [
              { "ruleId": "IDE1006", "message": { "text": "with column" },
                "locations": [ { "physicalLocation": {
                  "artifactLocation": { "uri": "a.cs" },
                  "region": { "startLine": 4, "startColumn": 17 } } } ] },
              { "ruleId": "IDE1006", "message": { "text": "region without column" },
                "locations": [ { "physicalLocation": {
                  "artifactLocation": { "uri": "a.cs" },
                  "region": { "startLine": 5 } } } ] },
              { "ruleId": "IDE1006", "message": { "text": "no region" },
                "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "a.cs" } } } ] }
            ] } ] }
            """);

        IReadOnlyList<SarifDiagnostic> diagnostics = SarifReader.Read(log, tree.Root);

        Assert.Equal([(4, 17), (5, 1), (0, 1)], diagnostics.Select(d => (d.Line, d.Column)));
    }
}
