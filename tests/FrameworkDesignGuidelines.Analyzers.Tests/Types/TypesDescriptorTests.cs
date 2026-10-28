using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Types;

public sealed class TypesDescriptorTests
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> _analyzers =
    [
        new AbstractConstructorAnalyzer(),
        new StructDefaultConstructorAnalyzer(),
        new MutableStructAnalyzer(),
        new ValueTypeEquatableAnalyzer(),
        new SingleValueEnumAnalyzer(),
        new InterfaceNestedTypeAnalyzer(),
        new AssemblyAttributesAnalyzer(),
        new FileVersionFormatAnalyzer(),
    ];

    [Fact]
    public async Task Descriptors_are_disabled_by_default_and_in_the_types_category()
    {
        DiagnosticDescriptor[] descriptors = _analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).ToArray();
        Assert.Equal(
            ["FDG4013", "FDG4026", "FDG4027", "FDG4032", "FDG4038", "FDG4056", "FDG4059", "FDG4061", "FDG4062"],
            descriptors.Select(descriptor => descriptor.Id).Order());
        Assert.All(descriptors, descriptor => Assert.False(descriptor.IsEnabledByDefault));
        Assert.All(descriptors, descriptor => Assert.Equal("FrameworkDesignGuidelines.Types", descriptor.Category));

        const string Source =
            """
            [assembly: System.Reflection.AssemblyFileVersion("1.0")]

            public abstract class Claim
            {
                protected internal Claim()
                {
                }
            }

            public struct ZipCode
            {
                public int Code { get; set; }
            }

            public enum SomeOption
            {
                DefaultOption,
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
            ["FDG4013", "FDG4027", "FDG4032", "FDG4038", "FDG4059", "FDG4059", "FDG4059", "FDG4059", "FDG4061", "FDG4062"],
            enabledDiagnostics.Select(diagnostic => diagnostic.Id).Order());
    }
}
