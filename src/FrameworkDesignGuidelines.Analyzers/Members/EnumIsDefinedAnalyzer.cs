using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5111 (section 5.8.2): <c>Enum.IsDefined</c> is not used for the range check of an argument. A call is reported
/// when its value argument is a parameter of the calling member and its result, negated or combined with
/// <c>&amp;&amp;</c> or <c>||</c> or not, is the condition of an if statement or a conditional expression with a
/// branch that throws. Other calls ask whether a value is one of the defined ones, which the text allows.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnumIsDefinedAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5111);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod is not { Name: "IsDefined", ContainingType.SpecialType: SpecialType.System_Enum }
            || invocation.Arguments.FirstOrDefault(argument =>
                    argument.Parameter?.Ordinal == invocation.TargetMethod.Parameters.Length - 1)?.Value
                is not { } value
            || Unwrap(value) is not IParameterReferenceOperation { Parameter: var parameter }
            || !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, context.ContainingSymbol)
            || !GuardsThrow(invocation))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(FdgDescriptors.FDG5111, invocation.Syntax.GetLocation(), parameter.Name));
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    /// <summary>
    /// True when <paramref name="invocation"/>, through negations and conditional logical operators, is the condition
    /// of an if statement or a conditional expression with a branch that throws.
    /// </summary>
    private static bool GuardsThrow(IOperation invocation)
    {
        IOperation current = invocation;
        while (current.Parent is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not }
            or IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr })
        {
            current = current.Parent;
        }

        return current.Parent is IConditionalOperation conditional
            && conditional.Condition == current
            && (Throws(conditional.WhenTrue) || Throws(conditional.WhenFalse));
    }

    private static bool Throws(IOperation? branch)
    {
        return branch switch
        {
            IThrowOperation => true,
            IConversionOperation { Operand: var operand } => Throws(operand),
            IExpressionStatementOperation { Operation: IThrowOperation } => true,
            IBlockOperation block => block.Operations.Any(
                statement => statement is not IBlockOperation && Throws(statement)),
            _ => false,
        };
    }
}
