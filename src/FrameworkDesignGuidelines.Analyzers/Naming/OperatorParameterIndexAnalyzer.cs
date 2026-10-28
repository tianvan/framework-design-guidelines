using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// FDG3086: a parameter of an externally visible user-defined operator or conversion operator is not named with a
/// numeric index, a name that ends with a digit such as the book's <c>d1</c> and <c>d2</c>.
/// </summary>
/// <remarks>
/// A name that is the name of its parameter's type, such as <c>vector3</c> for <c>Vector3</c>, ends with a digit of
/// the type name rather than an index and is not reported. Abbreviations, the rule's other subject, cannot be told
/// from words and are not checked.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OperatorParameterIndexAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG3086);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind is not (MethodKind.UserDefinedOperator or MethodKind.Conversion))
        {
            return;
        }

        foreach ((ISymbol symbol, bool _) in NamingSymbols.DeclaredNames(method))
        {
            if (symbol is IParameterSymbol parameter
                && parameter.Name.Length > 0
                && char.IsDigit(parameter.Name[parameter.Name.Length - 1])
                && !string.Equals(parameter.Name, parameter.Type.Name, StringComparison.OrdinalIgnoreCase)
                && NamingSymbols.SourceLocation(parameter) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG3086, location, parameter.Name));
            }
        }
    }
}
