using System.Collections.Concurrent;
using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// FDG9020, FDG9022, FDG9023, FDG9027, FDG9028, FDG9029, FDG9033 and FDG9045 (sections 9.2.2, 9.2.4 and 9.2.10): the
/// signatures of externally visible ordinary methods that return a task-like type (see
/// <see cref="AsyncTypes.IsTaskLike"/>) or <c>IAsyncEnumerable&lt;T&gt;</c>. Overrides and interface implementations
/// take their signature from another member and are not reported, and neither is a static method named Main, the entry
/// point of a program. FDG9020 (task-like) and FDG9045 (<c>IAsyncEnumerable&lt;T&gt;</c>) report a name that does not
/// end with "Async" or "AsyncCore". FDG9027, FDG9028 and FDG9029 apply to both; the others apply to task-like returns
/// only. FDG9020 and FDG9022 leave out a static method without the async keyword whose every return value, outside
/// lambdas and local functions, is a completed task (see <see cref="IsCompleted"/>): like Task.FromResult, it starts no
/// operation. FDG9022 reports a method without a <c>CancellationToken</c> parameter, unless it is named
/// DisposeAsyncCore or another method of the same name in the type takes one, or, at the first token, one without a
/// token named cancellationToken. FDG9023 reports a token without a default value, unless another method of the same
/// name is in the type, a required parameter follows the token, or the method is protected and its name ends with
/// "Core". FDG9027 reports ref and out parameters. FDG9028 reports in and ref readonly parameters of virtual, abstract
/// and interface methods, and FDG9029 those of other methods unless the parameter type is a readonly struct. FDG9033
/// reports a method whose name ends with "Async" when every method of the type named without the suffix that is static
/// or not as it is, does not return a task-like type, and shares at least two parameter names with it, other than
/// tokens, has those names in another relative order.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncSignatureAnalyzer : DiagnosticAnalyzer
{
    private const string Suffix = "Async";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG9020,
            FdgDescriptors.FDG9022,
            FdgDescriptors.FDG9023,
            FdgDescriptors.FDG9027,
            FdgDescriptors.FDG9028,
            FdgDescriptors.FDG9029,
            FdgDescriptors.FDG9033,
            FdgDescriptors.FDG9045);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var types = new AsyncTypes(start.Compilation);
            start.RegisterSymbolStartAction(symbolStart => AnalyzeMethodStart(symbolStart, types), SymbolKind.Method);
        });
    }

    /// <summary>
    /// Records, for a static method without the async keyword that returns a task-like type, whether each value it
    /// returns is a completed task, and checks every method at its end.
    /// </summary>
    private static void AnalyzeMethodStart(SymbolStartAnalysisContext context, AsyncTypes types)
    {
        if (context.Symbol is not IMethodSymbol { IsStatic: true, IsAsync: false } method
            || !types.IsTaskLike(method.ReturnType))
        {
            context.RegisterSymbolEndAction(end => AnalyzeMethod(end, types, isCompletedFactory: false));
            return;
        }

        var completed = new ConcurrentQueue<bool>();
        context.RegisterOperationBlockAction(block =>
        {
            foreach (IOperation root in block.OperationBlocks)
            {
                foreach (IReturnOperation returned in Returns(root))
                {
                    completed.Enqueue(returned.ReturnedValue is { } value && IsCompleted(value, types));
                }
            }
        });
        context.RegisterSymbolEndAction(end =>
            AnalyzeMethod(end, types, isCompletedFactory: !completed.IsEmpty && completed.All(value => value)));
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, AsyncTypes types, bool isCompletedFactory)
    {
        if (context.Symbol is not IMethodSymbol { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false } method
            || method is { IsStatic: true, Name: "Main" }
            || NamingSymbols.IsInherited(method)
            || !NamingSymbols.IsExternallyVisible(method)
            || NamingSymbols.SourceLocation(method) is not { } location)
        {
            return;
        }

        bool hasSuffix = method.Name.EndsWith(Suffix, StringComparison.Ordinal)
            || method.Name.EndsWith(Suffix + "Core", StringComparison.Ordinal);
        if (types.IsAsyncEnumerable(method.ReturnType))
        {
            if (!hasSuffix)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9045, location, method.Name));
            }

            CheckModifiers(context, method);
            return;
        }

        if (!types.IsTaskLike(method.ReturnType))
        {
            return;
        }

        if (!hasSuffix && !isCompletedFactory)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9020, location, method.Name));
        }

        CheckCancellationToken(context, types, method, location, isCompletedFactory);
        CheckModifiers(context, method);
        CheckOrder(context, types, method, location);
    }

    private static void CheckCancellationToken(
        SymbolAnalysisContext context,
        AsyncTypes types,
        IMethodSymbol method,
        Location location,
        bool isCompletedFactory)
    {
        IParameterSymbol[] tokens = method.Parameters.Where(p => types.IsCancellationToken(p.Type)).ToArray();
        IMethodSymbol[] sameName = method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>()
            .Where(other => !SymbolEqualityComparer.Default.Equals(other, method))
            .ToArray();
        if (tokens.Length == 0)
        {
            if (!isCompletedFactory
                && method.Name != "DisposeAsyncCore"
                && !sameName.Any(other => other.Parameters.Any(p => types.IsCancellationToken(p.Type))))
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9022, location, method.Name));
            }

            return;
        }

        if (!tokens.Any(token => token.Name == "cancellationToken")
            && NamingSymbols.SourceLocation(tokens[0]) is { } firstLocation)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9022, firstLocation, method.Name));
        }

        bool isTemplateCore = method.Name.EndsWith("Core", StringComparison.Ordinal)
            && method.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal;
        if (sameName.Length > 0 || isTemplateCore)
        {
            return;
        }

        foreach (IParameterSymbol token in tokens)
        {
            bool isRequiredAfter = method.Parameters.Skip(token.Ordinal + 1)
                .Any(p => !p.IsOptional && !p.IsParams);
            if (!token.HasExplicitDefaultValue
                && !isRequiredAfter
                && NamingSymbols.SourceLocation(token) is { } tokenLocation)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(FdgDescriptors.FDG9023, tokenLocation, token.Name, method.Name));
            }
        }
    }

    private static void CheckModifiers(SymbolAnalysisContext context, IMethodSymbol method)
    {
        bool isVirtual = method.IsVirtual || method.IsAbstract || method.ContainingType.TypeKind == TypeKind.Interface;
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (NamingSymbols.SourceLocation(parameter) is not { } location)
            {
                continue;
            }

            switch (parameter.RefKind)
            {
                case RefKind.Ref or RefKind.Out:
                    context.ReportDiagnostic(Diagnostic.Create(
                        FdgDescriptors.FDG9027,
                        location,
                        parameter.Name,
                        parameter.RefKind == RefKind.Ref ? "ref" : "out",
                        method.Name));
                    break;
                case RefKind.In or RefKind.RefReadOnlyParameter when isVirtual:
                    context.ReportDiagnostic(
                        Diagnostic.Create(FdgDescriptors.FDG9028, location, parameter.Name, method.Name));
                    break;
                case RefKind.In or RefKind.RefReadOnlyParameter when parameter.Type is not INamedTypeSymbol
                {
                    IsValueType: true, IsReadOnly: true,
                }:
                    context.ReportDiagnostic(
                        Diagnostic.Create(FdgDescriptors.FDG9029, location, parameter.Name, method.Name));
                    break;
            }
        }
    }

    private static void CheckOrder(
        SymbolAnalysisContext context, AsyncTypes types, IMethodSymbol method, Location location)
    {
        if (!method.Name.EndsWith(Suffix, StringComparison.Ordinal) || method.Name.Length == Suffix.Length)
        {
            return;
        }

        string syncName = method.Name.Substring(0, method.Name.Length - Suffix.Length);
        string[] asyncOrder = Names(method, types);
        bool isCompared = false;
        foreach (IMethodSymbol sync in method.ContainingType.GetMembers(syncName).OfType<IMethodSymbol>())
        {
            if (sync.MethodKind != MethodKind.Ordinary
                || sync.IsStatic != method.IsStatic
                || types.IsTaskLike(sync.ReturnType))
            {
                continue;
            }

            string[] syncOrder = Names(sync, types);
            string[] shared = asyncOrder.Intersect(syncOrder, StringComparer.Ordinal).ToArray();
            if (shared.Length < 2)
            {
                continue;
            }

            if (asyncOrder.Where(shared.Contains).SequenceEqual(syncOrder.Where(shared.Contains)))
            {
                return;
            }

            isCompared = true;
        }

        if (isCompared)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9033, location, method.Name, syncName));
        }
    }

    /// <summary>The return operations of a method body, outside lambdas and local functions.</summary>
    private static IEnumerable<IReturnOperation> Returns(IOperation operation)
    {
        foreach (IOperation child in operation.ChildOperations)
        {
            if (child is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                continue;
            }

            if (child is IReturnOperation returned)
            {
                yield return returned;
            }

            foreach (IReturnOperation nested in Returns(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// True for a completed task, through conversions and the branches of a conditional expression: a call to
    /// FromResult, FromException or FromCanceled, or the CompletedTask property, of <c>Task</c> or <c>ValueTask</c>;
    /// or a <c>ValueTask</c> or <c>ValueTask&lt;TResult&gt;</c> that is default, or created without arguments or from
    /// a result.
    /// </summary>
    private static bool IsCompleted(IOperation value, AsyncTypes types)
    {
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        return value switch
        {
            IConditionalOperation { WhenFalse: { } whenFalse } conditional =>
                IsCompleted(conditional.WhenTrue, types) && IsCompleted(whenFalse, types),
            IInvocationOperation { TargetMethod: { IsStatic: true } target } =>
                IsTaskType(target.ContainingType, types)
                && target.Name is "FromResult" or "FromException" or "FromCanceled",
            IPropertyReferenceOperation { Property: { IsStatic: true, Name: "CompletedTask" } property } =>
                IsTaskType(property.ContainingType, types),
            IDefaultValueOperation { Type: { } type } => IsValueTask(type, types),
            IObjectCreationOperation { Type: { } type, Constructor: { } constructor } =>
                IsValueTask(type, types)
                && (constructor.Parameters.Length == 0
                    || (constructor.Parameters.Length == 1
                        && constructor.OriginalDefinition.Parameters[0].Type is ITypeParameterSymbol)),
            _ => false,
        };
    }

    private static bool IsTaskType(INamedTypeSymbol type, AsyncTypes types)
    {
        return SymbolEqualityComparer.Default.Equals(type, types.Task)
            || SymbolEqualityComparer.Default.Equals(type, types.ValueTask);
    }

    private static bool IsValueTask(ITypeSymbol type, AsyncTypes types)
    {
        return SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, types.ValueTask)
            || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, types.ValueTaskOfT);
    }

    /// <summary>The names of the parameters in order, without the <c>CancellationToken</c> parameters.</summary>
    private static string[] Names(IMethodSymbol method, AsyncTypes types)
    {
        return method.Parameters.Where(p => !types.IsCancellationToken(p.Type)).Select(p => p.Name).ToArray();
    }
}
