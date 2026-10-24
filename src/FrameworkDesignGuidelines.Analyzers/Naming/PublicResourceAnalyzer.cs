using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// FDG3087: a class that a resource editor generated, marked
/// <c>[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", ...)]</c>, is not externally visible.
/// </summary>
/// <remarks>
/// The generated class sits in a <c>.Designer.cs</c> file, which Roslyn treats as generated code, so this analyzer
/// analyzes and reports generated code; the attribute limits it to resource classes.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublicResourceAnalyzer : DiagnosticAnalyzer
{
    private const string ResourceBuilder = "System.Resources.Tools.StronglyTypedResourceBuilder";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG3087);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || !NamingSymbols.IsExternallyVisible(type)
            || !type.GetAttributes().Any(IsResourceBuilder)
            || NamingSymbols.SourceLocation(type) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG3087, location, type.Name));
    }

    private static bool IsResourceBuilder(AttributeData attribute)
    {
        return attribute.AttributeClass is { Name: "GeneratedCodeAttribute" } attributeClass
            && attributeClass.ContainingNamespace?.ToDisplayString() == "System.CodeDom.Compiler"
            && attribute.ConstructorArguments.Length > 0
            && attribute.ConstructorArguments[0].Value is ResourceBuilder;
    }
}
