using FrameworkDesignGuidelines.Analyzers.Patterns;
using Microsoft.CodeAnalysis.Testing;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class BufferSignatureTests
{
    [Fact]
    public Task Span_parameters_of_task_returning_methods_are_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public interface IChannel
            {
                Task SendAsync(ReadOnlySpan<byte> {|FDG9143:data|});
            }

            public class Channel : IChannel
            {
                public Task SendAsync(ReadOnlySpan<byte> data) => Task.CompletedTask;

                public Task<int> ReadAsync(Span<byte> {|FDG9144:buffer|}, CancellationToken cancellationToken = default)
                {
                    return Task.FromResult(buffer.Length);
                }

                public Task<int> {|FDG9148:ReadAsync|}(
                    Memory<byte> buffer,
                    CancellationToken cancellationToken = default)
                {
                    return Task.FromResult(buffer.Length);
                }

                public Task WriteAsync(ReadOnlyMemory<byte> buffer) => Task.CompletedTask;

                public int Read(Span<byte> buffer) => buffer.Length;

                private Task FlushAsync(Span<char> {|FDG9144:pending|}) => Task.CompletedTask;

                public virtual Task CloseAsync(ReadOnlySpan<char> {|FDG9143:reason|}) => Task.CompletedTask;
            }

            public class Derived : Channel
            {
                public override Task CloseAsync(ReadOnlySpan<char> reason) => Task.CompletedTask;
            }
            """);
    }

    [Fact]
    public Task Span_parameters_of_async_enumerable_methods_are_reported()
    {
        return BufferReferences.VerifyNet100Async<BufferSignatureAnalyzer>(
            """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public static class Streams
            {
                public static IAsyncEnumerable<int> Values(ReadOnlySpan<int> {|FDG9143:source|}) => null;

                public static ValueTask<int> CountAsync(Span<int> {|FDG9144:values|}) => default;
            }
            """);
    }

    [Fact]
    public Task Book_overloads_across_writable_and_read_only_buffers_are_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;

            public static class Sums
            {
                public static int Sum(ReadOnlySpan<int> values) => 0;

                public static void {|FDG9147:Sum|}(Span<int> values) { }

                public static int Total(ReadOnlyMemory<int> values) => 0;

                public static int {|FDG9147:Total|}(Memory<int> values) => 0;

                public static int Count<T>(ReadOnlySpan<T> values, int start) => 0;

                public static int {|FDG9147:Count|}<T>(Span<T> values, int start) => 0;

                public static int Mixed(ReadOnlySpan<int> values, int start) => 0;

                public static int Mixed(Span<int> values, long start) => 0;

                public static int Elements(ReadOnlySpan<int> values) => 0;

                public static int Elements(Span<long> values) => 0;

                public static int Hidden(ReadOnlySpan<int> values) => 0;

                internal static int Hidden(Span<int> values) => 0;
            }
            """);
    }

    [Fact]
    public Task Book_overloads_across_spans_and_memory_are_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;

            public static class Text
            {
                public static void ProcessText(Span<char> span) { }

                public static void {|FDG9148:ProcessText|}(Memory<char> memory) { }

                public static void Parse(ReadOnlySpan<char> span) { }

                public static void {|FDG9148:Parse|}(ReadOnlyMemory<char> memory) { }

                public static void Read(ReadOnlySpan<char> span) { }

                public static void Read(Memory<char> memory) { }
            }
            """);
    }

    [Fact]
    public Task Book_trim_overloads_returning_their_input_type_are_not_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;

            public static class Trimming
            {
                public static ReadOnlySpan<char> TrimEnd(ReadOnlySpan<char> span) => span;

                public static Span<char> TrimEnd(Span<char> span) => span;

                public static Memory<char> TrimEnd(Memory<char> memory) => memory;

                public static ReadOnlySpan<char> TrimStart(ReadOnlySpan<char> span) => span;

                public static ReadOnlySpan<char> {|FDG9147:TrimStart|}(Span<char> span) => span;
            }
            """);
    }

    [Fact]
    public Task Book_source_and_destination_positions_are_not_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;

            public enum BitOperation
            {
                And,
                Or,
            }

            public class IncrementalHash
            {
                public bool TryGetHashAndReset(Span<byte> destination, out int bytesWritten)
                {
                    bytesWritten = 0;
                    return true;
                }
            }

            public static class ByteOperations
            {
                public static int XorInto(ReadOnlySpan<byte> source, Span<byte> destination) => 0;

                public static int Xor(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, Span<byte> destination) => 0;

                public static int CombineInto(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    BitOperation operation) => 0;

                public static int XorInto(byte[] source, byte[] destination) => 0;

                public static int XorInto(
                    byte[] source,
                    int sourceIndex,
                    byte[] destination,
                    int destinationIndex) => 0;

                public static int Merge(
                    ReadOnlySpan<byte> other,
                    ReadOnlySpan<byte> source,
                    Span<byte> destination) => 0;

                public static int Append(this Span<byte> buffer, ReadOnlySpan<byte> source) => 0;

                public static bool TryWriteGuid(Guid value, Span<byte> destination, out int bytesWritten)
                {
                    bytesWritten = 0;
                    return true;
                }
            }
            """);
    }

    [Fact]
    public Task Misplaced_source_and_destination_are_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;

            public static class Codec
            {
                public static int Encode(
                    Span<byte> {|FDG9157:destination|},
                    ReadOnlySpan<byte> {|FDG9156:source|}) => 0;

                public static int Write(int count, string {|FDG9156:source|}) => 0;

                public static int Copy(byte[] {|FDG9157:destination|}, int offset, byte[] {|FDG9156:source|}) => 0;

                internal static int Hidden(Span<byte> destination, ReadOnlySpan<byte> source) => 0;
            }
            """);
    }

    [Fact]
    public Task Book_try_write_methods_are_not_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;

            public class Version
            {
                public bool TryFormat(Span<char> destination, out int charsWritten)
                {
                    charsWritten = 0;
                    return true;
                }
            }

            public class Target
            {
                public bool TryWriteBytes(Span<byte> destination, out int bytesWritten)
                {
                    bytesWritten = 0;
                    return true;
                }

                public int WriteBytes(Span<byte> destination) => 0;

                public bool TryWriteValues(Span<int> destination, out int valuesWritten)
                {
                    valuesWritten = 0;
                    return true;
                }
            }
            """);
    }

    [Fact]
    public Task Try_write_methods_of_the_wrong_shape_are_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;

            public class Writer
            {
                public bool TryFormat(Span<char> destination, out int {|FDG9159:written|})
                {
                    written = 0;
                    return true;
                }

                public bool TryWriteInts(int[] destination, out long {|FDG9159:count|})
                {
                    count = 0;
                    return true;
                }

                public bool {|FDG9163:TryWriteUInt64LittleEndian|}(Span<byte> destination, ulong value) => true;

                public bool {|FDG9162:WriteBytes|}(Span<byte> destination, out int bytesWritten)
                {
                    bytesWritten = 0;
                    return true;
                }

                public int {|FDG9162:TryWriteChars|}(Span<char> destination, out int charsWritten)
                {
                    charsWritten = 0;
                    return 0;
                }

                public bool Trying(Span<char> destination) => true;

                internal bool TryHidden(Span<char> destination) => true;
            }
            """);
    }

    [Fact]
    public Task Book_operation_status_methods_are_not_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;
            using System.Buffers;

            public static class Lists
            {
                public static OperationStatus ReadInt32List(
                    ReadOnlySpan<char> source,
                    Span<int> destination,
                    out int charsConsumed,
                    out int valuesWritten)
                {
                    charsConsumed = 0;
                    valuesWritten = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus ToBase64(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    out int bytesConsumed,
                    out int charsWritten,
                    bool isFinalBlock = true)
                {
                    bytesConsumed = 0;
                    charsWritten = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus Flush(Span<byte> destination, out int bytesWritten)
                {
                    bytesWritten = 0;
                    return OperationStatus.Done;
                }
            }
            """);
    }

    [Fact]
    public Task Operation_status_methods_of_the_wrong_shape_are_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;
            using System.Buffers;

            public static class Decoder
            {
                public static OperationStatus {|FDG9171:{|FDG9173:Decode|}|}(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination) => OperationStatus.Done;

                public static OperationStatus {|FDG9171:Expand|}(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    out int bytesWritten)
                {
                    bytesWritten = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus {|FDG9173:Inflate|}(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    out int bytesConsumed)
                {
                    bytesConsumed = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus {|FDG9173:Swap|}(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    out int bytesWritten,
                    out int bytesConsumed)
                {
                    bytesWritten = 0;
                    bytesConsumed = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus Rename(
                    ReadOnlySpan<byte> source,
                    Span<char> destination,
                    out int {|FDG9172:consumed|},
                    out int {|FDG9159:written|})
                {
                    consumed = 0;
                    written = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus Parse(
                    string source,
                    Span<int> destination,
                    out int {|FDG9172:valuesConsumed|},
                    out int valuesWritten)
                {
                    valuesConsumed = 0;
                    valuesWritten = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus Measure(ReadOnlySpan<byte> source, out int count)
                {
                    count = 0;
                    return OperationStatus.Done;
                }

                internal static OperationStatus Hidden(ReadOnlySpan<byte> source, Span<byte> destination) =>
                    OperationStatus.Done;
            }
            """);
    }

    [Fact]
    public Task Final_block_flags_of_the_wrong_shape_are_reported()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;
            using System.Buffers;

            public static class Blocks
            {
                public static OperationStatus Required(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    out int bytesConsumed,
                    out int bytesWritten,
                    bool {|FDG9174:isFinalBlock|})
                {
                    bytesConsumed = 0;
                    bytesWritten = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus False(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    out int bytesConsumed,
                    out int bytesWritten,
                    bool {|FDG9174:isFinalBlock|} = false)
                {
                    bytesConsumed = 0;
                    bytesWritten = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus Named(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    out int bytesConsumed,
                    out int bytesWritten,
                    bool {|FDG9174:isLast|} = true)
                {
                    bytesConsumed = 0;
                    bytesWritten = 0;
                    return OperationStatus.Done;
                }

                public static OperationStatus Flag(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    out int bytesConsumed,
                    out int bytesWritten,
                    bool flush)
                {
                    bytesConsumed = 0;
                    bytesWritten = 0;
                    return OperationStatus.Done;
                }

                public static int Other(ReadOnlySpan<byte> source, bool isLast) => 0;
            }
            """);
    }

    [Fact]
    public Task Messages_name_the_parameters_and_the_method()
    {
        return BufferReferences.VerifyAsync<BufferSignatureAnalyzer>(
            """
            using System;
            using System.Buffers;
            using System.Threading.Tasks;

            public static class Api
            {
                public static Task SendAsync(ReadOnlySpan<byte> {|#0:data|}) => Task.CompletedTask;

                public static int Sum(ReadOnlySpan<int> values) => 0;

                public static int {|#1:Sum|}(Span<int> values) => 0;

                public static int Encode(Span<byte> {|#2:destination|}, ReadOnlySpan<byte> {|#3:source|}) => 0;

                public static bool TryFormat(Span<char> destination, out int {|#4:written|})
                {
                    written = 0;
                    return true;
                }

                public static OperationStatus Decode(
                    ReadOnlySpan<byte> source,
                    Span<byte> destination,
                    out int {|#6:consumed|},
                    out int bytesWritten,
                    bool {|#7:last|} = true)
                {
                    consumed = 0;
                    bytesWritten = 0;
                    return OperationStatus.Done;
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9143).WithLocation(0)
                .WithArguments("byte", "data", "Api.SendAsync"),
            new DiagnosticResult(FdgDescriptors.FDG9147).WithLocation(1)
                .WithArguments("Api.Sum", "Span<int>", "ReadOnlySpan<int>"),
            new DiagnosticResult(FdgDescriptors.FDG9157).WithLocation(2)
                .WithArguments("destination", "Api.Encode", "source"),
            new DiagnosticResult(FdgDescriptors.FDG9156).WithLocation(3).WithArguments("source", "Api.Encode"),
            new DiagnosticResult(FdgDescriptors.FDG9159).WithLocation(4)
                .WithArguments("written", "Api.TryFormat", "charsWritten"),
            new DiagnosticResult(FdgDescriptors.FDG9172).WithLocation(6)
                .WithArguments("consumed", "Api.Decode", "bytesConsumed"),
            new DiagnosticResult(FdgDescriptors.FDG9174).WithLocation(7).WithArguments("last", "Api.Decode"));
    }
}
