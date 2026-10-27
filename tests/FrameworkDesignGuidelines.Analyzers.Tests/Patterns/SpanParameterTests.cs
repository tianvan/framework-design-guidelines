using FrameworkDesignGuidelines.Analyzers.Patterns;
using FrameworkDesignGuidelines.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class SpanParameterTests
{
    [Fact]
    public Task Book_sum_over_a_writable_span_is_reported()
    {
        return BufferReferences.VerifyAsync<SpanParameterAnalyzer>(
            """
            using System;

            public static class Sums
            {
                public static long Sum(ReadOnlySpan<int> values)
                {
                    long sum = 0;
                    for (int i = 0; i < values.Length; i++)
                    {
                        sum += values[i];
                    }

                    return sum;
                }

                public static long Sum(int[] values)
                {
                    if (values == null)
                    {
                        throw new ArgumentNullException(nameof(values));
                    }

                    return Sum(values.AsSpan());
                }

                public static long Sum(Span<int> {|FDG9138:values|})
                {
                    return Sum((ReadOnlySpan<int>)values);
                }
            }
            """);
    }

    [Fact]
    public Task Read_only_uses_are_reported()
    {
        return BufferReferences.VerifyAsync<SpanParameterAnalyzer>(
            """
            using System;

            public class Reader
            {
                public Reader(Span<byte> {|FDG9138:header|})
                {
                    Length = header.Length;
                }

                public int Length { get; }

                public static bool Empty(Span<int> {|FDG9138:values|}) => values.IsEmpty;

                public static int Last(Span<int> {|FDG9138:values|}) => values[values.Length - 1];

                public static int Total(Span<int> {|FDG9138:values|})
                {
                    int total = 0;
                    foreach (int value in values)
                    {
                        total += value;
                    }

                    return total;
                }

                public static int[] Copy(Span<int> {|FDG9138:values|}, Span<int> destination)
                {
                    values.CopyTo(destination);
                    bool copied = values.TryCopyTo(destination);
                    return values.ToArray();
                }

                public static string Text(Span<char> {|FDG9138:chars|}) => chars.ToString();

                public static int Find(Span<int> {|FDG9138:values|})
                {
                    return values.IndexOf(3) + values.LastIndexOf(3) + values.IndexOfAny(4, 5);
                }

                public static bool Same(Span<int> {|FDG9138:left|}, ReadOnlySpan<int> right)
                {
                    return left.SequenceEqual(right) && left.StartsWith(right);
                }

                public static void Unused(Span<int> {|FDG9138:values|}) { }

                public static string Name(Span<string> {|FDG9138:names|}) => names[0].Trim();
            }
            """);
    }

    [Fact]
    public Task Uses_that_may_write_are_not_reported()
    {
        return BufferReferences.VerifyAsync<SpanParameterAnalyzer>(
            """
            using System;

            public struct Point
            {
                public int X;
            }

            public static class Writers
            {
                public static void Set(Span<int> values) => values[0] = 1;

                public static void Add(Span<int> values) => values[0] += 1;

                public static void Increment(Span<int> values) => values[0]++;

                public static void Clear(Span<int> values) => values.Clear();

                public static void Fill(Span<int> values) => values.Fill(0);

                public static void Pass(Span<int> values) => Set(values);

                public static void Part(Span<int> values) => Set(values.Slice(1));

                public static void Swap(Span<int> values) => Exchange(ref values[0], ref values[1]);

                public static void Reverse(Span<int> values) => values.Reverse();

                public static void Move(Span<Point> points) => points[0].X = 1;

                public static int Peek(Span<Point> points) => points[0].X;

                public static void Alias(Span<int> values)
                {
                    ref int first = ref values[0];
                    first = 1;
                }

                public static void Each(Span<int> values)
                {
                    foreach (ref int value in values)
                    {
                        value = 0;
                    }
                }

                public static void Reassign(Span<int> values)
                {
                    values = default;
                }

                private static void Exchange(ref int left, ref int right) { }
            }
            """);
    }

    [Fact]
    public Task Contracts_and_hidden_methods_are_not_reported()
    {
        return BufferReferences.VerifyAsync<SpanParameterAnalyzer>(
            """
            using System;

            public interface ISummer
            {
                int Sum(Span<int> values);
            }

            public abstract class Base : ISummer
            {
                public int Sum(Span<int> values) => values.Length;

                public virtual int Count(Span<int> values) => values.Length;

                public abstract int Size(Span<int> values);
            }

            public class Derived : Base
            {
                public override int Count(Span<int> values) => values.Length;

                public override int Size(Span<int> values) => values.Length;
            }

            public static class Other
            {
                public static int ByRef(ref Span<int> values) => values.Length;

                internal static int Hidden(Span<int> values) => values.Length;

                public static int Read(ReadOnlySpan<int> values) => values.Length;
            }
            """);
    }

    [Fact]
    public Task Primary_constructor_parameters_are_not_reported()
    {
        return AnalyzerVerifier<SpanParameterAnalyzer>.VerifyAsync(
            """
            using System;

            public ref struct Cursor(Span<int> values)
            {
                private Span<int> _values = values;

                public void Clear() => _values.Clear();
            }
            """,
            BufferReferences.Net100,
            LanguageVersion.Latest);
    }

    [Fact]
    public Task Book_trim_returning_a_slice_of_the_parameter_is_reported()
    {
        return BufferReferences.VerifyNet100Async<SpanParameterAnalyzer>(
            """
            using System;

            public static class Trimming
            {
                public static ReadOnlySpan<char> {|FDG9142:Trim|}(ReadOnlySpan<char> span)
                {
                    int end = span.Length - 1;
                    while (end >= 0 && char.IsWhiteSpace(span[end]))
                    {
                        end--;
                    }

                    int start = 0;
                    while (start < end && char.IsWhiteSpace(span[start]))
                    {
                        start++;
                    }

                    return span.Slice(start, end - start + 1);
                }

                public static ReadOnlySpan<char> {|FDG9142:TrimEnd|}(ReadOnlySpan<char> span)
                {
                    int index = span.Length - 1;
                    while (index >= 0 && char.IsWhiteSpace(span[index]))
                    {
                        index--;
                    }

                    return span.Slice(0, index + 1);
                }

                public static Span<char> {|FDG9142:TrimEnd|}(Span<char> span)
                {
                    if (span.IsEmpty)
                    {
                        return span;
                    }

                    return span.Slice(0, span.Length - 1);
                }

                public static ReadOnlySpan<char> Same(ReadOnlySpan<char> span) => span;

                public static ReadOnlySpan<char> Other(Span<char> span) => span.Slice(1);

                public static ReadOnlySpan<char> Pick(ReadOnlySpan<char> span, bool all) => all ? span : span.Slice(1);

                public static Range Bounds(ReadOnlySpan<char> span) => new Range(0, span.Length);

                internal static ReadOnlySpan<char> Hidden(ReadOnlySpan<char> span) => span.Slice(1);
            }
            """);
    }

    [Fact]
    public Task Range_indexer_slices_are_reported()
    {
        return AnalyzerVerifier<SpanParameterAnalyzer>.VerifyAsync(
            """
            using System;

            public static class Trimming
            {
                public static ReadOnlySpan<char> {|FDG9142:Rest|}(ReadOnlySpan<char> span) => span[1..];
            }
            """,
            BufferReferences.Net100,
            LanguageVersion.Latest);
    }

    [Fact]
    public Task Slices_are_not_reported_without_System_Range()
    {
        return AnalyzerVerifier<SpanParameterAnalyzer>.VerifyAsync(
            """
            using System;

            public static class Trimming
            {
                public static ReadOnlySpan<char> Rest(ReadOnlySpan<char> span) => span.Slice(1);
            }
            """,
            BufferReferences.NetStandard20WithMemory,
            LanguageVersion.CSharp7_3);
    }

    [Fact]
    public Task Messages_name_the_parameter_and_the_method()
    {
        return BufferReferences.VerifyNet100Async<SpanParameterAnalyzer>(
            """
            using System;

            public static class Api
            {
                public static int Count(Span<byte> {|#0:data|}) => data.Length;

                public static ReadOnlySpan<int> {|#1:Rest|}(ReadOnlySpan<int> values) => values.Slice(1);
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9138).WithLocation(0).WithArguments("data", "Api.Count", "byte"),
            new DiagnosticResult(FdgDescriptors.FDG9142).WithLocation(1).WithArguments("values", "Api.Rest"));
    }
}
