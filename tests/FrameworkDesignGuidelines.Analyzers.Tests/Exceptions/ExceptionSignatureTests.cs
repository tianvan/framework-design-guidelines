using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Exceptions.ExceptionSignatureAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Exceptions;

public sealed class ExceptionSignatureTests
{
    [Fact]
    public Task Throw_options_of_public_members_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class TypeLoader
            {
                public TypeLoader(string path, bool {|FDG7007:throwIfMissing|})
                {
                }

                public Type GetType(string name, bool {|FDG7007:throwOnError|})
                {
                    return null;
                }

                public static string Find(string name, int start, bool {|FDG7007:throwIfNotFound|} = true)
                {
                    return name;
                }

                public string this[string key, bool {|FDG7007:throwOnMissing|}]
                {
                    get { return key; }
                }

                protected void Load(bool {|FDG7007:throwOnFailure|})
                {
                }
            }
            """);
    }

    [Fact]
    public Task Other_parameters_and_members_that_are_not_public_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public interface ILoader
            {
                Type Load(string name, bool {|FDG7007:throwOnError|});
            }

            public abstract class LoaderBase
            {
                public abstract void Run(bool {|FDG7007:throwOnError|});
            }

            public class Loader : LoaderBase, ILoader
            {
                public Type Load(string name, bool throwOnError)
                {
                    return null;
                }

                public override void Run(bool throwOnError)
                {
                }

                public void Write(bool flush, bool thrown, int throwCount, bool overthrow)
                {
                }

                internal void Read(bool throwOnError)
                {
                }

                private void Skip(bool throwOnError)
                {
                }
            }

            internal class Hidden
            {
                public void Load(bool throwOnError)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Public_methods_that_return_exceptions_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.IO;

            public class Worker
            {
                public Exception {|FDG7008:DoSomething|}()
                {
                    return null;
                }

                public static IOException {|FDG7008:CreateError|}(string path)
                {
                    return new IOException(path);
                }

                public bool {|FDG7008:TryRun|}(out Exception error)
                {
                    error = null;
                    return true;
                }

                protected ArgumentException {|FDG7008:Check|}(int value)
                {
                    return null;
                }
            }

            public static class ExceptionExtensions
            {
                public static Exception {|FDG7008:Root|}(this Exception exception)
                {
                    return exception;
                }
            }
            """);
    }

    [Fact]
    public Task Builder_methods_properties_and_overrides_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class FileReader
            {
                public Exception LastError { get; private set; }

                public void Read(ref Exception error, Exception previous)
                {
                }

                internal static Exception NewReadException(string path)
                {
                    return new InvalidOperationException(path);
                }

                private static Exception NewWriteException(string path)
                {
                    return new InvalidOperationException(path);
                }
            }

            public class WrapperException : Exception
            {
                public override Exception GetBaseException()
                {
                    return this;
                }
            }

            internal class Hidden
            {
                public Exception Create()
                {
                    return null;
                }
            }
            """);
    }

    [Fact]
    public Task Messages_name_the_parameter_and_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public class TypeLoader
            {
                public TypeLoader(bool {|#0:throwIfMissing|})
                {
                }

                public Exception {|#1:Create|}()
                {
                    return null;
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG7007).WithLocation(0).WithArguments("throwIfMissing", "TypeLoader"),
            new DiagnosticResult(FdgDescriptors.FDG7008).WithLocation(1).WithArguments("Create"));
    }
}
