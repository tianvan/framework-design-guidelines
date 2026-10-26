using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// FDG9067, FDG9073, FDG9074, FDG9081 and FDG9082 (section 9.4), the Dispose Pattern. FDG9067 reports an ordinary
/// method named Dispose, of any accessibility, static and extension methods included, unless it has no parameters or
/// a single bool parameter passed by value and no type parameters, or is extern (a P/Invoke declaration); FDG9081 reports a method named DisposeAsync with
/// parameters or type parameters. Overrides and interface implementations, which take their signature from another
/// member, and local functions are not reported. FDG9073 reports a finalizer declared in an externally visible class,
/// and FDG9074 one declared in a class, of any accessibility, that does not implement <c>IDisposable</c>. FDG9082
/// reports the implementation of <c>IAsyncDisposable.DisposeAsync</c>, explicit or not, declared in an unsealed class
/// of any accessibility, whose body is not exactly an await of DisposeAsyncCore() on the same instance (with or
/// without ConfigureAwait), a call to Dispose(false) and a call to GC.SuppressFinalize(this), in that order; the
/// Dispose(false) call is expected only when the class or a base class declares an instance Dispose(bool). Overrides
/// and abstract methods are not reported, and FDG9082 is skipped when <c>IAsyncDisposable</c> is not available.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisposePatternAnalyzer : DiagnosticAnalyzer
{
    private const string AllSteps =
        "awaiting DisposeAsyncCore(), calling Dispose(false) and calling GC.SuppressFinalize(this)";

    private const string AsyncSteps = "awaiting DisposeAsyncCore() and calling GC.SuppressFinalize(this)";

    /// <summary>Names a method with its containing type and parameter types, <c>Holder.Dispose(int)</c>.</summary>
    private static readonly SymbolDisplayFormat _methodFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithMemberOptions(
            SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType)
        .WithParameterOptions(
            SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        FdgDescriptors.FDG9067,
        FdgDescriptors.FDG9073,
        FdgDescriptors.FDG9074,
        FdgDescriptors.FDG9081,
        FdgDescriptors.FDG9082);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? disposable = start.Compilation.GetTypeByMetadataName("System.IDisposable");
            start.RegisterSymbolAction(symbolContext => AnalyzeMethod(symbolContext, disposable), SymbolKind.Method);

            INamedTypeSymbol? asyncDisposable = start.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
            if (asyncDisposable?.GetMembers("DisposeAsync").FirstOrDefault() is IMethodSymbol disposeAsync
                && start.Compilation.GetTypeByMetadataName("System.GC") is { } gc)
            {
                start.RegisterOperationBlockAction(
                    blockContext => AnalyzeDisposeAsync(blockContext, disposeAsync, gc));
            }
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, INamedTypeSymbol? disposable)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.IsImplicitlyDeclared || NamingSymbols.SourceLocation(method) is not { } location)
        {
            return;
        }

        if (method.MethodKind == MethodKind.Destructor)
        {
            INamedTypeSymbol type = method.ContainingType;
            if (NamingSymbols.IsExternallyVisible(type))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(FdgDescriptors.FDG9073, location, UsageSymbols.Display(type)));
            }

            if (disposable is not null && !UsageSymbols.Implements(type, disposable))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(FdgDescriptors.FDG9074, location, UsageSymbols.Display(type)));
            }

            return;
        }

        if (method.MethodKind != MethodKind.Ordinary || NamingSymbols.IsInherited(method))
        {
            return;
        }

        if (method.Name == "Dispose" && !method.IsExtern && !IsDisposeSignature(method))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9067, location, method.ToDisplayString(_methodFormat)));
        }
        else if (method.Name == "DisposeAsync" && (method.Parameters.Length > 0 || method.IsGenericMethod))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9081, location, method.ToDisplayString(_methodFormat)));
        }
    }

    /// <summary>True for Dispose() and Dispose(bool), the bool passed by value.</summary>
    private static bool IsDisposeSignature(IMethodSymbol method)
    {
        return !method.IsGenericMethod
            && (method.Parameters.Length == 0
                || (method.Parameters.Length == 1
                    && method.Parameters[0] is
                    {
                        RefKind: RefKind.None, Type.SpecialType: SpecialType.System_Boolean,
                    }));
    }

    private static void AnalyzeDisposeAsync(
        OperationBlockAnalysisContext context, IMethodSymbol disposeAsync, INamedTypeSymbol gc)
    {
        if (context.OwningSymbol is not IMethodSymbol
            {
                Name: "DisposeAsync" or "System.IAsyncDisposable.DisposeAsync",
                IsOverride: false,
                IsAbstract: false,
                ContainingType: { TypeKind: TypeKind.Class, IsSealed: false, IsStatic: false } type,
            } method
            || !SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(disposeAsync), method)
            || NamingSymbols.SourceLocation(method) is not { } location)
        {
            return;
        }

        IBlockOperation? body = context.OperationBlocks
            .Select(operation => operation is IMethodBodyOperation methodBody
                ? methodBody.BlockBody ?? methodBody.ExpressionBody
                : operation as IBlockOperation)
            .FirstOrDefault(block => block is not null);
        if (body is null)
        {
            return;
        }

        bool expectsDispose = DeclaresDisposeBool(type);
        IOperation[] statements = body.Operations
            .Where(operation => operation is not IReturnOperation { ReturnedValue: null, IsImplicit: true })
            .Select(operation => operation is IExpressionStatementOperation statement ? statement.Operation : operation)
            .ToArray();
        bool matches = expectsDispose
            ? statements.Length == 3
                && IsAwaitOfCore(statements[0])
                && IsDisposeFalse(statements[1])
                && IsSuppressFinalize(statements[2], gc)
            : statements.Length == 2
                && IsAwaitOfCore(statements[0])
                && IsSuppressFinalize(statements[1], gc);
        if (!matches)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG9082,
                location,
                method.ToDisplayString(_methodFormat),
                expectsDispose ? AllSteps : AsyncSteps));
        }
    }

    /// <summary>True when the class or one of its base classes declares an instance Dispose(bool).</summary>
    private static bool DeclaresDisposeBool(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers("Dispose").OfType<IMethodSymbol>().Any(method =>
                    !method.IsStatic && method.Parameters.Length == 1 && IsDisposeSignature(method)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>await DisposeAsyncCore()</c> on this instance, optionally through ConfigureAwait.</summary>
    private static bool IsAwaitOfCore(IOperation operation)
    {
        if (operation is not IAwaitOperation { Operation: var awaited })
        {
            return false;
        }

        if (awaited is IInvocationOperation { TargetMethod.Name: "ConfigureAwait", Instance: { } configured })
        {
            awaited = configured;
        }

        return awaited is IInvocationOperation { TargetMethod.Name: "DisposeAsyncCore", Arguments.Length: 0 } core
            && IsThis(core.Instance);
    }

    /// <summary><c>Dispose(false)</c> on this instance.</summary>
    private static bool IsDisposeFalse(IOperation operation)
    {
        return operation is IInvocationOperation
            {
                TargetMethod: { Name: "Dispose", IsStatic: false } target,
                Arguments.Length: 1,
            } invocation
            && invocation.Arguments[0].Value.ConstantValue is { HasValue: true, Value: false }
            && IsDisposeSignature(target)
            && IsThis(invocation.Instance);
    }

    /// <summary><c>GC.SuppressFinalize(this)</c>.</summary>
    private static bool IsSuppressFinalize(IOperation operation, INamedTypeSymbol gc)
    {
        return operation is IInvocationOperation
            {
                TargetMethod.Name: "SuppressFinalize", Arguments.Length: 1,
            } invocation
            && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, gc)
            && IsThis(invocation.Arguments[0].Value);
    }

    private static bool IsThis(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance };
    }
}
