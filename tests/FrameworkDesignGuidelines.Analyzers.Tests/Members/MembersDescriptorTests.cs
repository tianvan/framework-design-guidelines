using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Members;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class MembersDescriptorTests
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> _analyzers =
    [
        new OverloadParameterOrderAnalyzer(),
        new VirtualOverloadAnalyzer(),
        new RefKindOverloadAnalyzer(),
        new GenericOverloadAnalyzer(),
        new DefaultParameterAnalyzer(),
        new MultiParameterIndexerAnalyzer(),
        new RangeIndexerAnalyzer(),
        new ConstructorParameterNameAnalyzer(),
    ];

    [Fact]
    public async Task Descriptors_are_disabled_by_default_and_in_the_members_category()
    {
        DiagnosticDescriptor[] descriptors = _analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).ToArray();
        Assert.Equal(
            [
                "FDG5003", "FDG5004", "FDG5005", "FDG5008", "FDG5010", "FDG5011", "FDG5013", "FDG5015", "FDG5034",
                "FDG5040", "FDG5046",
            ],
            descriptors.Select(descriptor => descriptor.Id).Order());
        Assert.All(descriptors, descriptor => Assert.False(descriptor.IsEnabledByDefault));
        Assert.All(descriptors, descriptor => Assert.Equal("FrameworkDesignGuidelines.Members", descriptor.Category));

        const string Source =
            """
            public interface IExample
            {
                void Print(int value = 5);
            }

            public class Grid
            {
                public Grid(string name)
                {
                    Title = name;
                }

                public string Title { get; }

                public int this[int row, int column] => 0;

                public virtual void Fill()
                {
                }

                public virtual void Fill(int value)
                {
                }
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

        Assert.Equal(
            ["FDG5004", "FDG5011", "FDG5034", "FDG5046"],
            enabledDiagnostics.Select(diagnostic => diagnostic.Id).Order());
    }
}
