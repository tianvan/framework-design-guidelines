using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.RangeIndexerAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class RangeIndexerTests
{
    private const string Violations =
        """
        using System;

        public class SomeValue
        {
        }

        public class WrongIndexer
        {
            public SomeValue[] {|FDG5040:this|}[Range range] => null;
        }

        public class WrongSlice
        {
            public int Count => 0;

            public SomeValue[] {|FDG5040:Slice|}(int offset, int length) => null;
        }

        public struct WrongLengthSlice
        {
            public int Length => 0;

            public int[] {|FDG5040:Slice|}(int start, int length) => null;
        }
        """;

    [Theory]
    [InlineData(LanguageVersion.CSharp7_3)]
    [InlineData(LanguageVersion.Latest)]
    public Task Range_indexer_or_slice_returning_another_type_is_reported(LanguageVersion languageVersion)
    {
        // System.Range exists from .NET Core 3.0, so .NET Framework has neither kind of range indexer.
        return Verifier.VerifyAsync(Violations, ReferenceAssemblies.Net.Net80, languageVersion);
    }

    [Fact]
    public Task Message_names_the_declaring_type_and_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Items<T>
            {
                public int Count => 0;

                public T[] {|#0:Slice|}(int offset, int length) => null;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5040).WithLocation(0).WithArguments("Items<T>", "Slice"));
    }

    [Fact]
    public Task Declaring_type_and_members_that_are_no_range_indexer_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class SomeCollection<T>
            {
                public int Count => 0;

                public SomeCollection<T> Slice(int offset, int length) => this;

                public T this[int index] => default(T);
            }

            public class NoCount
            {
                public int[] Slice(int offset, int length) => null;
            }

            public class OtherSlice
            {
                public int Length => 0;

                public int[] Slice(int offset) => null;

                public static int[] Slice(int offset, int length) => null;

                internal OtherSlice Slice(long offset, long length) => null;
            }

            internal class Hidden
            {
                public int Count => 0;

                public int[] Slice(int offset, int length) => null;
            }
            """);
    }

    [Fact]
    public Task Range_indexer_returning_the_declaring_type_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class SomeCollection
            {
                public int Count => 0;

                public SomeCollection this[Range range] => this;

                public int[] Slice(int offset, int length) => null;
            }

            public class HiddenIndexer
            {
                internal int[] this[Range range] => null;
            }
            """,
            ReferenceAssemblies.Net.Net80,
            LanguageVersion.Latest);
    }
}
