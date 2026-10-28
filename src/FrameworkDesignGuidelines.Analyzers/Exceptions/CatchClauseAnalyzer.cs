using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Exceptions;

/// <summary>
/// FDG7047 and FDG7051 (sections 7.3.6 and 7.3.9): a catch clause of <see cref="System.StackOverflowException"/> is
/// reported (FDG7047), and so is a catch clause of <see cref="System.Threading.Tasks.TaskCanceledException"/> when the
/// same try statement does not also catch <see cref="System.OperationCanceledException"/> (FDG7051). Type tests in
/// filters or inside a broader catch are not seen.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CatchClauseAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG7047, FdgDescriptors.FDG7051);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            Compilation compilation = start.Compilation;
            INamedTypeSymbol? stackOverflow = compilation.GetTypeByMetadataName("System.StackOverflowException");
            INamedTypeSymbol? taskCanceled =
                compilation.GetTypeByMetadataName("System.Threading.Tasks.TaskCanceledException");
            INamedTypeSymbol? operationCanceled =
                compilation.GetTypeByMetadataName("System.OperationCanceledException");
            start.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeCatch(nodeContext, stackOverflow, taskCanceled, operationCanceled),
                SyntaxKind.CatchClause);
        });
    }

    private static void AnalyzeCatch(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? stackOverflow,
        INamedTypeSymbol? taskCanceled,
        INamedTypeSymbol? operationCanceled)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Declaration is not { Type: var typeSyntax }
            || ExceptionSymbols.CaughtType(catchClause, context.SemanticModel) is not { } caught)
        {
            return;
        }

        if (stackOverflow is not null && SymbolEqualityComparer.Default.Equals(caught, stackOverflow))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG7047, typeSyntax.GetLocation()));
        }
        else if (taskCanceled is not null
            && SymbolEqualityComparer.Default.Equals(caught, taskCanceled)
            && catchClause.Parent is TryStatementSyntax tryStatement
            && !tryStatement.Catches.Any(other => SymbolEqualityComparer.Default.Equals(
                ExceptionSymbols.CaughtType(other, context.SemanticModel), operationCanceled)))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG7051, typeSyntax.GetLocation()));
        }
    }
}
