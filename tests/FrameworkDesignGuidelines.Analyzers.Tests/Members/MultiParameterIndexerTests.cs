using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.MultiParameterIndexerAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class MultiParameterIndexerTests
{
    [Fact]
    public Task Indexer_with_more_than_one_parameter_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class Grid
            {
                public int {|FDG5034:this|}[int row, int column] => 0;
            }

            public interface ITable
            {
                string {|FDG5034:this|}[string row, string column, int index] { get; }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_type()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Grid<T>
            {
                public T {|#0:this|}[int row, int column] => default(T);
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5034).WithLocation(0).WithArguments("Grid<T>"));
    }

    [Fact]
    public Task Single_parameter_hidden_and_implementing_indexers_are_not_reported_but_the_interface_is()
    {
        return Verifier.VerifyAsync(
            """
            public interface ITable
            {
                string {|FDG5034:this|}[string row, string column] { get; }
            }

            public class Table : ITable
            {
                public string this[string row, string column] => null;

                public string this[int index] => null;
            }

            public class Explicit : ITable
            {
                string ITable.this[string row, string column] => null;
            }

            internal class Hidden
            {
                public int this[int row, int column] => 0;
            }

            public class Matrix
            {
                internal int this[int row, int column] => 0;
            }
            """);
    }
}
