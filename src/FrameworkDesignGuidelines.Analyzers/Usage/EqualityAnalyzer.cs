using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Usage;

/// <summary>
/// FDG8070, FDG8102, FDG8103, FDG8104 and FDG8106 (sections 8.9.1.2 and 8.13): value equality and the equality
/// operators of a class or struct declared in source. A class is mutable when it, or one of its base classes declared
/// in source, has an instance field that is neither readonly nor const (<see cref="UsageSymbols.HasMutableField"/>).
/// FDG8070 reports a mutable class that overrides <c>Object.Equals</c>, or a mutable record class. On the first
/// <c>operator ==</c> it declares, FDG8102 reports a type that does not implement <c>IEquatable&lt;T&gt;</c> of itself
/// and FDG8106 a mutable class. FDG8104 reports an <c>operator ==</c> whose parameter types differ, a
/// <c>Nullable&lt;T&gt;</c> counting as its T, and FDG8103 reports on it the overload with the parameters reversed when
/// neither parameter type declares it, and, once per type, the overload taking two instances of the declaring type
/// when the type has none. Operators that records declare implicitly are not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EqualityAnalyzer : DiagnosticAnalyzer
{
    private const string EqualityOperatorName = "op_Equality";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG8070,
            FdgDescriptors.FDG8102,
            FdgDescriptors.FDG8103,
            FdgDescriptors.FDG8104,
            FdgDescriptors.FDG8106);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? equatable = start.Compilation.GetTypeByMetadataName("System.IEquatable`1");
            start.RegisterSymbolAction(
                symbolContext => AnalyzeType(symbolContext, equatable),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol? equatable)
    {
        if (context.Symbol is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Class or TypeKind.Struct, IsImplicitlyDeclared: false,
            } type)
        {
            return;
        }

        string name = UsageSymbols.Display(type);
        bool isMutableClass = type.TypeKind == TypeKind.Class && UsageSymbols.HasMutableField(type);
        if (isMutableClass
            && (type.IsRecord || OverridesEquals(type))
            && NamingSymbols.SourceLocation(type) is { } typeLocation)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8070, typeLocation, name));
        }

        List<IMethodSymbol> operators = EqualityOperators(type).ToList();
        if (operators.Count == 0 || NamingSymbols.SourceLocation(operators[0]) is not { } firstLocation)
        {
            return;
        }

        if (!UsageSymbols.IsEquatableToItself(type, equatable))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8102, firstLocation, name));
        }

        if (isMutableClass)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8106, firstLocation, name));
        }

        bool needsSameType = !operators.Any(@operator =>
            IsSame(@operator.Parameters[0].Type, type) && IsSame(@operator.Parameters[1].Type, type));
        foreach (IMethodSymbol @operator in operators)
        {
            ITypeSymbol left = @operator.Parameters[0].Type;
            ITypeSymbol right = @operator.Parameters[1].Type;
            if (IsSame(left, right) || NamingSymbols.SourceLocation(@operator) is not { } location)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG8104, location, UsageSymbols.Display(left), UsageSymbols.Display(right)));
            if (!HasOperator(right, left, type))
            {
                ReportMissing(context, location, left, right, right, left);
            }

            if (needsSameType)
            {
                ReportMissing(context, location, left, right, type, type);
                needsSameType = false;
            }
        }
    }

    private static void ReportMissing(
        SymbolAnalysisContext context,
        Location location,
        ITypeSymbol left,
        ITypeSymbol right,
        ITypeSymbol missingLeft,
        ITypeSymbol missingRight)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            FdgDescriptors.FDG8103,
            location,
            UsageSymbols.Display(left),
            UsageSymbols.Display(right),
            UsageSymbols.Display(missingLeft),
            UsageSymbols.Display(missingRight)));
    }

    /// <summary>The <c>operator ==</c> overloads that the type declares itself, in declaration order.</summary>
    private static IEnumerable<IMethodSymbol> EqualityOperators(ITypeSymbol type)
    {
        return type.GetMembers(EqualityOperatorName).OfType<IMethodSymbol>().Where(method => method is
        {
            MethodKind: MethodKind.UserDefinedOperator, IsImplicitlyDeclared: false, Parameters.Length: 2,
        });
    }

    /// <summary>
    /// True when <paramref name="type"/> or one of the parameter types declares <c>operator ==</c> taking
    /// <paramref name="left"/> and <paramref name="right"/>.
    /// </summary>
    private static bool HasOperator(ITypeSymbol left, ITypeSymbol right, ITypeSymbol type)
    {
        return new[] { type, Strip(left), Strip(right) }.Any(owner => EqualityOperators(owner).Any(@operator =>
            IsSame(@operator.Parameters[0].Type, left) && IsSame(@operator.Parameters[1].Type, right)));
    }

    private static bool OverridesEquals(INamedTypeSymbol type)
    {
        return type.GetMembers(nameof(Equals)).OfType<IMethodSymbol>().Any(method =>
            method is { IsOverride: true, IsImplicitlyDeclared: false, Parameters.Length: 1 }
            && method.Parameters[0].Type.SpecialType == SpecialType.System_Object);
    }

    /// <summary>True when the two types are the same once a <c>Nullable&lt;T&gt;</c> is taken as its T.</summary>
    private static bool IsSame(ITypeSymbol first, ITypeSymbol second)
    {
        return SymbolEqualityComparer.Default.Equals(Strip(first), Strip(second));
    }

    private static ITypeSymbol Strip(ITypeSymbol type)
    {
        return type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: { Length: 1 } arguments,
            }
            ? arguments[0]
            : type;
    }
}
