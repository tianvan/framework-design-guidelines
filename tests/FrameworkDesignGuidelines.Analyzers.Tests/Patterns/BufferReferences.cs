using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

/// <summary>
/// The reference assembly sets for the buffer analyzers of section 9.12: .NET 10, where the span types are part of
/// the framework, and .NET Standard 2.0 with the System.Memory package, where they come from a package. .NET
/// Framework 4.7.2 without the package has no span types, so the default sets of the verifier do not apply.
/// </summary>
internal static class BufferReferences
{
    private static readonly LanguageVersion[] _languageVersions = [LanguageVersion.CSharp7_3, LanguageVersion.Latest];

    public static ReferenceAssemblies Net100 { get; } = new(
        "net10.0",
        new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
        Path.Combine("ref", "net10.0"));

    public static ReferenceAssemblies NetStandard20WithMemory { get; } =
        ReferenceAssemblies.NetStandard.NetStandard20.AddPackages(
            ImmutableArray.Create(new PackageIdentity("System.Memory", "4.5.5")));

    /// <summary>Verifies both reference sets, each under C# 7.3 and the latest language version.</summary>
    public static async Task VerifyAsync<TAnalyzer>(string source, params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        foreach (ReferenceAssemblies referenceAssemblies in new[] { Net100, NetStandard20WithMemory })
        {
            foreach (LanguageVersion languageVersion in _languageVersions)
            {
                await AnalyzerVerifier<TAnalyzer>.VerifyWithAsync(
                    source, referenceAssemblies, languageVersion, expected);
            }
        }
    }

    /// <summary>
    /// Verifies .NET 10 only, under C# 7.3 and the latest language version, for types that the System.Memory
    /// package lacks (System.Range, IAsyncEnumerable).
    /// </summary>
    public static async Task VerifyNet100Async<TAnalyzer>(string source, params DiagnosticResult[] expected)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        foreach (LanguageVersion languageVersion in _languageVersions)
        {
            await AnalyzerVerifier<TAnalyzer>.VerifyWithAsync(source, Net100, languageVersion, expected);
        }
    }
}
