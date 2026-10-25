using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Exceptions;

/// <summary>
/// FDG7007 and FDG7008 (section 7.1): public members neither choose by an option whether they throw nor return
/// exceptions. FDG7007 reports a bool parameter whose first word is "throw" (<c>throwOnError</c>) of an externally
/// visible method, constructor or indexer. FDG7008 reports an externally visible method whose return type, or the
/// type of one of its out parameters, is <see cref="System.Exception"/> or derives from it. Overrides and interface
/// implementations take their signature from another member and are not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExceptionSignatureAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG7007, FdgDescriptors.FDG7008);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? exception = start.Compilation.GetTypeByMetadataName("System.Exception");
            start.RegisterSymbolAction(
                symbolContext => AnalyzeMember(symbolContext, exception),
                SymbolKind.Method,
                SymbolKind.Property);
        });
    }

    private static void AnalyzeMember(SymbolAnalysisContext context, INamedTypeSymbol? exception)
    {
        ISymbol member = context.Symbol;
        ImmutableArray<IParameterSymbol> parameters;
        string name;
        switch (member)
        {
            case IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.Constructor } method:
                parameters = method.Parameters;
                name = method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name;
                break;
            case IPropertySymbol { IsIndexer: true } indexer:
                parameters = indexer.Parameters;
                name = "this";
                break;
            default:
                return;
        }

        if (member.IsImplicitlyDeclared
            || NamingSymbols.IsInherited(member)
            || !NamingSymbols.IsExternallyVisible(member))
        {
            return;
        }

        foreach (IParameterSymbol parameter in parameters)
        {
            if (parameter.Type.SpecialType == SpecialType.System_Boolean
                && NamingSymbols.Words(parameter.Name) is { Count: > 0 } words
                && string.Equals(words[0], "throw", StringComparison.OrdinalIgnoreCase)
                && NamingSymbols.SourceLocation(parameter) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG7007, location, parameter.Name, name));
            }
        }

        if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } ordinary
            && (ExceptionSymbols.DerivesFrom(ordinary.ReturnType, exception)
                || ordinary.Parameters.Any(parameter =>
                    parameter.RefKind == RefKind.Out && ExceptionSymbols.DerivesFrom(parameter.Type, exception)))
            && NamingSymbols.SourceLocation(ordinary) is { } methodLocation)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG7008, methodLocation, ordinary.Name));
        }
    }
}
