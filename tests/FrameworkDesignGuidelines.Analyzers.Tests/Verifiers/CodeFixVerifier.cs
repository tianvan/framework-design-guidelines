using Microsoft.CodeAnalysis;
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
internal static class CodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
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
        int? fixAllIterations = null)
    {
        // Raw string literals take the line endings of this file, which depend on the checkout (core.autocrlf).
        source = NormalizeEndOfLine(source, endOfLine);
        fixedSource = NormalizeEndOfLine(fixedSource, endOfLine);
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
                }

                test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", EditorConfig(extraEditorConfig)));
                await test.RunAsync();
            }
        }

        if (source != fixedSource)
        {
            await AssertFormatterLeavesUnchangedAsync(fixedSource);
        }
    }

    public static Task VerifyNoDiagnosticsAsync(string source, string? extraEditorConfig = null)
    {
        return VerifyAsync(source, source, extraEditorConfig);
    }

    /// <summary>
    /// The fixed text must be stable under <c>dotnet format whitespace</c> (IDE0055 with Roslyn defaults).
    /// </summary>
    public static async Task AssertFormatterLeavesUnchangedAsync(string text)
    {
        text = NormalizeEndOfLine(text, "\n");
        using var workspace = new AdhocWorkspace();
        Document document = workspace.CurrentSolution
            .AddProject("Formatting", "Formatting", LanguageNames.CSharp)
            .AddDocument("Test0.cs", SourceText.From(text));
        Document formatted = await Formatter.FormatAsync(document);
        string formattedText = (await formatted.GetTextAsync()).ToString();
        Assert.Equal(text, formattedText);
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
