using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Types;

/// <summary>
/// FDG4059 and FDG4061: an assembly with externally visible types carries the informational attributes the book
/// lists. Each missing attribute is reported once, without a source location. Attributes the SDK generates from
/// project properties count.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AssemblyAttributesAnalyzer : DiagnosticAnalyzer
{
    private static readonly (DiagnosticDescriptor Descriptor, string Attribute)[] _required =
    [
        (FdgDescriptors.FDG4059, "AssemblyTitleAttribute"),
        (FdgDescriptors.FDG4059, "AssemblyCompanyAttribute"),
        (FdgDescriptors.FDG4059, "AssemblyProductAttribute"),
        (FdgDescriptors.FDG4059, "AssemblyDescriptionAttribute"),
        (FdgDescriptors.FDG4061, "AssemblyFileVersionAttribute"),
        (FdgDescriptors.FDG4061, "AssemblyCopyrightAttribute"),
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG4059, FdgDescriptors.FDG4061);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        IAssemblySymbol assembly = context.Compilation.Assembly;
        if (!NamingSymbols.IsExternallyVisible(assembly.GlobalNamespace))
        {
            return;
        }

        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (AttributeData attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass
                && attributeClass.ContainingNamespace?.ToDisplayString() == "System.Reflection")
            {
                present.Add(attributeClass.Name);
            }
        }

        foreach ((DiagnosticDescriptor descriptor, string attribute) in _required)
        {
            if (!present.Contains(attribute))
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, attribute));
            }
        }
    }
}
