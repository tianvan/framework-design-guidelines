using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Exceptions.InnerExceptionAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Exceptions;

public sealed class InnerExceptionTests
{
    [Fact]
    public Task Wrapping_exceptions_without_the_inner_exception_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.IO;

            public class TransactionFileMissingException : Exception
            {
                public TransactionFileMissingException(string message)
                    : base(message)
                {
                }

                public TransactionFileMissingException(string message, Exception inner)
                    : base(message, inner)
                {
                }
            }

            public class Store
            {
                public string Load(string path)
                {
                    try
                    {
                        return File.ReadAllText(path);
                    }
                    catch (FileNotFoundException e)
                    {
                        throw {|FDG7036:new TransactionFileMissingException(e.Message)|};
                    }
                    catch (IOException)
                    {
                        throw {|FDG7036:new InvalidOperationException("The store cannot be read.")|};
                    }
                    catch
                    {
                        try
                        {
                            Console.WriteLine(path);
                        }
                        finally
                        {
                            Console.WriteLine(path);
                        }

                        return path ?? throw {|FDG7036:new InvalidDataException("No path.")|};
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Throws_that_pass_an_exception_or_cannot_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.IO;

            public class NoInnerException : Exception
            {
                public NoInnerException(string message)
                    : base(message)
                {
                }
            }

            public class Store
            {
                public string Load(string path)
                {
                    try
                    {
                        return File.ReadAllText(path);
                    }
                    catch (FileNotFoundException e)
                    {
                        throw new InvalidOperationException("The store cannot be read.", e);
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        throw new InvalidOperationException("The store cannot be read.", e.GetBaseException());
                    }
                    catch (PathTooLongException e)
                    {
                        string Local() => throw new InvalidOperationException("Later.");

                        Exception error = new InvalidOperationException("Not thrown.");
                        Action later = () => throw new InvalidOperationException("Later.");
                        Func<string> local = Local;
                        Console.WriteLine(error.Message + later + local + e);
                        throw new NoInnerException("No constructor takes an inner exception.");
                    }
                    catch (IOException e)
                    {
                        try
                        {
                            return path;
                        }
                        catch (ArgumentException inner)
                        {
                            throw new InvalidOperationException("Nested.", inner);
                        }
                        finally
                        {
                            Console.WriteLine(e);
                        }
                    }
                    catch (Exception) when (path == null)
                    {
                        throw;
                    }
                }

                public void Save(string path)
                {
                    if (path == null)
                    {
                        throw new ArgumentNullException(nameof(path));
                    }
                }
            }
            """);
    }

    [Fact]
    public Task An_omitted_optional_inner_exception_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class StoreException : Exception
            {
                public StoreException(string message, Exception inner = null)
                    : base(message, inner)
                {
                }
            }

            public class Store
            {
                public void Load(Action action)
                {
                    try
                    {
                        action();
                    }
                    catch (FormatException e)
                    {
                        throw new StoreException("The store is corrupt.", e);
                    }
                    catch (InvalidOperationException)
                    {
                        throw {|FDG7036:new StoreException("The store is busy.")|};
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Exceptions_passed_in_an_array_or_a_sequence_are_inner_exceptions()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;

            public class Batch
            {
                public void Run(Action action)
                {
                    var errors = new List<Exception>();
                    try
                    {
                        action();
                    }
                    catch (FormatException e)
                    {
                        throw new AggregateException(e);
                    }
                    catch (ArgumentException e)
                    {
                        errors.Add(e);
                        throw new AggregateException("The batch failed.", errors);
                    }
                    catch (InvalidOperationException e)
                    {
                        throw new AggregateException("The batch failed.", new Exception[] { e });
                    }
                }
            }
            """);
    }

    [Fact]
    public Task A_type_whose_inner_exception_constructor_is_not_accessible_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class SealedOffException : Exception
            {
                public SealedOffException(string message)
                    : base(message)
                {
                }

                private SealedOffException(string message, Exception inner)
                    : base(message, inner)
                {
                }

                public static void Rethrow(Action action)
                {
                    try
                    {
                        action();
                    }
                    catch (FormatException)
                    {
                        throw {|FDG7036:new SealedOffException("Inside the type.")|};
                    }
                }
            }

            public class Store
            {
                public void Load(Action action)
                {
                    try
                    {
                        action();
                    }
                    catch (FormatException)
                    {
                        throw new SealedOffException("Outside the type.");
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_exception_type()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;
            using System.Collections.Generic;

            public class Store
            {
                public void Load(Action action)
                {
                    try
                    {
                        action();
                    }
                    catch (KeyNotFoundException)
                    {
                        throw {|#0:new System.InvalidOperationException("Missing.")|};
                    }
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG7036).WithLocation(0).WithArguments("InvalidOperationException"));
    }
}
