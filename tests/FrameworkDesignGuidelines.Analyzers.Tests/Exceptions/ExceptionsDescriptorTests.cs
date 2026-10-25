using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Exceptions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Exceptions;

public sealed class ExceptionsDescriptorTests
{
    private static readonly string[] _ids =
    [
        "FDG7007", "FDG7008", "FDG7010", "FDG7021", "FDG7022", "FDG7036", "FDG7044", "FDG7047", "FDG7051", "FDG7064",
    ];

    private static readonly ImmutableArray<DiagnosticAnalyzer> _analyzers =
    [
        new ExceptionSignatureAnalyzer(),
        new ExceptionFilterAnalyzer(),
        new ExceptionMessageAnalyzer(),
        new InnerExceptionAnalyzer(),
        new SetterParameterNameAnalyzer(),
        new CatchClauseAnalyzer(),
        new TryMethodAnalyzer(),
    ];

    [Fact]
    public async Task Descriptors_are_disabled_by_default_and_in_the_exceptions_category()
    {
        DiagnosticDescriptor[] descriptors = _analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).ToArray();
        Assert.Equal(_ids, descriptors.Select(descriptor => descriptor.Id).Order());
        Assert.All(descriptors, descriptor => Assert.False(descriptor.IsEnabledByDefault));
        Assert.All(
            descriptors,
            descriptor => Assert.Equal("FrameworkDesignGuidelines.Exceptions", descriptor.Category));

        const string Source =
            """
            using System;
            using System.Threading.Tasks;

            public class Loader
            {
                private string _name;

                public string Name
                {
                    set { _name = value ?? throw new ArgumentNullException(nameof(Name)); }
                }

                public Exception Create(bool throwOnError)
                {
                    if (throwOnError)
                    {
                        throw new NotSupportedException("Why?");
                    }

                    return null;
                }

                public int TryLoad(out int value)
                {
                    value = 0;
                    return 0;
                }

                public async Task RunAsync(Func<Task> action)
                {
                    try
                    {
                        await action();
                    }
                    catch (TaskCanceledException)
                    {
                    }
                    catch (StackOverflowException)
                    {
                    }
                    catch (InvalidOperationException e) when ((e.Message ?? throw new FormatException()) != null)
                    {
                    }
                    catch (Exception)
                    {
                        throw new InvalidOperationException("Wrapped");
                    }
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

        Assert.Equal(_ids, enabledDiagnostics.Select(diagnostic => diagnostic.Id).Order());
    }
}
