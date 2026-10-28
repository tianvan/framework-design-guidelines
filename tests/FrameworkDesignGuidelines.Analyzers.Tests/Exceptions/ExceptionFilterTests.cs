using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Exceptions.ExceptionFilterAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Exceptions;

public sealed class ExceptionFilterTests
{
    [Fact]
    public Task Throw_expressions_in_filters_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Reader
            {
                public void Read(string prefix)
                {
                    try
                    {
                        Console.WriteLine(prefix);
                    }
                    catch (ArgumentException e)
                        when (e.Message.StartsWith(prefix ?? {|FDG7010:throw new InvalidOperationException()|}))
                    {
                    }
                    catch (Exception e) when (e.InnerException == null
                        ? {|FDG7010:throw new InvalidOperationException("No inner exception.")|}
                        : true)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Filters_without_throws_and_deferred_throws_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Reader
            {
                public void Read(string prefix)
                {
                    try
                    {
                        Console.WriteLine(prefix ?? throw new ArgumentNullException(nameof(prefix)));
                    }
                    catch (ArgumentException e) when (e.InnerException.Message.StartsWith("File"))
                    {
                        throw new InvalidOperationException("Read failed.", e);
                    }
                    catch (Exception e) when (Check(e, () => throw new InvalidOperationException("Late.")))
                    {
                    }
                    catch (InvalidOperationException e)
                        when (Check(e, delegate { throw new InvalidOperationException(); }))
                    {
                    }
                }

                private static bool Check(Exception e, Func<bool> next)
                {
                    return e != null;
                }

                private static bool Check(Exception e, Action next)
                {
                    return e != null;
                }
            }
            """);
    }
}
