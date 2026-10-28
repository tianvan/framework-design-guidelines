using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Patterns.AsyncEnumeratorAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class AsyncEnumeratorTests
{
    [Fact]
    public Task Async_enumerator_in_a_signature_is_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            public class Feed
            {
                public IAsyncEnumerator<int> {|FDG9047:Cursor|} { get; }

                public IAsyncEnumerator<int> {|FDG9047:Field|};

                public void Consume(IAsyncEnumerator<int> {|FDG9047:enumerator|})
                {
                }

                public Task<IAsyncEnumerator<int>> {|FDG9047:OpenAsync|}() => null;

                public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => null;

                public IEnumerable<IAsyncEnumerator<int>> Many() => null;

                private IAsyncEnumerator<int> Hidden() => null;
            }

            public interface ISource
            {
                IAsyncEnumerator<int> {|FDG9047:Next|}();
            }

            public class Source : ISource
            {
                public IAsyncEnumerator<int> Next() => null;
            }
            """);
    }

    [Fact]
    public Task Public_type_implementing_both_interfaces_is_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            public class {|FDG9048:Both|} : IAsyncEnumerable<int>, IAsyncEnumerator<int>
            {
                public int Current => 0;

                public ValueTask DisposeAsync() => default;

                public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

                public ValueTask<bool> MoveNextAsync() => default;
            }

            public class Derived : Both
            {
            }

            internal class Hidden : IAsyncEnumerable<int>, IAsyncEnumerator<int>
            {
                public int Current => 0;

                public ValueTask DisposeAsync() => default;

                public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

                public ValueTask<bool> MoveNextAsync() => default;
            }

            public class Values
            {
                public static async IAsyncEnumerable<int> ReadAsync()
                {
                    await Task.Yield();
                    yield return 1;
                }
            }
            """);
    }

    [Fact]
    public Task Nothing_is_reported_without_the_async_interfaces()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections;

            public class Holder
            {
                public IEnumerator Cursor { get; }
            }
            """);
    }
}
