using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5008: on a generic type, two overloads with the same parameter count do not differ only in that one takes a
/// type parameter of the type where the other takes a specific type, which makes the first uncallable when the type
/// argument is that type. Only top-level parameter types are compared. A type parameter is matched with a type only
/// when the type can be shown to meet its constraints, and generic methods are left out.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GenericOverloadAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5008);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsInGenericType(type))
        {
            return;
        }

        foreach (OverloadFamily family in Overloads.Families(type))
        {
            var reported = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            for (int i = 0; i < family.Length; i++)
            {
                for (int j = i + 1; j < family.Length; j++)
                {
                    if (Collision(family[i], family[j], context.Compilation) is not { } collision)
                    {
                        continue;
                    }

                    foreach ((IParameterSymbol parameter, ITypeSymbol specific) in collision)
                    {
                        var method = (IMethodSymbol)parameter.ContainingSymbol;
                        bool inherited = SymbolEqualityComparer.Default.Equals(method, family[i])
                            ? family.IsInherited(i)
                            : family.IsInherited(j);
                        if (!inherited && reported.Add(parameter)
                            && NamingSymbols.SourceLocation(parameter) is { } location)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                FdgDescriptors.FDG5008,
                                location,
                                Overloads.DisplayName(method),
                                parameter.Type.Name,
                                specific.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The parameters that take a type parameter where the other overload takes a specific type, each with that type,
    /// when one choice of type arguments makes both signatures the same; otherwise null.
    /// </summary>
    private static List<(IParameterSymbol Parameter, ITypeSymbol Specific)>? Collision(
        IMethodSymbol first, IMethodSymbol second, Compilation compilation)
    {
        if (first.Parameters.Length != second.Parameters.Length || first.IsGenericMethod || second.IsGenericMethod)
        {
            return null;
        }

        var bindings = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        var generic = new List<(IParameterSymbol, ITypeSymbol)>();
        for (int k = 0; k < first.Parameters.Length; k++)
        {
            IParameterSymbol a = first.Parameters[k];
            IParameterSymbol b = second.Parameters[k];
            if (a.RefKind != b.RefKind)
            {
                return null;
            }

            if (Overloads.SameType(a.Type, b.Type))
            {
                continue;
            }

            (IParameterSymbol parameter, ITypeSymbol specific)? side =
                IsTypeParameterOfType(a.Type) && !ContainsTypeParameter(b.Type) ? (a, b.Type)
                : IsTypeParameterOfType(b.Type) && !ContainsTypeParameter(a.Type) ? (b, a.Type)
                : null;
            if (side is not { } found)
            {
                return null;
            }

            var typeParameter = (ITypeParameterSymbol)found.parameter.Type;
            if (bindings.TryGetValue(typeParameter, out ITypeSymbol? bound))
            {
                if (!Overloads.SameType(bound, found.specific))
                {
                    return null;
                }
            }
            else if (Satisfies(typeParameter, found.specific, compilation))
            {
                bindings.Add(typeParameter, found.specific);
            }
            else
            {
                return null;
            }

            generic.Add(found);
        }

        return generic.Count > 0 ? generic : null;
    }

    private static bool IsInGenericType(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypeParameterOfType(ITypeSymbol type)
    {
        return type is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Type };
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        return type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
            _ => false,
        };
    }

    /// <summary>
    /// True when <paramref name="type"/> can be shown to meet every constraint of <paramref name="typeParameter"/>.
    /// </summary>
    private static bool Satisfies(ITypeParameterSymbol typeParameter, ITypeSymbol type, Compilation compilation)
    {
        if ((typeParameter.HasReferenceTypeConstraint && !type.IsReferenceType)
            || (typeParameter.HasValueTypeConstraint
                && (!type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
            || (typeParameter.HasUnmanagedTypeConstraint && !type.IsUnmanagedType))
        {
            return false;
        }

        if (typeParameter.HasConstructorConstraint && !type.IsValueType
            && !(type is INamedTypeSymbol { IsAbstract: false } named
                && named.InstanceConstructors.Any(
                    c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public)))
        {
            return false;
        }

        foreach (ITypeSymbol constraint in typeParameter.ConstraintTypes)
        {
            if (ContainsTypeParameter(constraint))
            {
                return false;
            }

            CommonConversion conversion = compilation.ClassifyCommonConversion(type, constraint);
            if (!conversion.Exists || !conversion.IsImplicit || conversion.IsUserDefined || conversion.IsNumeric
                || conversion.IsNullable)
            {
                return false;
            }
        }

        return true;
    }
}
