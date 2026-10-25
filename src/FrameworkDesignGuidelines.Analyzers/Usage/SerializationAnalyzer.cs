using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Usage;

/// <summary>
/// FDG8088 and FDG8089 (section 8.10): the members of a class or struct declared in source that implements
/// <c>ISerializable</c>, directly or through a base class such as <c>Exception</c>. FDG8088 reports a
/// <c>(SerializationInfo, StreamingContext)</c> constructor that is not protected in an unsealed class, or private in
/// a sealed class or a struct. FDG8089 reports the type's own implementation of <c>GetObjectData</c> when it is
/// implicit: an override takes its accessibility from the member it overrides and is not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SerializationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG8088, FdgDescriptors.FDG8089);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            Compilation compilation = start.Compilation;
            INamedTypeSymbol? serializable =
                compilation.GetTypeByMetadataName("System.Runtime.Serialization.ISerializable");
            INamedTypeSymbol? info =
                compilation.GetTypeByMetadataName("System.Runtime.Serialization.SerializationInfo");
            INamedTypeSymbol? streamingContext =
                compilation.GetTypeByMetadataName("System.Runtime.Serialization.StreamingContext");
            if (serializable?.GetMembers("GetObjectData").OfType<IMethodSymbol>().FirstOrDefault()
                is not { } getObjectData)
            {
                return;
            }

            start.RegisterSymbolAction(
                symbolContext => AnalyzeType(symbolContext, serializable, getObjectData, info, streamingContext),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(
        SymbolAnalysisContext context,
        INamedTypeSymbol serializable,
        IMethodSymbol getObjectData,
        INamedTypeSymbol? info,
        INamedTypeSymbol? streamingContext)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } type
            || !UsageSymbols.Implements(type, serializable))
        {
            return;
        }

        string name = UsageSymbols.Display(type);
        Accessibility expected = type.IsSealed ? Accessibility.Private : Accessibility.Protected;
        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (!constructor.IsImplicitlyDeclared
                && constructor.Parameters.Length == 2
                && UsageSymbols.Is(constructor.Parameters[0].Type, info)
                && UsageSymbols.Is(constructor.Parameters[1].Type, streamingContext)
                && constructor.DeclaredAccessibility != expected
                && NamingSymbols.SourceLocation(constructor) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG8088,
                    location,
                    name,
                    expected == Accessibility.Private ? "private" : "protected"));
            }
        }

        if (type.FindImplementationForInterfaceMember(getObjectData) is IMethodSymbol
            {
                IsOverride: false, IsImplicitlyDeclared: false, ExplicitInterfaceImplementations.IsEmpty: true,
            } implementation
            && SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type)
            && NamingSymbols.SourceLocation(implementation) is { } methodLocation)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8089, methodLocation, name));
        }
    }
}
