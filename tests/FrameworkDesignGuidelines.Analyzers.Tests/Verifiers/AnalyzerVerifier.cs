using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Verifiers;

/// <summary>
/// Runs every analyzer test that has no code fix against .NET Framework 4.7.2 and .NET 8 reference assemblies, each
/// under C# 7.3 and the latest language version, with the rules enabled through an .editorconfig the way a profile
/// globalconfig would enable them.
/// </summary>
internal static class AnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    private static readonly ReferenceAssemblies[] _referenceAssemblies =
    [
        ReferenceAssemblies.NetFramework.Net472.Default,
        ReferenceAssemblies.Net.Net80,
    ];

    private static readonly LanguageVersion[] _languageVersions = [LanguageVersion.CSharp7_3, LanguageVersion.Latest];

    /// <summary>
    /// Verifies the diagnostics marked in <paramref name="source"/>, for example <c>{|FDG3013:Naïve|}</c>.
    /// <paramref name="extraSources"/> are added as further files, by path.
    /// </summary>
    public static async Task VerifyAsync(string source, params (string Path, string Text)[] extraSources)
    {
        foreach (ReferenceAssemblies referenceAssemblies in _referenceAssemblies)
        {
            foreach (LanguageVersion languageVersion in _languageVersions)
            {
                await VerifyAsync(source, referenceAssemblies, languageVersion, extraSources);
            }
        }
    }

    /// <summary>
    /// Verifies <paramref name="source"/> with <paramref name="expected"/> on top of the marked diagnostics, for
    /// diagnostics without a source location (assembly-level checks).
    /// </summary>
    public static async Task VerifyWithAsync(string source, params DiagnosticResult[] expected)
    {
        foreach (ReferenceAssemblies referenceAssemblies in _referenceAssemblies)
        {
            foreach (LanguageVersion languageVersion in _languageVersions)
            {
                await VerifyAsync(source, referenceAssemblies, languageVersion, expected, []);
            }
        }
    }

    /// <summary>
    /// Verifies <paramref name="source"/> in a project that references a project built from
    /// <paramref name="referencedSource"/>, for members declared in a referenced assembly. Diagnostics in the
    /// referenced project are marked in <paramref name="referencedSource"/>.
    /// </summary>
    public static async Task VerifyWithReferenceAsync(string source, string referencedSource)
    {
        foreach (ReferenceAssemblies referenceAssemblies in _referenceAssemblies)
        {
            foreach (LanguageVersion languageVersion in _languageVersions)
            {
                await VerifyAsync(source, referenceAssemblies, languageVersion, [], [], referencedSource);
            }
        }
    }

    /// <summary>
    /// Verifies .NET 8 under the latest language version only, for features that C# 7.3 or .NET Framework lack
    /// (records, primary constructors, static abstract interface members).
    /// </summary>
    public static Task VerifyLatestAsync(string source, params (string Path, string Text)[] extraSources)
    {
        return VerifyAsync(source, ReferenceAssemblies.Net.Net80, LanguageVersion.Latest, extraSources);
    }

    /// <summary>Verifies one reference assembly set and language version only.</summary>
    public static Task VerifyAsync(
        string source,
        ReferenceAssemblies referenceAssemblies,
        LanguageVersion languageVersion,
        params (string Path, string Text)[] extraSources)
    {
        return VerifyAsync(source, referenceAssemblies, languageVersion, [], extraSources);
    }

    private static Task VerifyAsync(
        string source,
        ReferenceAssemblies referenceAssemblies,
        LanguageVersion languageVersion,
        DiagnosticResult[] expected,
        (string Path, string Text)[] extraSources,
        string? referencedSource = null)
    {
        var test = new Test(languageVersion)
        {
            TestCode = source.Replace("\r\n", "\n"),
            ReferenceAssemblies = referenceAssemblies,
        };
        foreach ((string path, string text) in extraSources)
        {
            test.TestState.Sources.Add((path, text.Replace("\r\n", "\n")));
        }

        if (referencedSource is not null)
        {
            ProjectState referenced = test.TestState.AdditionalProjects["Referenced"];
            referenced.Sources.Add(("/Referenced/Referenced.cs", referencedSource.Replace("\r\n", "\n")));
            referenced.AnalyzerConfigFiles.Add(("/Referenced/.editorconfig", EditorConfig()));
            test.TestState.AdditionalProjectReferences.Add("Referenced");
        }

        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", EditorConfig()));
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    private static string EditorConfig()
    {
        return "root = true\n\n[*]\n"
            + string.Concat(new TAnalyzer().SupportedDiagnostics.Select(
                descriptor => $"dotnet_diagnostic.{descriptor.Id}.severity = warning\n"));
    }

    private sealed class Test(LanguageVersion languageVersion)
        : CSharpCodeFixTest<TAnalyzer, EmptyCodeFixProvider, DefaultVerifier>
    {
        protected override ParseOptions CreateParseOptions()
        {
            return ((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(languageVersion);
        }

        protected override CompilationOptions CreateCompilationOptions()
        {
            // Unsafe code is allowed so that tests can declare pointer parameters.
            return ((CSharpCompilationOptions)base.CreateCompilationOptions()).WithAllowUnsafe(true);
        }
    }
}
