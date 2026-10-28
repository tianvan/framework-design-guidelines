using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Exceptions;

/// <summary>
/// FDG7064 (section 7.5.2): Try methods return bool. A method whose first word is "Try", followed by at least one
/// more word, is reported when it has an out parameter, as the value-producing Try methods of section 7.5.2.1 do, and
/// does not return bool. A Try method without an out parameter may only attempt an operation (<c>TryReset</c>) and is
/// not reported. Overrides and interface implementations take their name and return type from another member and are
/// not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TryMethodAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG7064);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Ordinary
            || method.IsImplicitlyDeclared
            || NamingSymbols.Words(method.Name) is not { Count: > 1 } words
            || words[0] != "Try"
            || !method.Parameters.Any(parameter => parameter.RefKind == RefKind.Out)
            || method.ReturnType.SpecialType == SpecialType.System_Boolean
            || NamingSymbols.IsInherited(method)
            || NamingSymbols.SourceLocation(method) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG7064, location, method.Name));
    }
}
