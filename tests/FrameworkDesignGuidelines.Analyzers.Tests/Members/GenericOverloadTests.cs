using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.GenericOverloadAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class GenericOverloadTests
{
    [Fact]
    public Task Overload_on_a_type_parameter_and_a_specific_type_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class PrettyPrinter<T>
            {
                public PrettyPrinter(string name)
                {
                }

                public PrettyPrinter(T {|FDG5008:prototype|})
                {
                }

                public void PrettyPrint(string format)
                {
                }

                public void PrettyPrint(T {|FDG5008:otherPrinter|})
                {
                }

                public void PrettyPrint(T otherPrinter, string format)
                {
                }

                public class Nested
                {
                    public void Add(int value)
                    {
                    }

                    public void Add(T {|FDG5008:value|})
                    {
                    }
                }
            }

            public class Constrained<T>
                where T : class
            {
                public void Add(string value)
                {
                }

                public void Add(T {|FDG5008:value|})
                {
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_type_parameter_and_the_type()
    {
        return Verifier.VerifyWithAsync(
            """
            using System.Collections.Generic;

            public class Printer<T>
            {
                public void Print(List<int> items)
                {
                }

                public void Print(T {|#0:items|})
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5008).WithLocation(0).WithArguments("Print", "T", "List<int>"));
    }

    [Fact]
    public Task Overloads_that_can_never_collide_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class ValueOnly<T>
                where T : struct
            {
                public void Add(string value)
                {
                }

                public void Add(T value)
                {
                }
            }

            public class Comparable<T>
                where T : IComparable<T>
            {
                public void Add(string value)
                {
                }

                public void Add(T value)
                {
                }
            }

            public class Disposable<T>
                where T : IDisposable
            {
                public void Add(string value)
                {
                }

                public void Add(T value)
                {
                }
            }

            public class Different<T>
            {
                public void Add(string value, int count)
                {
                }

                public void Add(T value, string count)
                {
                }

                public void Remove(T value)
                {
                }

                public void Remove(T value, int count)
                {
                }

                public void Method<TOther>(TOther value)
                {
                }

                public void Method<TOther>(string value)
                {
                }

                internal void Hidden(string value)
                {
                }

                public void Hidden(T value)
                {
                }
            }

            public class NotGeneric
            {
                public void Add(string value)
                {
                }

                public void Add(object value)
                {
                }
            }
            """);
    }
}
