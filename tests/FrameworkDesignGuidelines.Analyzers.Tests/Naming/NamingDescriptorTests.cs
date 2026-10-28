using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class NamingDescriptorTests
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> _analyzers =
    [
        new AcronymCasingAnalyzer(),
        new NonAsciiNameAnalyzer(),
        new DelegateCallbackSuffixAnalyzer(),
        new EventHandlerParameterAnalyzer(),
        new CollectionPropertyNameAnalyzer(),
        new PublicResourceAnalyzer(),
        new GeneralTypeNameAnalyzer(),
        new CompoundWordAnalyzer(),
        new OperatorParameterIndexAnalyzer(),
    ];

    [Fact]
    public async Task Descriptors_are_disabled_by_default_and_in_the_naming_category()
    {
        DiagnosticDescriptor[] descriptors = _analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).ToArray();
        Assert.Equal(
            ["FDG3003", "FDG3004", "FDG3006", "FDG3013", "FDG3033", "FDG3051", "FDG3068", "FDG3075", "FDG3086", "FDG3087"],
            descriptors.Select(descriptor => descriptor.Id).Order());
        Assert.All(descriptors, descriptor => Assert.False(descriptor.IsEnabledByDefault));
        Assert.All(descriptors, descriptor => Assert.Equal("FrameworkDesignGuidelines.Naming", descriptor.Category));

        const string Source =
            """
            using System.Collections.Generic;

            public delegate void Completion(object source, int value);

            public class XMLNode
            {
                public event Completion Done;

                public int Naïve;
            }

            public class Message
            {
                public List<int> ValueList { get; set; }

                public string FileName;

                public static Message operator +(Message m1, Message right)
                {
                    return m1;
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
            ["FDG3004", "FDG3006", "FDG3013", "FDG3033", "FDG3068", "FDG3075", "FDG3086"],
            enabledDiagnostics.Select(diagnostic => diagnostic.Id).Order());
    }
}
