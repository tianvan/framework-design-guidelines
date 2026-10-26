using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Patterns.AsyncSignatureAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class AsyncSignatureTests
{
    [Fact]
    public Task Task_returning_method_without_the_suffix_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public class Parser
            {
                public Task {|FDG9020:Save|}(CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public Task<int> {|FDG9020:Count|}(CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                protected virtual Task {|FDG9020:WriteCore|}(CancellationToken cancellationToken)
                {
                    return Task.FromResult(0);
                }

                public Task SaveAsync(CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                protected virtual Task WriteAsyncCore(CancellationToken cancellationToken)
                {
                    return Task.FromResult(0);
                }

                internal Task Load()
                {
                    return Task.FromResult(0);
                }

                private Task Flush()
                {
                    return Task.FromResult(0);
                }

                public int Parse()
                {
                    return 0;
                }
            }
            """);
    }

    [Fact]
    public Task Overrides_and_interface_implementations_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public interface IRunner
            {
                Task {|FDG9020:Run|}(CancellationToken cancellationToken = default(CancellationToken));
            }

            public abstract class Base
            {
                public abstract Task {|FDG9020:Go|}(CancellationToken cancellationToken = default(CancellationToken));
            }

            public class Derived : Base, IRunner
            {
                public override Task Go(CancellationToken cancellationToken) { return Task.FromResult(0); }

                public Task Run(CancellationToken cancellationToken) { return Task.FromResult(0); }
            }
            """);
    }

    [Fact]
    public Task Entry_point_is_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System.Threading.Tasks;

            public static class Program
            {
                public static async Task Main()
                {
                    await Task.Yield();
                }
            }
            """);
    }

    [Fact]
    public Task Value_task_and_async_enumerable_methods_are_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;

            public class Source
            {
                public ValueTask {|FDG9020:Close|}(CancellationToken cancellationToken = default) => default;

                public ValueTask<int> {|FDG9020:Read|}(CancellationToken cancellationToken = default) => default;

                public Pending {|FDG9020:Wait|}(CancellationToken cancellationToken = default) => default;

                public async IAsyncEnumerable<int> {|FDG9045:Values|}(
                    [EnumeratorCancellation] CancellationToken cancellationToken = default)
                {
                    await Task.Yield();
                    yield return 1;
                }

                public async IAsyncEnumerable<int> ValuesAsync(
                    [EnumeratorCancellation] CancellationToken cancellationToken = default)
                {
                    await Task.Yield();
                    yield return 1;
                }

                protected virtual IAsyncEnumerable<int> ValuesAsyncCore() => null;
            }

            [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder))]
            public struct Pending
            {
            }
            """);
    }

    [Fact]
    public Task Missing_or_misnamed_cancellation_token_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public class Writer
            {
                public Task {|FDG9022:WriteAsync|}(string text)
                {
                    return Task.FromResult(0);
                }

                public Task FlushAsync(CancellationToken {|FDG9022:token|} = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public Task CloseAsync(CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public Task<int> ReadAsync(byte[] buffer)
                {
                    return ReadAsync(buffer, CancellationToken.None);
                }

                public Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
                {
                    return Task.FromResult(0);
                }

                protected virtual Task DisposeAsyncCore()
                {
                    return Task.FromResult(0);
                }

                private Task SendAsync()
                {
                    return Task.FromResult(0);
                }
            }
            """);
    }

    [Fact]
    public Task Cancellation_token_without_default_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;

            public class Writer
            {
                public Task SaveAsync(CancellationToken {|FDG9023:cancellationToken|})
                {
                    return Task.FromResult(0);
                }

                public Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public Task WriteAsync(string text, CancellationToken cancellationToken)
                {
                    return Task.FromResult(0);
                }

                public Task WriteAsync(string text, Encoding encoding, CancellationToken cancellationToken)
                {
                    return Task.FromResult(0);
                }

                public Task SendAsync(CancellationToken cancellationToken, string text)
                {
                    return Task.FromResult(0);
                }

                protected virtual Task SaveAsyncCore(CancellationToken cancellationToken)
                {
                    return Task.FromResult(0);
                }

                public void Add(int item, CancellationToken cancellationToken)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Ref_and_out_parameters_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public class Parser
            {
                public Task<bool> TryParseAsync(
                    string text,
                    out int {|FDG9027:value|},
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    value = 0;
                    return Task.FromResult(true);
                }

                public Task UpdateAsync(
                    ref string {|FDG9027:value|},
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public bool TryParse(string text, out int value)
                {
                    value = 0;
                    return true;
                }
            }
            """);
    }

    [Fact]
    public Task In_parameters_are_reported_by_virtual_kind()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public struct Mutable
            {
                public int Value;
            }

            public readonly struct Frozen
            {
                public Frozen(int value) { Value = value; }

                public int Value { get; }
            }

            public interface IStore
            {
                Task SaveAsync(
                    in Mutable {|FDG9028:item|},
                    CancellationToken cancellationToken = default(CancellationToken));
            }

            public abstract class Store
            {
                public abstract Task WriteAsync(
                    in Frozen {|FDG9028:item|},
                    CancellationToken cancellationToken = default(CancellationToken));

                public virtual Task FlushAsync(
                    in Mutable {|FDG9028:item|},
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public Task SendAsync(
                    in Mutable {|FDG9029:item|},
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public Task PostAsync(in Frozen item, CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public void Write(in Mutable item)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Ref_readonly_parameters_are_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public struct Mutable
            {
                public int Value;
            }

            public class Store
            {
                public Task SendAsync(
                    ref readonly Mutable {|FDG9029:item|},
                    CancellationToken cancellationToken = default)
                {
                    return Task.CompletedTask;
                }

                public virtual Task PostAsync(
                    ref readonly Mutable {|FDG9028:item|},
                    CancellationToken cancellationToken = default)
                {
                    return Task.CompletedTask;
                }
            }
            """);
    }

    [Fact]
    public Task Parameter_order_differing_from_the_synchronous_variant_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            public class Document
            {
                public static Document Load(TextReader textReader, int options) { return null; }

                public static Task<Document> {|FDG9033:LoadAsync|}(
                    int options,
                    TextReader textReader,
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult<Document>(null);
                }

                public void Save(TextWriter writer, int options) { }

                public Task SaveAsync(
                    TextWriter writer,
                    int options,
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public int Divide(int divisor, int dividend, out int remainder) { remainder = 0; return 0; }

                public Task<int> DivideAsync(
                    int divisor,
                    int dividend,
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public void Copy(string source, string target, CancellationToken cancellationToken) { }

                public Task CopyAsync(
                    string source,
                    string target,
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }

                public static void Send(string to, string body) { }

                public Task SendAsync(
                    string body,
                    string to,
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }
            }
            """);
    }

    [Fact]
    public Task Parameter_order_matching_one_synchronous_overload_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public class Mailer
            {
                public void Send(string to, string body) { }

                public void Send(string body, string to, int priority) { }

                public Task SendAsync(
                    string to,
                    string body,
                    CancellationToken cancellationToken = default(CancellationToken))
                {
                    return Task.FromResult(0);
                }
            }
            """);
    }

    [Fact]
    public Task Static_method_returning_only_completed_tasks_is_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Results
            {
                public static ValueTask<bool> True() => new(true);

                public static ValueTask<int> Zero() => new ValueTask<int>(0);

                public static ValueTask Done() => default;

                public static Task<int> FromValue(int value) => Task.FromResult(value);

                public static Task Fail(Exception error, bool canceled)
                {
                    if (canceled)
                    {
                        return Task.FromCanceled(new CancellationToken(true));
                    }

                    return error is null ? Task.CompletedTask : Task.FromException(error);
                }

                public static ValueTask<int> {|FDG9020:{|FDG9022:Wrap|}|}(Task<int> task) => new(task);

                public static Task {|FDG9020:{|FDG9022:Later|}|}()
                {
                    Func<Task> start = () => Task.CompletedTask;
                    return start();
                }

                public static async Task {|FDG9020:{|FDG9022:Wait|}|}()
                {
                    await Task.Yield();
                }
            }

            public class Values
            {
                public Task<int> {|FDG9020:{|FDG9022:Read|}|}() => Task.FromResult(0);
            }
            """);
    }

    [Fact]
    public Task Message_names_the_method_and_parameter()
    {
        return Verifier.VerifyWithAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public class Writer
            {
                public Task Write(out int count, CancellationToken cancellationToken)
                {
                    count = 0;
                    return Task.FromResult(0);
                }
            }
            """,
            new DiagnosticResult("FDG9020", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(6, 17, 6, 22)
                .WithArguments("Write"),
            new DiagnosticResult("FDG9023", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(6, 56, 6, 73)
                .WithArguments("cancellationToken", "Write"),
            new DiagnosticResult("FDG9027", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithSpan(6, 31, 6, 36)
                .WithArguments("count", "out", "Write"));
    }
}
