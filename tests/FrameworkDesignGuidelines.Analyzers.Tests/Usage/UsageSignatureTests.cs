using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Usage.UsageSignatureAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Usage;

public sealed class UsageSignatureTests
{
    [Fact]
    public Task Visible_read_only_array_fields_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.ObjectModel;

            public sealed class Path
            {
                public static readonly char[] {|FDG8002:InvalidPathChars|} = { '"', '<', '>', '|' };

                private static readonly char[] badChars = { '"', '<', '>', '|' };

                public static ReadOnlyCollection<char> GetInvalidPathChars()
                {
                    return Array.AsReadOnly(badChars);
                }

                public static char[] GetInvalidPathCharsCopy()
                {
                    return (char[])badChars.Clone();
                }
            }

            public class Table
            {
                public readonly int[] {|FDG8002:Values|} = new int[4];
                protected readonly int[][] {|FDG8002:Rows|} = new int[2][];
                internal readonly int[] Hidden = new int[1];
                public readonly string Name = "";
                public int[] Writable = new int[1];
            }

            internal class Internal
            {
                public static readonly int[] Values = new int[1];
            }
            """);
    }

    [Fact]
    public Task Read_only_fields_initialized_with_an_empty_array_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Defaults
            {
                public static readonly int[] Empty = new int[0];
                public static readonly int[] None = Array.Empty<int>();
                public static readonly string[] Names = System.Array.Empty<string>();
                public static readonly int[] Blank = new int[] { };
                public static readonly int[] Braces = { };
                public static readonly int[][] Rows = new int[0][];
                public static readonly int[] {|FDG8002:One|} = new int[1];
                public static readonly int[] {|FDG8002:Filled|} = new int[] { 1 };
                public static readonly int[] {|FDG8002:Pair|} = new int[2], Zero = new int[0];
                public readonly int[] {|FDG8002:Assigned|};

                public Defaults(int[] values)
                {
                    Assigned = values;
                }
            }
            """);
    }

    [Fact]
    public Task Read_only_fields_initialized_with_an_empty_collection_expression_are_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public class Defaults
            {
                public static readonly int[] Empty = [];
                public static readonly int[] {|FDG8002:Values|} = [1, 2];
            }
            """);
    }

    [Fact]
    public Task ArrayList_Hashtable_and_Dictionary_in_visible_signatures_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public class Catalog
            {
                public Hashtable {|FDG8015:Index|};

                public Catalog(ArrayList {|FDG8014:items|})
                {
                }

                public ArrayList {|FDG8014:Items|} { get; }

                public int this[ArrayList {|FDG8014:keys|}]
                {
                    get { return 0; }
                }

                public Dictionary<string, int> {|FDG8015:Counts|}()
                {
                    return null;
                }

                public Task<Hashtable> {|FDG8015:LoadAsync|}()
                {
                    return null;
                }

                public void Merge(Dictionary<string, object> {|FDG8015:values|})
                {
                }

                protected ArrayList {|FDG8014:Create|}()
                {
                    return null;
                }
            }
            """);
    }

    [Fact]
    public Task Derived_types_type_arguments_inherited_and_hidden_members_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections;
            using System.Collections.Generic;

            public class NameTable : Hashtable
            {
            }

            public class Lookup : Dictionary<string, int>
            {
            }

            public interface IStore
            {
                ArrayList {|FDG8014:Load|}();
            }

            public abstract class StoreBase
            {
                public abstract Hashtable {|FDG8015:Index|} { get; }
            }

            public class Store : StoreBase, IStore
            {
                private ArrayList _items = new ArrayList();

                public NameTable Names { get; }

                public Lookup Counts { get; }

                public IDictionary<string, int> Map { get; }

                public IEnumerable<ArrayList> Groups { get; }

                public override Hashtable Index
                {
                    get { return null; }
                }

                public ArrayList Load()
                {
                    var local = new Hashtable();
                    return _items;
                }

                internal ArrayList Internal()
                {
                    return null;
                }

                private Dictionary<string, int> Private()
                {
                    return new Dictionary<string, int>();
                }
            }

            public sealed class Sealed
            {
                protected internal ArrayList Shared()
                {
                    return null;
                }
            }

            internal class Hidden
            {
                public ArrayList Items { get; }
            }
            """);
    }

    [Fact]
    public Task Positional_record_parameters_are_reported_once()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System.Collections;

            public record Snapshot(ArrayList {|FDG8014:Items|}, int Count);
            """);
    }

    [Fact]
    public Task Enumerator_types_outside_the_return_type_of_GetEnumerator_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections;
            using System.Collections.Generic;

            public class Tree : IEnumerable<int>
            {
                public IEnumerator {|FDG8016:Cursor|}
                {
                    get { return null; }
                }

                public IEnumerator<int> GetEnumerator()
                {
                    return null;
                }

                IEnumerator IEnumerable.GetEnumerator()
                {
                    return GetEnumerator();
                }

                public IEnumerator<int> {|FDG8016:GetNodes|}()
                {
                    return null;
                }

                public void Walk(IEnumerator<int> {|FDG8016:nodes|})
                {
                }

                public List<int>.Enumerator {|FDG8016:Items|}()
                {
                    return default(List<int>.Enumerator);
                }

                public IEnumerable<int> Values()
                {
                    return null;
                }

                public IEnumerable<IEnumerator<int>> Cursors()
                {
                    return null;
                }

                private IEnumerator<int> Hidden()
                {
                    return null;
                }
            }
            """);
    }

    [Fact]
    public Task Collections_of_bytes_in_visible_signatures_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;

            public class Reader
            {
                public IReadOnlyCollection<byte> {|FDG8031:Header|};

                public IList<byte> {|FDG8031:Buffer|} { get; }

                public Collection<byte> {|FDG8031:ReadBytes|}()
                {
                    return null;
                }

                public void Write(List<byte> {|FDG8031:data|})
                {
                }

                public byte[] ReadAll()
                {
                    return null;
                }

                public int ReadBytes(byte[] destination)
                {
                    return 0;
                }

                public ArraySegment<byte> Segment()
                {
                    return default(ArraySegment<byte>);
                }

                public IEnumerable<byte> Bytes()
                {
                    return null;
                }

                public List<int> Numbers()
                {
                    return null;
                }

                public List<sbyte> Signed()
                {
                    return null;
                }
            }
            """);
    }

    [Fact]
    public Task ImmutableArray_of_bytes_is_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System.Collections.Immutable;
            using System.Collections.ObjectModel;

            public class Reader
            {
                public ImmutableArray<byte> Bytes { get; }

                public void Write(ImmutableArray<byte> data)
                {
                }

                public ImmutableList<byte> {|FDG8031:List|} { get; }

                public Collection<byte> {|FDG8031:Copy|}()
                {
                    return null;
                }
            }
            """);
    }

    [Fact]
    public Task ICloneable_in_visible_signatures_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Factory
            {
                public ICloneable {|FDG8051:Prototype|} { get; }

                public object Copy(ICloneable {|FDG8051:source|})
                {
                    return source.Clone();
                }

                public T CopyOf<T>(T source)
                    where T : ICloneable
                {
                    return (T)source.Clone();
                }

                public Factory Clone()
                {
                    return null;
                }
            }
            """);
    }

    [Fact]
    public Task XmlNode_and_its_subclasses_in_visible_signatures_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Xml;
            using System.Xml.XPath;

            public class Config
            {
                public XmlDocument {|FDG8096:Document|} { get; }

                public IXPathNavigable Navigable { get; }

                public void Load(XmlNode {|FDG8096:node|})
                {
                }

                public XmlElement {|FDG8096:Root|}()
                {
                    return null;
                }

                public XmlReader Reader()
                {
                    return null;
                }

                public void Save(XmlWriter writer)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Messages_leave_out_nullable_annotations()
    {
        return Verifier.VerifyLatestWithAsync(
            """
            #nullable enable
            using System.Collections;
            using System.Xml;

            public class Converter
            {
                public string Serialize(XmlNode? {|#0:node|})
                {
                    return "";
                }

                public Hashtable? {|#1:Index|} { get; }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG8096).WithLocation(0).WithArguments("XmlNode", "Serialize"),
            new DiagnosticResult(FdgDescriptors.FDG8015).WithLocation(1).WithArguments("Hashtable", "Index"));
    }

    [Fact]
    public Task Messages_name_the_type_and_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Xml;

            public class Catalog
            {
                public static readonly int[] {|#0:Values|} = new int[1];

                public ArrayList {|#1:Items|} { get; }

                public Dictionary<string, int> {|#2:Counts|}()
                {
                    return null;
                }

                public void Walk(IEnumerator<int> {|#3:nodes|})
                {
                }

                public void Write(List<byte> {|#4:data|})
                {
                }

                public void Copy(ICloneable {|#5:source|})
                {
                }

                public Catalog(XmlDocument {|#6:document|})
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG8002).WithLocation(0).WithArguments("Values"),
            new DiagnosticResult(FdgDescriptors.FDG8014).WithLocation(1).WithArguments("Items"),
            new DiagnosticResult(FdgDescriptors.FDG8015)
                .WithLocation(2)
                .WithArguments("Dictionary<string, int>", "Counts"),
            new DiagnosticResult(FdgDescriptors.FDG8016).WithLocation(3).WithArguments("IEnumerator<int>", "Walk"),
            new DiagnosticResult(FdgDescriptors.FDG8031).WithLocation(4).WithArguments("List<byte>", "Write"),
            new DiagnosticResult(FdgDescriptors.FDG8051).WithLocation(5).WithArguments("Copy"),
            new DiagnosticResult(FdgDescriptors.FDG8096).WithLocation(6).WithArguments("XmlDocument", "Catalog"));
    }
}
