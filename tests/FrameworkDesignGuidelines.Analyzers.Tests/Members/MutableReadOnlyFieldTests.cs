using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.MutableReadOnlyFieldAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class MutableReadOnlyFieldTests
{
    [Fact]
    public Task Readonly_fields_of_arrays_collections_and_streams_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            using System.IO;
            using System.Text;

            public class SomeType
            {
                public static readonly int[] {|FDG5068:Numbers|} = new int[10];
                public readonly List<string> {|FDG5068:Names|} = new List<string>();
                protected readonly Dictionary<string, int> {|FDG5068:Counts|} = new Dictionary<string, int>();
                public readonly HashSet<int> {|FDG5068:Seen|} = new HashSet<int>();
                public readonly Collection<int> {|FDG5068:Items|} = new Collection<int>();
                public readonly ObservableCollection<int> {|FDG5068:Observed|} = new ObservableCollection<int>();
                public readonly ArrayList {|FDG5068:Legacy|} = new ArrayList();
                public readonly MemoryStream {|FDG5068:Buffer|} = new MemoryStream();
                public readonly Stream {|FDG5068:Output|} = Stream.Null;
                public readonly StringBuilder {|FDG5068:Text|} = new StringBuilder();
                public readonly string[][] {|FDG5068:Jagged|} = new string[0][];
            }
            """);
    }

    [Fact]
    public Task Message_names_the_field_and_its_type()
    {
        return Verifier.VerifyWithAsync(
            """
            using System.Collections.Generic;

            public class SomeType
            {
                public static readonly List<int> {|#0:Numbers|} = new List<int>();
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5068).WithLocation(0).WithArguments("Numbers", "List<int>"));
    }

    [Fact]
    public Task Immutable_writable_and_hidden_fields_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;

            public class SomeType
            {
                public static readonly string Name = "x";
                public static readonly Uri Home = new Uri("http://localhost");
                public readonly ReadOnlyCollection<int> Values = new ReadOnlyCollection<int>(new int[0]);
                public readonly IReadOnlyList<int> Items = new int[0];
                public readonly IEnumerable<int> Sequence = new int[0];
                public int[] Writable = new int[0];
                internal readonly int[] Internal = new int[0];
                private readonly List<int> _list = new List<int>();
                protected internal readonly int Count;
            }

            internal class Hidden
            {
                public readonly int[] Numbers = new int[0];
            }
            """);
    }
}
