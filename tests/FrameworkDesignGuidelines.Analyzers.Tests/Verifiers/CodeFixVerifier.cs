using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Verifiers;

/// <summary>
/// Runs every code fix test against .NET Framework 4.7.2 and .NET 8 reference assemblies,
/// each under C# 7.3 and the latest language version, with the rule enabled through an
/// .editorconfig the way a profile globalconfig would enable it.
/// </summary>
/// <remarks>
/// On top of the testing library, each test also applies FixAll in a plain workspace until no diagnostic is left,
/// and requires the exact expected text, including its line endings, and a result the formatter leaves unchanged.
/// </remarks>
internal static class CodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    private const int MaxFixAllPasses = 5;

    private static readonly ReferenceAssemblies[] _referenceAssemblies =
    [
        ReferenceAssemblies.NetFramework.Net472.Default,
        ReferenceAssemblies.Net.Net80,
    ];

    private static readonly LanguageVersion[] _languageVersions = [LanguageVersion.CSharp7_3, LanguageVersion.Latest];

    public static async Task VerifyAsync(
        string source,
        string fixedSource,
        string? extraEditorConfig = null,
        string endOfLine = "\n",
        int? fixAllIterations = null,
        bool checkFormatter = true)
    {
        // Raw string literals take the line endings of this file, which depend on the checkout (core.autocrlf).
        source = NormalizeEndOfLine(source, endOfLine);
        fixedSource = NormalizeEndOfLine(fixedSource, endOfLine);
        string editorConfig = EditorConfig(extraEditorConfig);
        foreach (ReferenceAssemblies referenceAssemblies in _referenceAssemblies)
        {
            foreach (LanguageVersion languageVersion in _languageVersions)
            {
                var test = new Test(languageVersion)
                {
                    TestCode = source,
                    FixedCode = fixedSource,
                    ReferenceAssemblies = referenceAssemblies,
                };

                if (fixAllIterations is int iterations)
                {
                    test.NumberOfFixAllIterations = iterations;
                    test.NumberOfFixAllInDocumentIterations = iterations;
                    test.NumberOfIncrementalIterations = iterations;
                }

                test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorConfig));
                await test.RunAsync();
            }
        }

        TestFileMarkupParser.GetSpans(source, out string plainSource, out ImmutableDictionary<string, ImmutableArray<TextSpan>> _);
        string fixedAll = await FixAllUntilCleanAsync(plainSource, editorConfig);
        AssertEndOfLine(endOfLine, fixedAll);
        Assert.Equal(fixedSource, fixedAll);

        if (checkFormatter && source != fixedSource)
        {
            await AssertFormatterLeavesUnchangedAsync(fixedSource, extraEditorConfig);
        }
    }

    public static Task VerifyNoDiagnosticsAsync(string source, string? extraEditorConfig = null)
    {
        return VerifyAsync(source, source, extraEditorConfig);
    }

    /// <summary>
    /// The fixed text must be stable under <c>dotnet format whitespace</c> (IDE0055 with Roslyn defaults, plus
    /// the test's .editorconfig, for example <c>indent_style = tab</c>).
    /// </summary>
    public static async Task AssertFormatterLeavesUnchangedAsync(string text, string? extraEditorConfig = null)
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateDocument(workspace, text, EditorConfig(extraEditorConfig));
        Document formatted = await Formatter.FormatAsync(document);
        string formattedText = (await formatted.GetTextAsync()).ToString();
        Assert.Equal(text, formattedText);
    }

    private static void AssertEndOfLine(string endOfLine, string text)
    {
        if (endOfLine == "\n")
        {
            Assert.DoesNotContain('\r', text);
        }
        else
        {
            Assert.Equal(text.Split('\n').Length, text.Split(["\r\n"], StringSplitOptions.None).Length);
        }
    }

    /// <summary>Applies the fix's FixAll (document scope) until the analyzer reports nothing.</summary>
    private static async Task<string> FixAllUntilCleanAsync(string source, string editorConfig)
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateDocument(workspace, source, editorConfig);
        var analyzer = new TAnalyzer();
        var codeFix = new TCodeFix();
        for (int pass = 0; pass < MaxFixAllPasses; pass++)
        {
            Compilation compilation = (await document.Project.GetCompilationAsync())!;
            ImmutableArray<Diagnostic> diagnostics = (await compilation
                    .WithAnalyzers([analyzer], document.Project.AnalyzerOptions)
                    .GetAnalyzerDiagnosticsAsync())
                .Where(diagnostic => codeFix.FixableDiagnosticIds.Contains(diagnostic.Id))
                .ToImmutableArray();
            if (diagnostics.IsEmpty)
            {
                return (await document.GetTextAsync()).ToString();
            }

            var actions = new List<CodeAction>();
            await codeFix.RegisterCodeFixesAsync(new CodeFixContext(
                document,
                diagnostics[0],
                (action, _) => actions.Add(action),
                CancellationToken.None));
            var context = new FixAllContext(
                document,
                codeFix,
                FixAllScope.Document,
                actions.Single().EquivalenceKey,
                codeFix.FixableDiagnosticIds,
                new FixedDiagnosticProvider(diagnostics),
                CancellationToken.None);
            CodeAction fixAll = (await codeFix.GetFixAllProvider()!.GetFixAsync(context))!;
            ApplyChangesOperation operation = (await fixAll.GetOperationsAsync(CancellationToken.None))
                .OfType<ApplyChangesOperation>()
                .Single();
            document = operation.ChangedSolution.GetDocument(document.Id)!;
        }

        throw new InvalidOperationException($"Diagnostics remain after {MaxFixAllPasses} FixAll passes.");
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string text, string editorConfig)
    {
        string directory = Path.Combine(Path.GetTempPath(), "fdg-analyzer-tests");
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        Solution solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Test",
                "Test",
                LanguageNames.CSharp,
                filePath: Path.Combine(directory, "Test.csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
            .AddAnalyzerConfigDocument(
                DocumentId.CreateNewId(projectId),
                ".editorconfig",
                SourceText.From(editorConfig),
                filePath: Path.Combine(directory, ".editorconfig"))
            .AddDocument(documentId, "Test0.cs", SourceText.From(text), filePath: Path.Combine(directory, "Test0.cs"));
        return solution.GetDocument(documentId)!;
    }

    private static string NormalizeEndOfLine(string text, string endOfLine)
    {
        return text.Replace("\r\n", "\n").Replace("\n", endOfLine);
    }

    private static string EditorConfig(string? extra)
    {
        return "root = true\n\n[*]\n"
            + "dotnet_diagnostic.FDGA003.severity = warning\n"
            + "dotnet_diagnostic.FDGA023.severity = warning\n"
            + (extra ?? string.Empty);
    }

    private sealed class FixedDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>([]);
        }

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
        }
    }

    private sealed class Test : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
    {
        private readonly LanguageVersion _languageVersion;

        public Test(LanguageVersion languageVersion)
        {
            _languageVersion = languageVersion;
        }

        protected override ParseOptions CreateParseOptions()
        {
            return ((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(_languageVersion);
        }
    }
}
