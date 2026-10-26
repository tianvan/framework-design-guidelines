using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// FDG9111, FDG9112, FDG9115, FDG9116 and FDG9117 (section 9.8), variance of generic delegates and interfaces.
/// FDG9111 and FDG9112 report an invariant type parameter of an externally visible generic delegate or interface that
/// appears only in input positions, FDG9115 and FDG9116 one that appears only in output positions, following the
/// rules of the compiler (see <see cref="Walk"/>): parameters passed by value, property setters, event types and
/// constraints of method type parameters are inputs; return values, property getters and base interfaces are
/// outputs; ref, out and in parameters, ref returns, static members and constraints of the type's own type parameters
/// count as both. An unused type parameter is not reported, and neither is an interface that declares a class,
/// struct or enum, which cannot have variant type parameters (CS8427). For interfaces, an array in an output position
/// counts its element type as both, so that FDG9116 does not ask for what FDG9117 forbids. FDG9117 reports a non-static
/// method or a property with a getter, declared in an interface of any accessibility that has a covariant type
/// parameter, whose return or property type has, in an output position, an array whose element type uses a
/// covariant type parameter of the interface.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VarianceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Names a member with its parameter types, <c>ProduceArray(int)</c> or <c>this[int]</c>.</summary>
    private static readonly SymbolDisplayFormat _memberFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithMemberOptions(SymbolDisplayMemberOptions.IncludeParameters)
        .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        FdgDescriptors.FDG9111,
        FdgDescriptors.FDG9112,
        FdgDescriptors.FDG9115,
        FdgDescriptors.FDG9116,
        FdgDescriptors.FDG9117);

    [Flags]
    private enum Use
    {
        None = 0,
        Input = 1,
        Output = 2,
        Both = Input | Output,
    }

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Interface or TypeKind.Delegate, IsGenericType: true, IsImplicitlyDeclared: false,
            } type)
        {
            return;
        }

        if (type.TypeKind == TypeKind.Interface)
        {
            CheckArrays(context, type);
            if (type.GetTypeMembers().Any(nested =>
                    nested.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum))
            {
                return;
            }
        }

        if (!NamingSymbols.IsExternallyVisible(type))
        {
            return;
        }

        var uses = new Dictionary<ITypeParameterSymbol, Use>(SymbolEqualityComparer.Default);
        foreach (ITypeParameterSymbol parameter in type.TypeParameters)
        {
            uses[parameter] = Use.None;
        }

        bool isInterface = type.TypeKind == TypeKind.Interface;
        foreach (ITypeParameterSymbol parameter in type.TypeParameters)
        {
            foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
            {
                Walk(constraint, Use.Both, uses, isInterface);
            }
        }

        if (isInterface)
        {
            foreach (INamedTypeSymbol @interface in type.Interfaces)
            {
                Walk(@interface, Use.Output, uses, isInterface);
            }

            foreach (ISymbol member in type.GetMembers())
            {
                WalkMember(member, uses, isInterface);
            }
        }
        else if (type.DelegateInvokeMethod is { } invoke)
        {
            WalkMember(invoke, uses, isInterface);
        }

        foreach (ITypeParameterSymbol parameter in type.TypeParameters)
        {
            if (parameter.Variance != VarianceKind.None || NamingSymbols.SourceLocation(parameter) is not { } location)
            {
                continue;
            }

            DiagnosticDescriptor? descriptor = (uses[parameter], isInterface) switch
            {
                (Use.Input, false) => FdgDescriptors.FDG9111,
                (Use.Input, true) => FdgDescriptors.FDG9112,
                (Use.Output, false) => FdgDescriptors.FDG9115,
                (Use.Output, true) => FdgDescriptors.FDG9116,
                _ => null,
            };
            if (descriptor is not null)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(descriptor, location, parameter.Name, UsageSymbols.Display(type)));
            }
        }
    }

    private static void WalkMember(ISymbol member, Dictionary<ITypeParameterSymbol, Use> uses, bool isInterface)
    {
        if (member.IsImplicitlyDeclared && member is not IMethodSymbol { MethodKind: MethodKind.DelegateInvoke })
        {
            return;
        }

        Use output = member.IsStatic ? Use.Both : Use.Output;
        Use input = member.IsStatic ? Use.Both : Use.Input;
        switch (member)
        {
            case IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.DelegateInvoke } method:
                if (!method.ReturnsVoid)
                {
                    Walk(method.ReturnType, method.RefKind == RefKind.None ? output : Use.Both, uses, isInterface);
                }

                WalkParameters(method.Parameters, input, uses, isInterface);
                foreach (ITypeParameterSymbol parameter in method.TypeParameters)
                {
                    foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
                    {
                        Walk(constraint, input, uses, isInterface);
                    }
                }

                break;
            case IPropertySymbol property:
                if (property.RefKind != RefKind.None)
                {
                    Walk(property.Type, Use.Both, uses, isInterface);
                }
                else
                {
                    if (property.GetMethod is not null)
                    {
                        Walk(property.Type, output, uses, isInterface);
                    }

                    if (property.SetMethod is not null)
                    {
                        Walk(property.Type, input, uses, isInterface);
                    }
                }

                WalkParameters(property.Parameters, input, uses, isInterface);
                break;
            case IEventSymbol @event:
                Walk(@event.Type, input, uses, isInterface);
                break;
            case IFieldSymbol field:
                Walk(field.Type, Use.Both, uses, isInterface);
                break;
        }
    }

    private static void WalkParameters(
        ImmutableArray<IParameterSymbol> parameters, Use input, Dictionary<ITypeParameterSymbol, Use> uses,
        bool isInterface)
    {
        foreach (IParameterSymbol parameter in parameters)
        {
            Walk(parameter.Type, parameter.RefKind == RefKind.None ? input : Use.Both, uses, isInterface);
        }
    }

    /// <summary>
    /// Records the positions in which <paramref name="type"/> uses the type parameters in <paramref name="uses"/>:
    /// a type argument keeps the position for a covariant type parameter, flips it for a contravariant one and counts
    /// as both for an invariant one, as do the type arguments of a containing type, pointers and function pointers.
    /// An array keeps the position of its element, except an array in an output position of an interface.
    /// </summary>
    private static void Walk(
        ITypeSymbol type, Use position, Dictionary<ITypeParameterSymbol, Use> uses, bool isInterface)
    {
        switch (type)
        {
            case ITypeParameterSymbol parameter:
                if (uses.TryGetValue(parameter, out Use use))
                {
                    uses[parameter] = use | position;
                }

                break;
            case IArrayTypeSymbol array:
                Walk(array.ElementType, isInterface && (position & Use.Output) != 0 ? Use.Both : position, uses,
                    isInterface);
                break;
            case IPointerTypeSymbol pointer:
                Walk(pointer.PointedAtType, Use.Both, uses, isInterface);
                break;
            case IFunctionPointerTypeSymbol functionPointer:
                Walk(functionPointer.Signature.ReturnType, Use.Both, uses, isInterface);
                foreach (IParameterSymbol parameter in functionPointer.Signature.Parameters)
                {
                    Walk(parameter.Type, Use.Both, uses, isInterface);
                }

                break;
            case INamedTypeSymbol named:
                if (named.ContainingType is { } containing)
                {
                    Walk(containing, Use.Both, uses, isInterface);
                }

                ImmutableArray<ITypeParameterSymbol> definitions = named.OriginalDefinition.TypeParameters;
                for (int i = 0; i < named.TypeArguments.Length && i < definitions.Length; i++)
                {
                    Use argumentPosition = definitions[i].Variance switch
                    {
                        VarianceKind.Out => position,
                        VarianceKind.In => Flip(position),
                        _ => Use.Both,
                    };
                    Walk(named.TypeArguments[i], argumentPosition, uses, isInterface);
                }

                break;
        }
    }

    private static Use Flip(Use position)
    {
        return position switch
        {
            Use.Input => Use.Output,
            Use.Output => Use.Input,
            _ => position,
        };
    }

    private static void CheckArrays(SymbolAnalysisContext context, INamedTypeSymbol type)
    {
        ImmutableHashSet<ITypeParameterSymbol> covariant = ImmutableHashSet.CreateRange<ITypeParameterSymbol>(
            SymbolEqualityComparer.Default,
            type.TypeParameters.Where(parameter => parameter.Variance == VarianceKind.Out));
        if (covariant.IsEmpty)
        {
            return;
        }

        foreach (ISymbol member in type.GetMembers())
        {
            ITypeSymbol? returned = member switch
            {
                IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false, ReturnsVoid: false } method =>
                    method.ReturnType,
                IPropertySymbol { IsStatic: false, GetMethod: not null } property => property.Type,
                _ => null,
            };
            if (returned is not null
                && !member.IsImplicitlyDeclared
                && FindArray(returned, Use.Output, covariant) is { } array
                && NamingSymbols.SourceLocation(member) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG9117,
                    location,
                    UsageSymbols.Display(array),
                    member.ToDisplayString(_memberFormat),
                    UsageSymbols.Display(type)));
            }
        }
    }

    /// <summary>
    /// The outermost array in an output position of <paramref name="type"/> whose element type uses one of the
    /// <paramref name="covariant"/> type parameters, or null.
    /// </summary>
    private static IArrayTypeSymbol? FindArray(
        ITypeSymbol type, Use position, ImmutableHashSet<ITypeParameterSymbol> covariant)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return (position & Use.Output) != 0 && Uses(array.ElementType, covariant)
                    ? array
                    : FindArray(array.ElementType, position, covariant);
            case INamedTypeSymbol named:
                ImmutableArray<ITypeParameterSymbol> definitions = named.OriginalDefinition.TypeParameters;
                for (int i = 0; i < named.TypeArguments.Length && i < definitions.Length; i++)
                {
                    Use argumentPosition = definitions[i].Variance switch
                    {
                        VarianceKind.Out => position,
                        VarianceKind.In => Flip(position),
                        _ => Use.Both,
                    };
                    if (FindArray(named.TypeArguments[i], argumentPosition, covariant) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static bool Uses(ITypeSymbol type, ImmutableHashSet<ITypeParameterSymbol> parameters)
    {
        return type switch
        {
            ITypeParameterSymbol parameter => parameters.Contains(parameter),
            IArrayTypeSymbol array => Uses(array.ElementType, parameters),
            IPointerTypeSymbol pointer => Uses(pointer.PointedAtType, parameters),
            INamedTypeSymbol named => named.TypeArguments.Any(argument => Uses(argument, parameters))
                || (named.ContainingType is { } containing && Uses(containing, parameters)),
            _ => false,
        };
    }
}
