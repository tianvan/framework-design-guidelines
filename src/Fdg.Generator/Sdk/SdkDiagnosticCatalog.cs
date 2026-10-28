using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Fdg.Generator.Sdk;

/// <summary>
/// Lists the C# diagnostics of the NetAnalyzers (CA) and code style (IDE) layers shipped in an SDK.
/// </summary>
/// <remarks>
/// A diagnostic has a code fix when an exported C# <see cref="CodeFixProvider"/> lists it, either in those layers or
/// in the Roslyn Features layer bundled with <c>dotnet format</c>. The code style layer compiles some fixers (such as
/// IDE1006's) without their export; <c>dotnet format</c> and the IDE use the Features copies instead.
/// </remarks>
public static class SdkDiagnosticCatalog
{
    public static IReadOnlyDictionary<string, SdkDiagnostic> Load(string sdkDirectory)
    {
        string sdk = Path.Combine(sdkDirectory, "Sdks", "Microsoft.NET.Sdk");
        string analyzers = Path.Combine(sdk, "analyzers");
        string codeStyle = Path.Combine(sdk, "codestyle", "cs");
        string format = Path.Combine(sdkDirectory, "DotnetTools", "dotnet-format");
        var paths = new List<string>
        {
            Path.Combine(analyzers, "Microsoft.CodeAnalysis.NetAnalyzers.dll"),
            Path.Combine(analyzers, "Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll"),
        };
        string[] features =
        {
            Path.Combine(format, "Microsoft.CodeAnalysis.Features.dll"),
            Path.Combine(format, "Microsoft.CodeAnalysis.CSharp.Features.dll"),
        };

        // The layout is not a contract of the SDK: say which path was expected instead of failing on the first read.
        string[] missing = new[] { analyzers, codeStyle, format }.Where(p => !Directory.Exists(p)).ToArray();
        if (missing.Length == 0)
        {
            missing = paths.Concat(features).Where(p => !File.Exists(p)).ToArray();
        }

        if (missing.Length > 0)
        {
            throw new IOException(
                $"{sdkDirectory} is not an SDK directory with the layout fdg-gen reads; expected {string.Join(", ", missing)}. " +
                "Pass the directory of an SDK that has them with --sdk <dir>.");
        }

        paths.AddRange(Directory.GetFiles(codeStyle, "*.dll").Order(StringComparer.Ordinal));

        var loader = new SdkAnalyzerAssemblyLoader(new[] { analyzers, codeStyle, format });
        var descriptors = new Dictionary<string, DiagnosticDescriptor>(StringComparer.Ordinal);
        var fixable = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (string path in paths)
        {
            var reference = new AnalyzerFileReference(path, loader);
            reference.AnalyzerLoadFailed += (_, e) => failures.Add($"{Path.GetFileName(path)}: {e.Message}");
            foreach (DiagnosticAnalyzer analyzer in reference.GetAnalyzers(LanguageNames.CSharp))
            {
                foreach (DiagnosticDescriptor descriptor in analyzer.SupportedDiagnostics)
                {
                    descriptors.TryAdd(descriptor.Id, descriptor);
                }
            }

            foreach (CodeFixProvider fixer in CreateCSharpFixers(reference.GetAssembly(), failures))
            {
                fixable.UnionWith(fixer.FixableDiagnosticIds);
            }
        }

        foreach (string path in features)
        {
            foreach (CodeFixProvider fixer in CreateCSharpFixers(loader.LoadFromPath(path), failures))
            {
                fixable.UnionWith(fixer.FixableDiagnosticIds);
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException("Could not load every SDK analyzer:\n" + string.Join("\n", failures));
        }

        return descriptors.Values
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToDictionary(
                d => d.Id,
                d => new SdkDiagnostic(
                    d.Id,
                    d.Title.ToString(CultureInfo.InvariantCulture),
                    d.DefaultSeverity,
                    fixable.Contains(d.Id)),
                StringComparer.Ordinal);
    }

    /// <summary>Instantiates every C# <see cref="CodeFixProvider"/> exported by <paramref name="assembly"/>.</summary>
    private static IEnumerable<CodeFixProvider> CreateCSharpFixers(Assembly assembly, List<string> failures)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            failures.AddRange(exception.LoaderExceptions
                .OfType<Exception>()
                .Select(e => $"{assembly.GetName().Name}: {e.Message}")
                .Distinct());
            types = exception.Types.OfType<Type>().ToArray();
        }

        foreach (Type type in types)
        {
            ExportCodeFixProviderAttribute? export = type.GetCustomAttribute<ExportCodeFixProviderAttribute>();
            if (type.IsAbstract || export is null || !export.Languages.Contains(LanguageNames.CSharp))
            {
                continue;
            }

            CodeFixProvider? fixer = null;
            try
            {
                fixer = (CodeFixProvider?)Activator.CreateInstance(type, nonPublic: true);
            }
            catch (Exception exception) when (exception is MissingMethodException or TargetInvocationException)
            {
                failures.Add($"{type.FullName}: {exception.Message}");
            }

            if (fixer is not null)
            {
                yield return fixer;
            }
        }
    }
}
