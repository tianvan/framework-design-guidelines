using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5118 (section 5.8.4): an externally visible method or constructor that declares its own signature does not
/// assign an element of its params array, in its body or in the lambdas and local functions of its body.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParamsArrayAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5118);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationBlockStartAction(AnalyzeBlock);
    }

    private static void AnalyzeBlock(OperationBlockStartAnalysisContext context)
    {
        if (context.OwningSymbol is not IMethodSymbol method
            || method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)
            || method.Parameters.LastOrDefault() is not { IsParams: true, Type: IArrayTypeSymbol } parameter
            || !Signatures.IsDeclared(method))
        {
            return;
        }

        var modification = new Modification();
        context.RegisterOperationAction(
            operationContext => modification.Record(Target(operationContext.Operation), parameter),
            OperationKind.SimpleAssignment,
            OperationKind.CompoundAssignment,
            OperationKind.CoalesceAssignment,
            OperationKind.Increment,
            OperationKind.Decrement);
        context.RegisterOperationBlockEndAction(endContext =>
        {
            if (modification.Found && NamingSymbols.SourceLocation(parameter) is { } location)
            {
                endContext.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG5118, location, parameter.Name, Overloads.DisplayName(method)));
            }
        });
    }

    private static IOperation? Target(IOperation operation)
    {
        return operation switch
        {
            IAssignmentOperation assignment => assignment.Target,
            IIncrementOrDecrementOperation increment => increment.Target,
            _ => null,
        };
    }

    /// <summary>Whether an element of the params array is assigned, set from concurrent operation actions.</summary>
    private sealed class Modification
    {
        private int found;

        public bool Found => Volatile.Read(ref found) == 1;

        public void Record(IOperation? target, IParameterSymbol parameter)
        {
            if (target is IArrayElementReferenceOperation
                {
                    ArrayReference: IParameterReferenceOperation { Parameter: var assigned },
                }
                && SymbolEqualityComparer.Default.Equals(assigned, parameter))
            {
                Interlocked.Exchange(ref found, 1);
            }
        }
    }
}
