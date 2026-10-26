using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Patterns;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class PatternsDescriptorTests
{
    private static readonly string[] _ids =
    [
        "FDG9020", "FDG9022", "FDG9023", "FDG9027", "FDG9028", "FDG9029", "FDG9033", "FDG9036", "FDG9037", "FDG9038",
        "FDG9043", "FDG9045", "FDG9047", "FDG9048", "FDG9049", "FDG9052", "FDG9053", "FDG9054", "FDG9055", "FDG9056",
        "FDG9057", "FDG9067", "FDG9073", "FDG9074", "FDG9081", "FDG9082", "FDG9100", "FDG9103", "FDG9106", "FDG9111",
        "FDG9112", "FDG9115", "FDG9116", "FDG9117",
    ];

    private static readonly ImmutableArray<DiagnosticAnalyzer> _analyzers =
    [
        new AsyncSignatureAnalyzer(),
        new AsyncImplementationAnalyzer(),
        new AsyncEnumeratorAnalyzer(),
        new DependencyPropertyAnalyzer(),
        new DisposePatternAnalyzer(),
        new QueryPatternAnalyzer(),
        new VarianceAnalyzer(),
    ];

    [Fact]
    public async Task Descriptors_are_disabled_by_default_and_in_the_patterns_category()
    {
        DiagnosticDescriptor[] descriptors = _analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).ToArray();
        Assert.Equal(_ids, descriptors.Select(descriptor => descriptor.Id).Order());
        Assert.All(descriptors, descriptor => Assert.False(descriptor.IsEnabledByDefault));
        Assert.All(descriptors, descriptor => Assert.Equal("FrameworkDesignGuidelines.Patterns", descriptor.Category));

        const string Source =
            """
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using System.Windows;

            public class Api
            {
                public IAsyncEnumerator<int> Cursor { get; }

                public Task Save(CancellationToken cancellationToken = default) => Task.CompletedTask;

                public Task SendAsync() => Task.CompletedTask;

                public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public Task<int> ReadAsync(out int count, CancellationToken cancellationToken = default)
                {
                    count = 0;
                    return Task.FromResult(0);
                }

                public virtual Task WriteAsync(in int value, CancellationToken cancellationToken = default) =>
                    Task.CompletedTask;

                public Task PostAsync(in Mutable value, CancellationToken cancellationToken = default) =>
                    Task.CompletedTask;

                public void Copy(string source, string target) { }

                public Task CopyAsync(string target, string source, CancellationToken cancellationToken = default) =>
                    Task.CompletedTask;

                public async IAsyncEnumerable<int> Values(
                    [EnumeratorCancellation] CancellationToken cancellationToken = default)
                {
                    await Task.Yield();
                    yield return 1;
                }

                public Task NullAsync(CancellationToken cancellationToken = default)
                {
                    return null;
                }

                public Task CreatedAsync(CancellationToken cancellationToken = default)
                {
                    return new Task(() => { });
                }

                public async Task CancelAsync(CancellationToken cancellationToken = default)
                {
                    await Task.Yield();
                    if (cancellationToken.IsCancellationRequested) return;
                }

                public async Task CheckAsync(string name, CancellationToken cancellationToken = default)
                {
                    if (name == null) throw new ArgumentNullException(nameof(name));
                    await Task.Yield();
                }

                public async Task EachAsync(IAsyncEnumerable<int> source, CancellationToken cancellationToken = default)
                {
                    await foreach (int value in source)
                    {
                    }
                }
            }

            public struct Mutable
            {
                public int Value;
            }

            public class Both : IAsyncEnumerable<int>, IAsyncEnumerator<int>
            {
                public int Current => 0;
                public ValueTask DisposeAsync() => default;
                public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
                public ValueTask<bool> MoveNextAsync() => default;
            }

            public class Plain
            {
                public static readonly DependencyProperty TextProperty =
                    DependencyProperty.Register("Text", typeof(string), typeof(Plain));
            }

            public class Button : DependencyObject
            {
                private static readonly DependencyProperty SecretProperty =
                    DependencyProperty.Register("Secret", typeof(string), typeof(Button));

                public static readonly DependencyProperty Caption =
                    DependencyProperty.Register("Title", typeof(string), typeof(Button));

                public static readonly DependencyProperty SizeProperty =
                    DependencyProperty.Register("Size", typeof(int), typeof(Button));

                public static readonly DependencyProperty TextProperty =
                    DependencyProperty.Register("Text", typeof(string), typeof(Button));

                public Button()
                {
                    Text = "x";
                }

                public int Size => 0;

                public string Text
                {
                    get { return (string)GetValue(TextProperty); }
                    set { SetValue(TextProperty, value); Changed(); }
                }

                private void Changed() { }
            }

            internal class Holder
            {
                public void Dispose(int timeout) { }

                public void DisposeAsync(int timeout) { }
            }

            public class Finalizable : IDisposable
            {
                ~Finalizable() { }

                public void Dispose() { }
            }

            internal class Leaky
            {
                ~Leaky() { }
            }

            internal class Query
            {
                public Query Where(Func<int, bool> predicate) => this;
            }

            internal static class Ordering
            {
                public static IEnumerable<int> OrderBy(this IEnumerable<int> source, Func<int, int> key) => source;
            }

            public static class QueryExtensions
            {
                public static IEnumerable<int> Where(this IEnumerable<int> source, Func<int, bool> predicate) => source;
            }

            public delegate void Handler<T>(T value);

            public delegate T Factory<T>();

            public interface IConsumer<T>
            {
                void Consume(T value);
            }

            public interface IProducer<T>
            {
                T Produce();
            }

            internal interface IArrays<out T>
            {
                T[] ToArray();
            }

            namespace System.Windows
            {
                public class DependencyObject
                {
                    public object GetValue(DependencyProperty dp) { return null; }
                    public void SetValue(DependencyProperty dp, object value) { }
                }

                public sealed class DependencyProperty
                {
                    public static DependencyProperty Register(string name, Type propertyType, Type ownerType)
                    {
                        return null;
                    }
                }
            }
            """;
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Disabled",
            [CSharpSyntaxTree.ParseText(Source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(_analyzers)
            .GetAnalyzerDiagnosticsAsync();

        Assert.Empty(diagnostics);

        // Control: the same source does produce diagnostics once a configuration turns the rules on.
        CSharpCompilation enabled = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
            descriptors.ToDictionary(descriptor => descriptor.Id, _ => ReportDiagnostic.Warn)));
        ImmutableArray<Diagnostic> enabledDiagnostics = await enabled
            .WithAnalyzers(_analyzers)
            .GetAnalyzerDiagnosticsAsync();

        Assert.Equal(_ids, enabledDiagnostics.Select(diagnostic => diagnostic.Id).Order());
    }
}
