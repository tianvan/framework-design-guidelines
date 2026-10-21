using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace Fdg.Generator.Sdk;

/// <summary>
/// Loads the SDK's analyzer assemblies and their private dependencies in isolation, while sharing the
/// generator's own Roslyn so that analyzer and code fix types can be used directly.
/// </summary>
internal sealed class SdkAnalyzerAssemblyLoader : AssemblyLoadContext, IAnalyzerAssemblyLoader
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.CodeAnalysis",
        "Microsoft.CodeAnalysis.CSharp",
        "Microsoft.CodeAnalysis.Workspaces",
        "Microsoft.CodeAnalysis.CSharp.Workspaces",
    };

    private readonly IReadOnlyList<string> _directories;

    public SdkAnalyzerAssemblyLoader(IReadOnlyList<string> directories)
        : base("fdg-sdk-analyzers")
    {
        _directories = directories;
    }

    public void AddDependencyLocation(string fullPath)
    {
    }

    public Assembly LoadFromPath(string fullPath)
    {
        return LoadFromAssemblyPath(fullPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null || SharedAssemblies.Contains(assemblyName.Name))
        {
            return null;
        }

        foreach (string directory in _directories)
        {
            string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
            if (File.Exists(candidate))
            {
                return LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}
