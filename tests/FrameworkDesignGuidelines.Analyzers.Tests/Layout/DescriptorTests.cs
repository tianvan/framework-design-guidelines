using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Layout;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Layout;

public sealed class DescriptorTests
{
    [Fact]
    public async Task Descriptors_are_disabled_by_default()
    {
        Assert.False(FdgDescriptors.FDGA003.IsEnabledByDefault);
        Assert.False(FdgDescriptors.FDGA004.IsEnabledByDefault);
        Assert.False(FdgDescriptors.FDGA023.IsEnabledByDefault);

        const string Source =
            """
            class C
            {
                void M()
                {
                    try { M(); } catch { }
                    if (Equals(null)) M();
                    Failed(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30);
                }

                static void Failed(params int[] values)
                {
                }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Disabled",
            [CSharpSyntaxTree.ParseText(Source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        ImmutableArray<DiagnosticAnalyzer> analyzers =
            [new SingleLineCatchFinallyAnalyzer(), new OmittedBracesAnalyzer(), new LongListWrappingAnalyzer()];

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync();

        Assert.Empty(diagnostics);

        // Control: the same source does produce diagnostics once a configuration turns the rules on.
        CSharpCompilation enabled = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
            new Dictionary<string, ReportDiagnostic>
            {
                ["FDGA003"] = ReportDiagnostic.Warn,
                ["FDGA004"] = ReportDiagnostic.Warn,
                ["FDGA023"] = ReportDiagnostic.Warn,
            }));
        ImmutableArray<Diagnostic> enabledDiagnostics = await enabled
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync();

        Assert.Equal(
            ["FDGA003", "FDGA003", "FDGA004", "FDGA023"],
            enabledDiagnostics.Select(diagnostic => diagnostic.Id).Order());
    }
}
