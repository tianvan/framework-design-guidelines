using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Patterns.AsyncImplementationAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class AsyncImplementationTests
{
    [Fact]
    public Task Null_task_returned_from_a_non_async_function_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Threading.Tasks;

            public class Store
            {
                private Func<Task> _callback;

                public Task SaveAsync()
                {
                    return {|FDG9036:null|};
                }

                public Task<int> CountAsync(bool cached)
                {
                    return cached ? Task.FromResult(1) : {|FDG9036:null|};
                }

                public Task<string> NameAsync() => {|FDG9036:default(Task<string>)|};

                public Task Pending
                {
                    get { return {|FDG9036:_callback?.Invoke()|}; }
                }

                public Task RunAsync()
                {
                    return Local();

                    Task Local()
                    {
                        return {|FDG9036:null|};
                    }
                }

                public async Task<string> LoadAsync()
                {
                    await Task.Yield();
                    return null;
                }

                public Task DoneAsync()
                {
                    return Task.FromResult(0);
                }

                public Func<Task> Factory()
                {
                    return () => null;
                }

                public string Text()
                {
                    return null;
                }
            }
            """);
    }

    [Fact]
    public Task Default_value_task_is_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System.Threading.Tasks;

            public class Store
            {
                public ValueTask CloseAsync() => default;

                public ValueTask<int> ReadAsync() => default;
            }
            """);
    }

    [Fact]
    public Task Task_in_the_created_state_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading.Tasks;

            public class Worker
            {
                public Task RunAsync()
                {
                    return {|FDG9037:new Task(() => { })|};
                }

                public Task<int> ComputeAsync() => {|FDG9037:new Task<int>(() => 1)|};

                public Task StartAsync()
                {
                    var task = new Task(() => { });
                    task.Start();
                    return task;
                }

                public Task QueueAsync()
                {
                    return Task.Run(() => { });
                }
            }
            """);
    }

    [Fact]
    public Task Task_created_with_a_cancellation_token_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public class Worker
            {
                public Task CanceledAsync(CancellationToken cancellationToken)
                {
                    return new Task(() => { }, cancellationToken);
                }

                public Task<int> CanceledResultAsync(CancellationToken cancellationToken) =>
                    new Task<int>(() => 1, cancellationToken);
            }
            """);
    }

    [Fact]
    public Task Early_return_on_cancellation_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public class Saver
            {
                public async Task SaveAsync(byte[] data, CancellationToken cancellationToken)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        {|FDG9038:return;|}
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                public async Task<int> CountAsync(CancellationToken cancellationToken)
                {
                    await Task.Yield();
                    if (cancellationToken.IsCancellationRequested) {|FDG9038:return 0;|}

                    Func<Task> run = async () =>
                    {
                        await Task.Yield();
                        if (cancellationToken.IsCancellationRequested) {|FDG9038:return;|}
                    };
                    await run();
                    return 1;
                }

                public bool Check(CancellationToken cancellationToken)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    return true;
                }
            }
            """);
    }

    [Fact]
    public Task Usage_error_thrown_inside_a_public_async_method_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Threading.Tasks;

            public class Saver
            {
                public async Task SaveAsync(string filename)
                {
                    if (filename == null) {|FDG9043:throw new ArgumentNullException(nameof(filename));|}

                    await Task.Yield();
                }

                protected async Task<int> CountAsync(int limit)
                {
                    if (limit < 0) {|FDG9043:throw new ArgumentOutOfRangeException(nameof(limit));|}

                    await Task.Yield();
                    return limit;
                }

                public Task WriteAsync(string filename)
                {
                    if (filename == null) throw new ArgumentNullException(nameof(filename));

                    return WriteAsyncCore(filename);
                }

                private async Task WriteAsyncCore(string filename)
                {
                    if (filename.Length == 0) throw new ArgumentException("Empty.", nameof(filename));

                    await Task.Yield();
                }

                public async Task LoadAsync(string filename)
                {
                    await Task.Yield();
                    if (filename.Length == 0) throw new InvalidOperationException();

                    Func<string, Task> check = async name =>
                    {
                        await Task.Yield();
                        if (name == null) throw new ArgumentNullException(nameof(name));
                    };
                    await check(filename);
                }
            }
            """);
    }

    [Fact]
    public Task Usage_error_after_an_await_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.IO;
            using System.Threading.Tasks;

            public class Loader
            {
                public async Task<string> LoadAsync(string id)
                {
                    if (id == null) {|FDG9043:throw new ArgumentNullException(nameof(id));|}

                    string text = await Task.FromResult(id);
                    if (text.Length == 0) throw new ArgumentException("Empty.", nameof(id));

                    return text;
                }

                public async Task<int> ParseAsync(string id)
                {
                    try
                    {
                        string text = await Task.FromResult(id);
                        return int.Parse(text);
                    }
                    catch (FormatException e)
                    {
                        throw new ArgumentException("Not a number.", nameof(id), e);
                    }
                }

                public async Task ReadAsync(Stream stream)
                {
                    await Task.Yield();
                    if (stream.Length == 0) throw new ArgumentException("Empty.", nameof(stream));
                }

                public async Task SendAsync(string text, bool later)
                {
                    if (later)
                    {
                        await Task.Yield();
                    }
                    else if (text == null)
                    {
                        throw new ArgumentNullException(nameof(text));
                    }
                }

                public async Task<int> SizeAsync(string text)
                {
                    return await Task.FromResult((text ?? throw new ArgumentNullException(nameof(text))).Length);
                }
            }
            """);
    }

    [Fact]
    public Task Usage_error_after_await_foreach_or_an_await_using_statement_is_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Threading.Tasks;

            public class Loader
            {
                public async Task<int> SumAsync(IAsyncEnumerable<int> values, int limit)
                {
                    int sum = 0;
                    await foreach (int value in values)
                    {
                        sum += value;
                    }

                    if (sum > limit) throw new ArgumentOutOfRangeException(nameof(limit));

                    return sum;
                }

                public async Task CopyAsync(Stream source, string name)
                {
                    await using var copy = new MemoryStream();
                    if (name.Length == 0) {|FDG9043:throw new ArgumentException("Empty.", nameof(name));|}
                }

                public async Task WriteAsync(Stream target, string name)
                {
                    await using (var buffer = new MemoryStream())
                    {
                        if (name == null) {|FDG9043:throw new ArgumentNullException(nameof(name));|}
                    }

                    if (name.Length == 0) throw new ArgumentException("Empty.", nameof(name));
                }
            }
            """);
    }

    [Fact]
    public Task Throw_helpers_inside_a_public_async_method_are_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public class Saver
            {
                public async Task SaveAsync(string filename)
                {
                    {|FDG9043:ArgumentNullException.ThrowIfNull(filename)|};
                    await Task.Yield();
                }

                public async IAsyncEnumerable<int> ValuesAsync(int count)
                {
                    {|FDG9043:ArgumentOutOfRangeException.ThrowIfNegative(count)|};
                    await Task.Yield();
                    yield return count;
                }

                public Task SendAsync(string to)
                {
                    ArgumentNullException.ThrowIfNull(to);
                    return Task.CompletedTask;
                }
            }
            """);
    }

    [Fact]
    public Task Async_enumerable_parameter_without_with_cancellation_is_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;

            public class Aggregator
            {
                public async Task<int> MaxAsync(
                    IAsyncEnumerable<int> source,
                    CancellationToken cancellationToken = default)
                {
                    int max = int.MinValue;
                    await foreach (int value in {|FDG9049:source.ConfigureAwait(false)|})
                    {
                        max = Math.Max(max, value);
                    }

                    await foreach (int value in {|FDG9049:source|})
                    {
                        max = Math.Max(max, value);
                    }

                    await foreach (int value in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                    {
                        max = Math.Max(max, value);
                    }

                    await foreach (int value in source.ConfigureAwait(false).WithCancellation(cancellationToken))
                    {
                        max = Math.Max(max, value);
                    }

                    await foreach (int value in Generate(cancellationToken).ConfigureAwait(false))
                    {
                        max = Math.Max(max, value);
                    }

                    return max;
                }

                public async Task<int> SumAsync(IAsyncEnumerable<int> source)
                {
                    int sum = 0;
                    await foreach (int value in source.ConfigureAwait(false))
                    {
                        sum += value;
                    }

                    Func<IAsyncEnumerable<int>, CancellationToken, Task> run = async (items, token) =>
                    {
                        await foreach (int item in {|FDG9049:items|})
                        {
                        }
                    };
                    await run(source, default);
                    return sum;
                }

                private static async IAsyncEnumerable<int> Generate(
                    [EnumeratorCancellation] CancellationToken cancellationToken)
                {
                    await Task.Yield();
                    yield return 1;
                }
            }
            """);
    }
}
