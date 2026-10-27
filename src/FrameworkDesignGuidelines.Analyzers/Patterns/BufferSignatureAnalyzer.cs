using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// The signature rules of section 9.12 for methods that operate on buffers. FDG9143 and FDG9144 report a
/// <c>ReadOnlySpan&lt;T&gt;</c> or <c>Span&lt;T&gt;</c> parameter of a method of any accessibility that returns a
/// task-like type or <c>IAsyncEnumerable&lt;T&gt;</c>. FDG9147 and FDG9148 compare overloads (see
/// <see cref="AnalyzeOverloads"/>). FDG9156 and FDG9157 check the positions of the parameters named source and
/// destination. FDG9159, FDG9162 and FDG9163 check Try-Write methods, and FDG9159, FDG9171, FDG9172, FDG9173 and
/// FDG9174 methods that return <c>OperationStatus</c>. Every rule but FDG9143 and FDG9144 applies to externally
/// visible methods only, and none to overrides and interface implementations, whose signatures come from another
/// member.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BufferSignatureAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        FdgDescriptors.FDG9143,
        FdgDescriptors.FDG9144,
        FdgDescriptors.FDG9147,
        FdgDescriptors.FDG9148,
        FdgDescriptors.FDG9156,
        FdgDescriptors.FDG9157,
        FdgDescriptors.FDG9159,
        FdgDescriptors.FDG9162,
        FdgDescriptors.FDG9163,
        FdgDescriptors.FDG9171,
        FdgDescriptors.FDG9172,
        FdgDescriptors.FDG9173,
        FdgDescriptors.FDG9174);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var buffers = new BufferTypes(start.Compilation);
            var async = new AsyncTypes(start.Compilation);
            start.RegisterSymbolAction(
                symbolContext => AnalyzeMethod(symbolContext, buffers, async), SymbolKind.Method);
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, BufferTypes buffers, AsyncTypes async)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method is not { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false }
            || NamingSymbols.IsInherited(method)
            || NamingSymbols.SourceLocation(method) is not { } location)
        {
            return;
        }

        string name = UsageSymbols.Display(method.ContainingType) + "." + method.Name;
        if (buffers.HasSpans && (async.IsTaskLike(method.ReturnType) || async.IsAsyncEnumerable(method.ReturnType)))
        {
            AnalyzeAsync(context, method, name, buffers);
        }

        if (!NamingSymbols.IsExternallyVisible(method))
        {
            return;
        }

        AnalyzeOverloads(context, method, location, name, buffers);
        AnalyzePositions(context, method, name, buffers);
        if (UsageSymbols.Is(method.ReturnType, buffers.OperationStatus))
        {
            AnalyzeOperationStatus(context, method, location, name, buffers);
        }
        else
        {
            AnalyzeTryWrite(context, method, location, name, buffers);
        }
    }

    private static void AnalyzeAsync(
        SymbolAnalysisContext context, IMethodSymbol method, string name, BufferTypes buffers)
    {
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            DiagnosticDescriptor? descriptor = buffers.IsReadOnlySpan(parameter.Type) ? FdgDescriptors.FDG9143
                : buffers.IsSpan(parameter.Type) ? FdgDescriptors.FDG9144
                : null;
            if (descriptor is not null && NamingSymbols.SourceLocation(parameter) is { } location)
            {
                ITypeSymbol element = ((INamedTypeSymbol)parameter.Type).TypeArguments[0];
                context.ReportDiagnostic(Diagnostic.Create(
                    descriptor, location, UsageSymbols.Display(element), parameter.Name, name));
            }
        }
    }

    /// <summary>
    /// FDG9147 and FDG9148: an externally visible overload in the same type with the same number of parameters, the
    /// same ref kinds and the same types except at positions where one takes <c>Span&lt;T&gt;</c> and the other
    /// <c>ReadOnlySpan&lt;T&gt;</c>, or <c>Memory&lt;T&gt;</c> and <c>ReadOnlyMemory&lt;T&gt;</c> (FDG9147, reported
    /// on the writable method), or one takes <c>Memory&lt;T&gt;</c> and the other <c>Span&lt;T&gt;</c>, or
    /// <c>ReadOnlyMemory&lt;T&gt;</c> and <c>ReadOnlySpan&lt;T&gt;</c> (FDG9148, reported on the Memory method). The
    /// pair is exempt when each method returns the type of its own differing parameter, as TrimEnd does. Each method
    /// is reported once per rule, against the first such overload in declaration order.
    /// </summary>
    private static void AnalyzeOverloads(
        SymbolAnalysisContext context, IMethodSymbol method, Location location, string name, BufferTypes buffers)
    {
        bool reported9147 = false;
        bool reported9148 = false;
        foreach (IMethodSymbol other in method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>())
        {
            if (SymbolEqualityComparer.Default.Equals(other, method)
                || other.MethodKind != MethodKind.Ordinary
                || other.IsImplicitlyDeclared
                || other.Parameters.Length != method.Parameters.Length
                || other.TypeParameters.Length != method.TypeParameters.Length
                || !NamingSymbols.IsExternallyVisible(other)
                || NamingSymbols.IsInherited(other))
            {
                continue;
            }

            int? first = null;
            bool writable = true;
            bool memory = true;
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                IParameterSymbol mine = method.Parameters[i];
                IParameterSymbol theirs = other.Parameters[i];
                if (mine.RefKind != theirs.RefKind)
                {
                    writable = memory = false;
                    break;
                }

                if (SameType(mine.Type, theirs.Type, method, other))
                {
                    continue;
                }

                if (!SameElement(mine.Type, theirs.Type, method, other))
                {
                    writable = memory = false;
                    break;
                }

                first ??= i;
                writable &= (buffers.IsSpan(mine.Type) && buffers.IsReadOnlySpan(theirs.Type))
                    || (buffers.IsMemory(mine.Type) && buffers.IsReadOnlyMemory(theirs.Type));
                memory &= (buffers.IsMemory(mine.Type) && buffers.IsSpan(theirs.Type))
                    || (buffers.IsReadOnlyMemory(mine.Type) && buffers.IsReadOnlySpan(theirs.Type));
            }

            if (first is not { } position || ReturnsOwnType(method, other, position))
            {
                continue;
            }

            DiagnosticDescriptor? descriptor = writable && !reported9147 ? FdgDescriptors.FDG9147
                : memory && !reported9148 ? FdgDescriptors.FDG9148
                : null;
            if (descriptor is null)
            {
                continue;
            }

            reported9147 |= writable;
            reported9148 |= memory;
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                location,
                name,
                UsageSymbols.Display(method.Parameters[position].Type),
                UsageSymbols.Display(other.Parameters[position].Type)));
        }
    }

    /// <summary>
    /// True when each method returns the type of its own parameter at <paramref name="position"/>, the first
    /// position where they differ.
    /// </summary>
    private static bool ReturnsOwnType(IMethodSymbol method, IMethodSymbol other, int position)
    {
        return SymbolEqualityComparer.Default.Equals(method.ReturnType, method.Parameters[position].Type)
            && SymbolEqualityComparer.Default.Equals(other.ReturnType, other.Parameters[position].Type);
    }

    /// <summary>
    /// True for equal types, where a type parameter of one method matches the type parameter of the other at the
    /// same position.
    /// </summary>
    private static bool SameType(
        ITypeSymbol left, ITypeSymbol right, IMethodSymbol leftMethod, IMethodSymbol rightMethod)
    {
        if (left is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } leftParameter
            && right is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } rightParameter)
        {
            return SymbolEqualityComparer.Default.Equals(leftParameter.DeclaringMethod, leftMethod)
                && SymbolEqualityComparer.Default.Equals(rightParameter.DeclaringMethod, rightMethod)
                && leftParameter.Ordinal == rightParameter.Ordinal;
        }

        if (left is INamedTypeSymbol { IsGenericType: true } leftNamed
            && right is INamedTypeSymbol { IsGenericType: true } rightNamed)
        {
            return SymbolEqualityComparer.Default.Equals(leftNamed.OriginalDefinition, rightNamed.OriginalDefinition)
                && leftNamed.TypeArguments.Zip(rightNamed.TypeArguments, (l, r) => (l, r))
                    .All(pair => SameType(pair.l, pair.r, leftMethod, rightMethod));
        }

        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        {
            return leftArray.Rank == rightArray.Rank
                && SameType(leftArray.ElementType, rightArray.ElementType, leftMethod, rightMethod);
        }

        return SymbolEqualityComparer.Default.Equals(left, right);
    }

    /// <summary>True for two generic types with one type argument each, the same for both.</summary>
    private static bool SameElement(
        ITypeSymbol left, ITypeSymbol right, IMethodSymbol leftMethod, IMethodSymbol rightMethod)
    {
        return left is INamedTypeSymbol { TypeArguments.Length: 1 } leftNamed
            && right is INamedTypeSymbol { TypeArguments.Length: 1 } rightNamed
            && SameType(leftNamed.TypeArguments[0], rightNamed.TypeArguments[0], leftMethod, rightMethod);
    }

    /// <summary>
    /// FDG9156: a parameter named source of an input buffer type after a parameter that is an output buffer named
    /// destination, or neither another input buffer nor an int that follows an array; the this parameter of an
    /// extension method is not counted. A string or array named source is checked only when the method also has an
    /// output buffer. FDG9157: a parameter named destination of an output buffer type before a
    /// parameter named source.
    /// </summary>
    private static void AnalyzePositions(
        SymbolAnalysisContext context, IMethodSymbol method, string name, BufferTypes buffers)
    {
        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        int sourceIndex = -1;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Name == "source")
            {
                sourceIndex = i;
                break;
            }
        }

        if (sourceIndex < 0)
        {
            return;
        }

        IParameterSymbol source = parameters[sourceIndex];

        // Without an output buffer, a string or array named source can be a name, a path or a collection.
        bool isBuffer = buffers.IsReadOnlySpan(source.Type)
            || buffers.IsReadOnlyMemory(source.Type)
            || UsageSymbols.Is(source.Type, buffers.ReadOnlySequence)
            || parameters.Any(parameter => !SymbolEqualityComparer.Default.Equals(parameter, source)
                && buffers.IsOutput(parameter.Type));
        int start = method.IsExtensionMethod ? 1 : 0;
        bool misplaced = false;
        for (int i = start; i < sourceIndex; i++)
        {
            bool isOffset = parameters[i].Type.SpecialType == SpecialType.System_Int32
                && i > start
                && parameters[i - 1].Type is IArrayTypeSymbol;
            bool isDestination = parameters[i].Name == "destination" && buffers.IsOutput(parameters[i].Type);
            misplaced |= isDestination || (!buffers.IsInput(parameters[i].Type) && !isOffset);
            if (isDestination && NamingSymbols.SourceLocation(parameters[i]) is { } destinationLocation)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG9157, destinationLocation, parameters[i].Name, name, source.Name));
            }
        }

        if (misplaced
            && isBuffer
            && buffers.IsInput(source.Type)
            && NamingSymbols.SourceLocation(source) is { } location)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9156, location, source.Name, name));
        }
    }

    /// <summary>
    /// FDG9159, FDG9162 and FDG9163 for a method with a parameter named destination of an output buffer type. A
    /// Try-Write method is named Try followed by an uppercase letter and returns bool: FDG9163 when it has no out
    /// parameter of an integer type, FDG9159 when it has one whose name is not the one for the element type. FDG9162
    /// when a method with an out parameter of an integer type ending with Written has only one of the name and the
    /// return type of a Try-Write method.
    /// </summary>
    private static void AnalyzeTryWrite(
        SymbolAnalysisContext context, IMethodSymbol method, Location location, string name, BufferTypes buffers)
    {
        IParameterSymbol? destination = method.Parameters.FirstOrDefault(
            parameter => parameter.Name == "destination" && buffers.IsOutput(parameter.Type));
        if (destination is null)
        {
            return;
        }

        IParameterSymbol[] outs = OutCounts(method);
        bool isTry = method.Name.Length > 3 && method.Name.StartsWith("Try", StringComparison.Ordinal)
            && char.IsUpper(method.Name[3]);
        bool returnsBool = method.ReturnType.SpecialType == SpecialType.System_Boolean;
        if (isTry && returnsBool)
        {
            if (outs.Length == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9163, location, name));
            }
            else if (outs.Length == 1)
            {
                ReportName(context, outs[0], name, BufferTypes.CountWord(destination.Type) + "Written", "Written");
            }
        }
        else if (isTry != returnsBool
            && outs.Any(parameter => parameter.Name.EndsWith("Written", StringComparison.Ordinal)))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9162, location, name));
        }
    }

    /// <summary>
    /// FDG9159, FDG9171, FDG9172, FDG9173 and FDG9174 for a method that returns <c>OperationStatus</c>. The input
    /// buffer is the parameter named source, or else the first <c>ReadOnlySpan&lt;T&gt;</c>,
    /// <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ReadOnlySequence&lt;T&gt;</c> or string parameter; the output buffer is the
    /// parameter named destination, or else the first <c>Span&lt;T&gt;</c> or <c>Memory&lt;T&gt;</c> parameter.
    /// </summary>
    private static void AnalyzeOperationStatus(
        SymbolAnalysisContext context, IMethodSymbol method, Location location, string name, BufferTypes buffers)
    {
        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        IParameterSymbol? source = parameters.FirstOrDefault(parameter => parameter.Name == "source")
            ?? parameters.FirstOrDefault(parameter => parameter.RefKind == RefKind.None
                && (buffers.IsReadOnlySpan(parameter.Type)
                    || buffers.IsReadOnlyMemory(parameter.Type)
                    || UsageSymbols.Is(parameter.Type, buffers.ReadOnlySequence)
                    || parameter.Type.SpecialType == SpecialType.System_String));
        IParameterSymbol? destination = parameters.FirstOrDefault(parameter => parameter.Name == "destination")
            ?? parameters.FirstOrDefault(parameter => parameter.RefKind == RefKind.None
                && (buffers.IsSpan(parameter.Type) || buffers.IsMemory(parameter.Type)));
        IParameterSymbol[] outs = OutCounts(method);

        if (source is not null
            && (outs.Length == 0 || (outs.Length == 1 && outs[0].Name.EndsWith("Written", StringComparison.Ordinal))))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9171, location, name));
        }

        int written = Array.FindIndex(outs, parameter => parameter.Name.EndsWith("Written", StringComparison.Ordinal));
        int consumed = Array.FindLastIndex(
            outs, parameter => parameter.Name.EndsWith("Consumed", StringComparison.Ordinal));
        if ((destination is not null && outs.Length == 0)
            || (source is not null && outs.Length == 1 && consumed == 0)
            || (written >= 0 && consumed > written))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9173, location, name));
        }

        if (outs.Length == 2)
        {
            bool swapped = outs[0].Name.EndsWith("Written", StringComparison.Ordinal)
                && outs[1].Name.EndsWith("Consumed", StringComparison.Ordinal);
            IParameterSymbol consumedCount = swapped ? outs[1] : outs[0];
            IParameterSymbol writtenCount = swapped ? outs[0] : outs[1];
            if (source is not null)
            {
                ReportName(context, consumedCount, name, BufferTypes.CountWord(source.Type) + "Consumed", "Consumed");
            }

            if (destination is not null)
            {
                ReportName(context, writtenCount, name, BufferTypes.CountWord(destination.Type) + "Written", "Written");
            }
        }

        foreach (IParameterSymbol parameter in parameters)
        {
            if (parameter.Type.SpecialType != SpecialType.System_Boolean
                || NamingSymbols.SourceLocation(parameter) is not { } flagLocation)
            {
                continue;
            }

            bool isFinalBlock = parameter.Name == "isFinalBlock";
            bool misnamed = !isFinalBlock && NamingSymbols.Words(parameter.Name).Any(word =>
                string.Equals(word, "Final", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "Last", StringComparison.OrdinalIgnoreCase));
            bool wrongDefault = isFinalBlock
                && !(parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: true });
            if (misnamed || wrongDefault)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9174, flagLocation, parameter.Name, name));
            }
        }
    }

    /// <summary>
    /// Reports FDG9159 (for <paramref name="suffix"/> Written) or FDG9172 (for Consumed) when the out parameter is
    /// not named <paramref name="expected"/>.
    /// </summary>
    private static void ReportName(
        SymbolAnalysisContext context, IParameterSymbol parameter, string name, string expected, string suffix)
    {
        if (parameter.Name == expected || NamingSymbols.SourceLocation(parameter) is not { } location)
        {
            return;
        }

        DiagnosticDescriptor descriptor = suffix == "Written" ? FdgDescriptors.FDG9159 : FdgDescriptors.FDG9172;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, parameter.Name, name, expected));
    }

    /// <summary>The out parameters of an integer type, in declaration order.</summary>
    private static IParameterSymbol[] OutCounts(IMethodSymbol method)
    {
        return method.Parameters
            .Where(parameter => parameter.RefKind == RefKind.Out
                && parameter.Type.SpecialType is SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64)
            .ToArray();
    }
}
