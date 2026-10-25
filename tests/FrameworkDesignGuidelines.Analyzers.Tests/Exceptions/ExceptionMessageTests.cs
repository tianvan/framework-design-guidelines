using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Exceptions.ExceptionMessageAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Exceptions;

public sealed class ExceptionMessageTests
{
    [Fact]
    public Task Messages_without_a_final_period_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class ReadException : Exception
            {
                private const string Default = "The read failed";

                public ReadException()
                    : base({|FDG7021:Default|})
                {
                }

                public ReadException(string message)
                    : base(message)
                {
                }
            }

            public class Reader
            {
                public void Read(string path, int count)
                {
                    if (count < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(count), {|FDG7021:"The count is negative"|});
                    }

                    if (path.Length == 0)
                    {
                        throw new ArgumentException({|FDG7021:$"The path is empty for {count} bytes"|}, nameof(path));
                    }

                    throw new InvalidOperationException(message: {|FDG7021:"Read failed. Try again later "|});
                }
            }
            """);
    }

    [Fact]
    public Task Messages_with_a_final_period_or_built_at_run_time_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Reader
            {
                public void Read(string path, int count, string text)
                {
                    if (count < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(count), "The count is negative. ");
                    }

                    if (count == 0)
                    {
                        throw new ArgumentNullException("path");
                    }

                    if (path.Length == 0)
                    {
                        throw new ArgumentException($"The path is empty: {path}", nameof(path));
                    }

                    if (text == null)
                    {
                        throw new InvalidOperationException("Read failed: " + path);
                    }

                    throw new InvalidOperationException(string.Format("Read {0} failed", path));
                }

                public void Write(string message)
                {
                    Console.WriteLine("No period");
                    Write("No period either");
                }
            }
            """);
    }

    [Fact]
    public Task Question_marks_and_exclamation_points_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Reader
            {
                public void Read(string path)
                {
                    if (path == null)
                    {
                        throw new ArgumentNullException(nameof(path), {|FDG7022:"Did you forget the path?"|});
                    }

                    if (path.Length == 0)
                    {
                        throw new ArgumentException({|FDG7022:$"'{path}' is empty! Use a file."|}, nameof(path));
                    }

                    throw new InvalidOperationException(
                        {|FDG7022:"Read failed!"|});
                }
            }
            """);
    }

    [Fact]
    public Task Operators_in_quoted_expressions_are_not_counted()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Reader
            {
                public void Read(string path)
                {
                    throw new InvalidOperationException(
                        "The condition 'path != null' failed, so 'path?.Length ?? 0' is 0, and 'items?[0]' too.");
                }
            }
            """);
    }
}
