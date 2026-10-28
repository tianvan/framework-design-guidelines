using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Types.ValueTypeEquatableAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Types;

public sealed class ValueTypeEquatableTests
{
    [Fact]
    public Task Visible_struct_without_IEquatable_of_itself_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public struct {|FDG4032:Plain|}
            {
                public int Value;
            }

            public struct {|FDG4032:Other|} : IEquatable<int>
            {
                public bool Equals(int other) => false;
            }

            public class Outer
            {
                public struct {|FDG4032:Nested|}
                {
                }
            }
            """);
    }

    [Fact]
    public Task Generic_struct_is_named_with_its_type_parameters()
    {
        return Verifier.VerifyWithAsync(
            """
            public struct {|#0:Gen2|}<T>
            {
                public T Value;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG4032).WithLocation(0).WithArguments("Gen2<T>"));
    }

    [Fact]
    public Task Equatable_hidden_and_reference_types_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public struct ZipCode : IEquatable<ZipCode>
            {
                public bool Equals(ZipCode other) => true;
            }

            public struct Wrapper<T> : IEquatable<Wrapper<T>>
            {
                public bool Equals(Wrapper<T> other) => true;
            }

            internal struct Hidden
            {
            }

            public class Reference
            {
            }

            public enum Kind
            {
                None,
            }
            """);
    }

    [Fact]
    public Task Record_structs_and_ref_structs_are_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public readonly record struct Point(int X, int Y);

            public ref struct Buffer
            {
            }
            """);
    }
}
