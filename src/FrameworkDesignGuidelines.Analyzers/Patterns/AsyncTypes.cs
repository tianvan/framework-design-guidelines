using Microsoft.CodeAnalysis;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>The types that the asynchronous pattern analyzers of section 9.2 look for in one compilation.</summary>
internal sealed class AsyncTypes(Compilation compilation)
{
    public INamedTypeSymbol? Task { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");

    public INamedTypeSymbol? TaskOfT { get; } = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");

    public INamedTypeSymbol? ValueTask { get; } =
        compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");

    public INamedTypeSymbol? ValueTaskOfT { get; } =
        compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");

    public INamedTypeSymbol? AsyncEnumerable { get; } =
        compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");

    public INamedTypeSymbol? AsyncEnumerator { get; } =
        compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerator`1");

    public INamedTypeSymbol? CancellationToken { get; } =
        compilation.GetTypeByMetadataName("System.Threading.CancellationToken");

    public INamedTypeSymbol? ArgumentException { get; } =
        compilation.GetTypeByMetadataName("System.ArgumentException");

    /// <summary>
    /// True for <c>Task</c> or <c>Task&lt;TResult&gt;</c>, the reference types whose value can be null.
    /// </summary>
    public bool IsTask(ITypeSymbol? type)
    {
        return Is(type, Task) || Is(type, TaskOfT);
    }

    /// <summary>
    /// True for <c>Task</c>, <c>Task&lt;TResult&gt;</c>, <c>ValueTask</c>, <c>ValueTask&lt;TResult&gt;</c>, or a type
    /// marked with an attribute named <c>System.Runtime.CompilerServices.AsyncMethodBuilderAttribute</c>, which is
    /// how the compiler finds task-like types (a polyfilled attribute counts too).
    /// </summary>
    public bool IsTaskLike(ITypeSymbol? type)
    {
        return IsTask(type)
            || Is(type, ValueTask)
            || Is(type, ValueTaskOfT)
            || (type is INamedTypeSymbol named && named.OriginalDefinition.GetAttributes().Any(attribute =>
                attribute.AttributeClass is { Name: "AsyncMethodBuilderAttribute", ContainingNamespace: { } ns }
                && ns.ToDisplayString() == "System.Runtime.CompilerServices"));
    }

    public bool IsAsyncEnumerable(ITypeSymbol? type)
    {
        return Is(type, AsyncEnumerable);
    }

    public bool IsCancellationToken(ITypeSymbol? type)
    {
        return CancellationToken is not null && SymbolEqualityComparer.Default.Equals(type, CancellationToken);
    }

    private static bool Is(ITypeSymbol? type, INamedTypeSymbol? definition)
    {
        return type is not null
            && definition is not null
            && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition);
    }
}
