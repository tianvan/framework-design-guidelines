using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Exceptions.CatchClauseAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Exceptions;

public sealed class CatchClauseTests
{
    [Fact]
    public Task Catches_of_StackOverflowException_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Runner
            {
                public void Run(Action action)
                {
                    try
                    {
                        action();
                    }
                    catch ({|FDG7047:StackOverflowException|})
                    {
                    }

                    try
                    {
                        action();
                    }
                    catch ({|FDG7047:System.StackOverflowException|} e) when (e.Message != null)
                    {
                        throw;
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Catches_of_TaskCanceledException_alone_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Threading.Tasks;

            public class Runner
            {
                public async Task RunAsync(Func<Task> action)
                {
                    try
                    {
                        await action();
                    }
                    catch ({|FDG7051:TaskCanceledException|})
                    {
                    }

                    try
                    {
                        await action();
                    }
                    catch ({|FDG7051:TaskCanceledException|} e) when (e.Task != null)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Other_catches_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Threading.Tasks;

            public class Runner
            {
                public async Task RunAsync(Func<Task> action)
                {
                    try
                    {
                        await action();
                    }
                    catch (TaskCanceledException e) when (e.Task != null)
                    {
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (InsufficientExecutionStackException)
                    {
                    }
                    catch (Exception e) when (e is StackOverflowException)
                    {
                        throw;
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
            """);
    }
}
