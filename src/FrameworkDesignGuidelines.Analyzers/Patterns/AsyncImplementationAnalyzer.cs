using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Exceptions;
using FrameworkDesignGuidelines.Analyzers.Naming;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// FDG9036, FDG9037, FDG9038, FDG9043 and FDG9049 (sections 9.2.5 and 9.2.10): the bodies of asynchronous methods.
/// FDG9036 reports a null or default literal, directly, through a conversion or as a branch of a conditional
/// expression, or a conditional access, returned as <c>Task</c> or <c>Task&lt;TResult&gt;</c> from a method, local
/// function or property getter without the async keyword; FDG9037 reports a <c>Task</c> or <c>Task&lt;TResult&gt;</c>
/// constructor call returned the same way, unless it passes a <c>CancellationToken</c>: a task created with a token
/// that is already canceled starts in the Canceled state, not Created. Lambdas are not checked. FDG9038 reports a
/// return statement that is the whole branch of an if statement whose condition reads
/// <c>CancellationToken.IsCancellationRequested</c>, in an async method, local function or lambda that returns a
/// task-like type. FDG9043 reports, in the body of an externally visible async method itself (not in a lambda or local
/// function), a throw of <c>ArgumentException</c> or a type derived from it, or a call to a static method whose name
/// starts with ThrowIf declared on such a type. FDG9049 reports an await foreach over a parameter of type
/// <c>IAsyncEnumerable&lt;T&gt;</c>, directly or through ConfigureAwait, when the method, local function or lambda that
/// declares the parameter also has a <c>CancellationToken</c> parameter and WithCancellation is not called on the way.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncImplementationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG9036,
            FdgDescriptors.FDG9037,
            FdgDescriptors.FDG9038,
            FdgDescriptors.FDG9043,
            FdgDescriptors.FDG9049);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var types = new AsyncTypes(start.Compilation);
            if (types.Task is null)
            {
                return;
            }

            start.RegisterOperationAction(operation => AnalyzeReturn(operation, types), OperationKind.Return);
            start.RegisterOperationAction(operation => AnalyzeIf(operation, types), OperationKind.Conditional);
            start.RegisterOperationAction(operation => AnalyzeThrow(operation, types), OperationKind.Throw);
            start.RegisterOperationAction(operation => AnalyzeThrowHelper(operation, types), OperationKind.Invocation);
            start.RegisterOperationAction(operation => AnalyzeLoop(operation, types), OperationKind.Loop);
        });
    }

    private static void AnalyzeReturn(OperationAnalysisContext context, AsyncTypes types)
    {
        if (context.Operation is not IReturnOperation { ReturnedValue: { } value }
            || Function(context.Operation, context.ContainingSymbol) is not
            {
                IsAsync: false,
                MethodKind: MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation
                    or MethodKind.LocalFunction or MethodKind.PropertyGet,
            } function
            || !types.IsTask(function.ReturnType))
        {
            return;
        }

        string name = function.AssociatedSymbol is IPropertySymbol property ? property.Name : function.Name;
        foreach (IOperation leaf in Leaves(value))
        {
            if (IsNull(leaf) || leaf is IConditionalAccessOperation)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9036, leaf.Syntax.GetLocation(), name));
            }
            else if (leaf is IObjectCreationOperation { Type: { } created } creation
                && types.IsTask(created)
                && !creation.Arguments.Any(argument => types.IsCancellationToken(argument.Parameter?.Type)))
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9037, leaf.Syntax.GetLocation(), name));
            }
        }
    }

    private static void AnalyzeIf(OperationAnalysisContext context, AsyncTypes types)
    {
        if (context.Operation is not IConditionalOperation { IsRef: false, Condition: { } condition } conditional
            || conditional.Type is not null
            || UnwrapConversions(condition) is not IPropertyReferenceOperation
            {
                Property.Name: "IsCancellationRequested",
            } read
            || !types.IsCancellationToken(read.Property.ContainingType)
            || SingleReturn(conditional.WhenTrue) is not { } returned)
        {
            return;
        }

        IMethodSymbol? function = Owner(context.Operation, context.ContainingSymbol);
        if (function is { IsAsync: true } && types.IsTaskLike(function.ReturnType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG9038,
                returned.Syntax.GetLocation(),
                Name(function, context.ContainingSymbol)));
        }
    }

    private static void AnalyzeThrow(OperationAnalysisContext context, AsyncTypes types)
    {
        if (context.Operation is IThrowOperation { Exception: { } exception }
            && UnwrapConversions(exception).Type is { } type
            && ExceptionSymbols.DerivesFrom(type, types.ArgumentException)
            && UsageErrorOwner(context) is { } method)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG9043,
                context.Operation.Syntax.GetLocation(),
                UsageSymbols.Display(type),
                method.Name));
        }
    }

    private static void AnalyzeThrowHelper(OperationAnalysisContext context, AsyncTypes types)
    {
        if (context.Operation is IInvocationOperation { TargetMethod: { IsStatic: true } target }
            && target.Name.StartsWith("ThrowIf", StringComparison.Ordinal)
            && ExceptionSymbols.DerivesFrom(target.ContainingType, types.ArgumentException)
            && UsageErrorOwner(context) is { } method)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG9043,
                context.Operation.Syntax.GetLocation(),
                UsageSymbols.Display(target.ContainingType),
                method.Name));
        }
    }

    private static void AnalyzeLoop(OperationAnalysisContext context, AsyncTypes types)
    {
        if (context.Operation is not IForEachLoopOperation { IsAsynchronous: true, Collection: { } collection }
            || Enumerated(collection) is not IParameterReferenceOperation { Parameter: { } parameter }
            || !types.IsAsyncEnumerable(parameter.Type)
            || parameter.ContainingSymbol is not IMethodSymbol owner
            || !owner.Parameters.Any(p => types.IsCancellationToken(p.Type)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            FdgDescriptors.FDG9049,
            UnwrapConversions(collection).Syntax.GetLocation(),
            parameter.Name));
    }

    /// <summary>
    /// The value an await foreach enumerates, looking through conversions and ConfigureAwait calls, or null when
    /// WithCancellation is called on the way.
    /// </summary>
    private static IOperation? Enumerated(IOperation collection)
    {
        IOperation current = UnwrapConversions(collection);
        while (current is IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.Name == "WithCancellation")
            {
                return null;
            }

            if (invocation.TargetMethod.Name != "ConfigureAwait")
            {
                return current;
            }

            IOperation? receiver = invocation.Instance
                ?? (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);
            if (receiver is null)
            {
                return current;
            }

            current = UnwrapConversions(receiver);
        }

        return current;
    }

    /// <summary>
    /// The externally visible async method whose own body holds the operation, or null when the operation is in a
    /// lambda or local function, or the method is not async or not visible.
    /// </summary>
    private static IMethodSymbol? UsageErrorOwner(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is not IMethodSymbol { IsAsync: true, MethodKind: MethodKind.Ordinary } method
            || !NamingSymbols.IsExternallyVisible(method))
        {
            return null;
        }

        for (IOperation? current = context.Operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return null;
            }
        }

        return method;
    }

    /// <summary>
    /// The method or local function that the operation belongs to, or null inside a lambda or anonymous method.
    /// </summary>
    private static IMethodSymbol? Function(IOperation operation, ISymbol containingSymbol)
    {
        for (IOperation? current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation:
                    return null;
                case ILocalFunctionOperation localFunction:
                    return localFunction.Symbol;
            }
        }

        return containingSymbol as IMethodSymbol;
    }

    /// <summary>The method, local function or lambda that the operation belongs to.</summary>
    private static IMethodSymbol? Owner(IOperation operation, ISymbol containingSymbol)
    {
        for (IOperation? current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation anonymous:
                    return anonymous.Symbol;
                case ILocalFunctionOperation localFunction:
                    return localFunction.Symbol;
            }
        }

        return containingSymbol as IMethodSymbol;
    }

    /// <summary>The name of the function, or of the member that contains it for a lambda.</summary>
    private static string Name(IMethodSymbol function, ISymbol containingSymbol)
    {
        IMethodSymbol named = function.MethodKind == MethodKind.AnonymousFunction
            && containingSymbol is IMethodSymbol containing
                ? containing
                : function;
        return named.AssociatedSymbol is IPropertySymbol property ? property.Name : named.Name;
    }

    /// <summary>The return statement that is the whole branch, alone or as the only statement of a block.</summary>
    private static IReturnOperation? SingleReturn(IOperation branch)
    {
        return branch switch
        {
            IReturnOperation returned => returned,
            IBlockOperation { Operations: { Length: 1 } operations } => operations[0] as IReturnOperation,
            _ => null,
        };
    }

    /// <summary>
    /// The values the operation can return: conversions are looked through, and conditional branches.
    /// </summary>
    private static IEnumerable<IOperation> Leaves(IOperation operation)
    {
        return operation switch
        {
            IConversionOperation conversion => Leaves(conversion.Operand),
            IConditionalOperation { WhenFalse: { } whenFalse } conditional =>
                Leaves(conditional.WhenTrue).Concat(Leaves(whenFalse)),
            _ => [operation],
        };
    }

    private static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool IsNull(IOperation operation)
    {
        return operation is IDefaultValueOperation
            || operation is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } };
    }
}
