using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// The buffer types that the analyzers of section 9.12 look for in one compilation, by metadata name, so that the
/// framework types and those of the System.Memory package are both found.
/// </summary>
internal sealed class BufferTypes(Compilation compilation)
{
    public INamedTypeSymbol? Span { get; } = compilation.GetTypeByMetadataName("System.Span`1");

    public INamedTypeSymbol? ReadOnlySpan { get; } = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");

    public INamedTypeSymbol? Memory { get; } = compilation.GetTypeByMetadataName("System.Memory`1");

    public INamedTypeSymbol? ReadOnlyMemory { get; } = compilation.GetTypeByMetadataName("System.ReadOnlyMemory`1");

    public INamedTypeSymbol? ReadOnlySequence { get; } =
        compilation.GetTypeByMetadataName("System.Buffers.ReadOnlySequence`1");

    public INamedTypeSymbol? ArraySegment { get; } = compilation.GetTypeByMetadataName("System.ArraySegment`1");

    public INamedTypeSymbol? OperationStatus { get; } =
        compilation.GetTypeByMetadataName("System.Buffers.OperationStatus");

    public INamedTypeSymbol? Range { get; } = compilation.GetTypeByMetadataName("System.Range");

    public INamedTypeSymbol? MemoryExtensions { get; } = compilation.GetTypeByMetadataName("System.MemoryExtensions");

    /// <summary>True when the compilation has the span types at all.</summary>
    public bool HasSpans => Span is not null && ReadOnlySpan is not null;

    public bool IsSpan(ITypeSymbol? type)
    {
        return UsageSymbols.Is(type, Span);
    }

    public bool IsReadOnlySpan(ITypeSymbol? type)
    {
        return UsageSymbols.Is(type, ReadOnlySpan);
    }

    public bool IsMemory(ITypeSymbol? type)
    {
        return UsageSymbols.Is(type, Memory);
    }

    public bool IsReadOnlyMemory(ITypeSymbol? type)
    {
        return UsageSymbols.Is(type, ReadOnlyMemory);
    }

    /// <summary>
    /// True for an input buffer type: <c>ReadOnlySpan&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c>,
    /// <c>ReadOnlySequence&lt;T&gt;</c>, an array, <c>ArraySegment&lt;T&gt;</c> or string.
    /// </summary>
    public bool IsInput(ITypeSymbol type)
    {
        return IsReadOnlySpan(type)
            || IsReadOnlyMemory(type)
            || UsageSymbols.Is(type, ReadOnlySequence)
            || type is IArrayTypeSymbol
            || UsageSymbols.Is(type, ArraySegment)
            || type.SpecialType == SpecialType.System_String;
    }

    /// <summary>
    /// True for an output buffer type: <c>Span&lt;T&gt;</c>, <c>Memory&lt;T&gt;</c>, an array or
    /// <c>ArraySegment&lt;T&gt;</c>.
    /// </summary>
    public bool IsOutput(ITypeSymbol type)
    {
        return IsSpan(type) || IsMemory(type) || type is IArrayTypeSymbol || UsageSymbols.Is(type, ArraySegment);
    }

    /// <summary>
    /// The first word of the name of an out parameter that counts the elements of a buffer type, as FDG9159 and
    /// FDG9172 give it: bytes for byte and sbyte elements, chars for char elements and for string, values otherwise.
    /// </summary>
    public static string CountWord(ITypeSymbol type)
    {
        ITypeSymbol? element = type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { TypeArguments.Length: 1 } named => named.TypeArguments[0],
            _ => null,
        };
        return (element ?? type).SpecialType switch
        {
            SpecialType.System_Byte or SpecialType.System_SByte => "bytes",
            SpecialType.System_Char or SpecialType.System_String => "chars",
            _ => "values",
        };
    }
}
