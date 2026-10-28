using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Extensibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Extensibility;

public sealed class ExtensibilityDescriptorTests
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> _analyzers =
    [
        new PublicVirtualMemberAnalyzer(),
        new BaseSuffixAnalyzer(),
    ];

    [Fact]
    public async Task Descriptors_are_disabled_by_default_and_in_the_extensibility_category()
    {
        DiagnosticDescriptor[] descriptors = _analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).ToArray();
        Assert.Equal(["FDG6013", "FDG6019"], descriptors.Select(descriptor => descriptor.Id).Order());
        Assert.All(descriptors, descriptor => Assert.False(descriptor.IsEnabledByDefault));
        Assert.All(
            descriptors,
            descriptor => Assert.Equal("FrameworkDesignGuidelines.Extensibility", descriptor.Category));

        const string Source =
            """
            public class ItemBase
            {
                public virtual void Select()
                {
                }
            }

            public class Directory
            {
                public ItemBase First => null;
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Disabled",
            [CSharpSyntaxTree.ParseText(Source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(_analyzers)
            .GetAnalyzerDiagnosticsAsync();

        Assert.Empty(diagnostics);

        // Control: the same source does produce diagnostics once a configuration turns the rules on.
        CSharpCompilation enabled = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
            descriptors.ToDictionary(descriptor => descriptor.Id, _ => ReportDiagnostic.Warn)));
        ImmutableArray<Diagnostic> enabledDiagnostics = await enabled
            .WithAnalyzers(_analyzers)
            .GetAnalyzerDiagnosticsAsync();

        Assert.Equal(["FDG6013", "FDG6019"], enabledDiagnostics.Select(diagnostic => diagnostic.Id).Order());
    }
}
