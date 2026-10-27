using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// FDG9138 and FDG9142 (section 9.12), what a method does with a span parameter. FDG9138 reports a
/// <c>Span&lt;T&gt;</c> parameter passed by value to an externally visible method or constructor with a body that
/// only reads it (see <see cref="IsRead"/>); virtual, abstract and extern methods, overrides and interface
/// implementations keep their parameter types as a contract. FDG9142 reports an externally visible method that
/// returns <c>ReadOnlySpan&lt;T&gt;</c> or <c>Span&lt;T&gt;</c> when every return statement returns the same
/// parameter of that type, whole or sliced with Slice or a range indexer, and at least one slices it; nothing is
/// reported when the compilation has no <c>System.Range</c> to return instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SpanParameterAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> _readMethods = ImmutableHashSet.Create(
        "CopyTo",
        "TryCopyTo",
        "ToArray",
        "ToString");

    private static readonly ImmutableHashSet<string> _readExtensions = ImmutableHashSet.Create(
        "Contains",
        "IndexOf",
        "LastIndexOf",
        "IndexOfAny",
        "LastIndexOfAny",
        "SequenceEqual",
        "SequenceCompareTo",
        "StartsWith",
        "EndsWith",
        "BinarySearch");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG9138, FdgDescriptors.FDG9142);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var types = new BufferTypes(start.Compilation);
            if (!types.HasSpans)
            {
                return;
            }

            start.RegisterOperationBlockAction(blockContext => Analyze(blockContext, types));
        });
    }

    private static void Analyze(OperationBlockAnalysisContext context, BufferTypes types)
    {
        if (context.OwningSymbol is not IMethodSymbol
            {
                MethodKind: MethodKind.Ordinary or MethodKind.Constructor,
                IsImplicitlyDeclared: false,
            } method
            || !NamingSymbols.IsExternallyVisible(method)
            || NamingSymbols.IsInherited(method))
        {
            return;
        }

        string owner = UsageSymbols.Display(method.ContainingType) + "."
            + (method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name);
        IOperation[] operations = context.OperationBlocks.SelectMany(block => block.DescendantsAndSelf()).ToArray();
        if (!method.IsVirtual && !method.IsAbstract && !method.IsExtern)
        {
            AnalyzeReadOnly(context, method, operations, owner, types);
        }

        if (method.MethodKind == MethodKind.Ordinary && types.Range is not null)
        {
            AnalyzeSlices(context, method, operations, owner, types);
        }
    }

    private static void AnalyzeReadOnly(
        OperationBlockAnalysisContext context,
        IMethodSymbol method,
        IOperation[] operations,
        string owner,
        BufferTypes types)
    {
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None
                || !types.IsSpan(parameter.Type)
                || NamingSymbols.SourceLocation(parameter) is not { } location)
            {
                continue;
            }

            bool isReadOnly = operations
                .OfType<IParameterReferenceOperation>()
                .Where(reference => SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter))
                .All(reference => IsRead(reference, method.RefKind == RefKind.Ref, types));
            if (isReadOnly)
            {
                ITypeSymbol element = ((INamedTypeSymbol)parameter.Type).TypeArguments[0];
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG9138, location, parameter.Name, owner, UsageSymbols.Display(element)));
            }
        }
    }

    /// <summary>
    /// True when a use of a span parameter only reads it: its Length or IsEmpty, an element read through the
    /// indexer, a conversion to <c>ReadOnlySpan&lt;T&gt;</c>, CopyTo, TryCopyTo, ToArray or ToString called on it, a
    /// read-only MemoryExtensions method called on it, or a foreach without ref. Anything else may write.
    /// </summary>
    private static bool IsRead(IOperation reference, bool returnsByRef, BufferTypes types)
    {
        IOperation? parent = reference.Parent;
        while (parent is IConversionOperation { Conversion.IsIdentity: true } identity)
        {
            reference = identity;
            parent = identity.Parent;
        }

        switch (parent)
        {
            case IPropertyReferenceOperation { Property.Name: "Length" or "IsEmpty" } property
                when property.Instance == reference:
                return true;
            case IPropertyReferenceOperation { Property.IsIndexer: true } indexer when indexer.Instance == reference:
                return IsElementRead(indexer, returnsByRef);
            case IConversionOperation conversion:
                return types.IsReadOnlySpan(conversion.Type);
            case IInvocationOperation invocation when invocation.Instance == reference:
                return _readMethods.Contains(invocation.TargetMethod.Name);
            case IArgumentOperation { Parameter.RefKind: RefKind.None, Parent: IInvocationOperation invocation }:
                return SymbolEqualityComparer.Default.Equals(
                        invocation.TargetMethod.ContainingType, types.MemoryExtensions)
                    && _readExtensions.Contains(invocation.TargetMethod.Name);
            case IForEachLoopOperation loop when loop.Collection == reference:
                return !loop.Locals.Any(local => local.RefKind == RefKind.Ref);
            default:
                return false;
        }
    }

    /// <summary>
    /// True when an element of the span is only read: it is not assigned, incremented, passed or returned by
    /// reference, bound to a ref local and does not have its address taken, and no member is accessed on an element
    /// that is not of a reference type, since that member could change the element.
    /// </summary>
    private static bool IsElementRead(IPropertyReferenceOperation element, bool returnsByRef)
    {
        switch (element.Parent)
        {
            case IAssignmentOperation assignment when assignment.Target == element:
            case ISimpleAssignmentOperation { IsRef: true }:
            case IIncrementOrDecrementOperation:
            case IAddressOfOperation:
            case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out }:
            case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.Ref } }:
                return false;
            case IReturnOperation:
                return !returnsByRef;
            case IMemberReferenceOperation member when member.Instance == element:
                return element.Type is { IsReferenceType: true };
            case IInvocationOperation invocation when invocation.Instance == element:
                return element.Type is { IsReferenceType: true };
            default:
                return true;
        }
    }

    private static void AnalyzeSlices(
        OperationBlockAnalysisContext context,
        IMethodSymbol method,
        IOperation[] operations,
        string owner,
        BufferTypes types)
    {
        if (!(types.IsSpan(method.ReturnType) || types.IsReadOnlySpan(method.ReturnType))
            || NamingSymbols.SourceLocation(method) is not { } location)
        {
            return;
        }

        IReturnOperation[] returns = operations
            .OfType<IReturnOperation>()
            .Where(operation => !IsInNestedFunction(operation))
            .ToArray();
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None
                || !SymbolEqualityComparer.Default.Equals(parameter.Type, method.ReturnType))
            {
                continue;
            }

            bool sliced = false;
            bool all = returns.Length > 0;
            foreach (IReturnOperation operation in returns)
            {
                switch (operation.ReturnedValue)
                {
                    case IParameterReferenceOperation whole when IsParameter(whole, parameter):
                        break;
                    case IInvocationOperation { TargetMethod.Name: "Slice", Instance: { } instance }
                        when IsParameter(instance, parameter):
                        sliced = true;
                        break;
                    case IImplicitIndexerReferenceOperation range
                        when IsParameter(range.Instance, parameter)
                            && UsageSymbols.Is(range.Argument.Type, types.Range):
                        sliced = true;
                        break;
                    default:
                        all = false;
                        break;
                }
            }

            if (all && sliced)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9142, location, parameter.Name, owner));
                return;
            }
        }
    }

    private static bool IsParameter(IOperation operation, IParameterSymbol parameter)
    {
        return operation is IParameterReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);
    }

    private static bool IsInNestedFunction(IOperation operation)
    {
        for (IOperation? current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is ILocalFunctionOperation or IAnonymousFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }
}
