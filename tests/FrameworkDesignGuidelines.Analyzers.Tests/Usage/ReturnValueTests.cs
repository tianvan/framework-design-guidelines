using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Usage.ReturnValueAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Usage;

public sealed class ReturnValueTests
{
    [Fact]
    public Task Null_returned_as_a_collection_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            using System.Threading.Tasks;

            public class Store
            {
                public string[] Names
                {
                    get { return {|FDG8026:null|}; }
                }

                public List<int> Ids => {|FDG8026:null|};

                public ICollection this[int index]
                {
                    get { return {|FDG8026:null|}; }
                }

                public IEnumerable<string> GetList()
                {
                    return {|FDG8026:null|};
                }

                public IList<int> Find(bool all)
                {
                    return all ? new List<int>() : {|FDG8026:null|};
                }

                public Dictionary<string, int> Map()
                {
                    return {|FDG8026:default(Dictionary<string, int>)|};
                }

                public IReadOnlyList<int> Recent()
                {
                    return {|FDG8026:default|};
                }

                public async Task<List<int>> LoadAsync()
                {
                    await Task.Yield();
                    return {|FDG8026:null|};
                }

                public IEnumerable<int> Outer()
                {
                    return Inner();

                    IEnumerable<int> Inner()
                    {
                        return {|FDG8026:null|};
                    }
                }

                private List<int> Hidden()
                {
                    return {|FDG8026:null|};
                }

                public OrderCollection Orders()
                {
                    return {|FDG8026:null|};
                }
            }

            public class OrderCollection : Collection<int>
            {
            }
            """);
    }

    [Fact]
    public Task Empty_collections_other_types_and_lambdas_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Xml;

            public class Store
            {
                private List<string> _names;

                public IEnumerable<string> GetList()
                {
                    IEnumerable<string> list = GetList();
                    foreach (string name in list)
                    {
                    }

                    return new string[0];
                }

                public string Text()
                {
                    return null;
                }

                public object Value()
                {
                    return null;
                }

                public XmlNode Node()
                {
                    return null;
                }

                public List<string> Cached()
                {
                    return _names;
                }

                public IEnumerable<int> Iterate()
                {
                    yield break;
                }

                public Func<List<int>> Factory()
                {
                    return () => null;
                }

                public Func<List<int>> Anonymous()
                {
                    return delegate { return null; };
                }

                public void Run()
                {
                    return;
                }

                public Property Find(string name)
                {
                    return null;
                }
            }

            public abstract class Node : IEnumerable<Node>
            {
                public IEnumerator<Node> GetEnumerator() { return null; }
                IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
            }

            public abstract class Property : Node, IList<Node>
            {
                public abstract Node this[int index] { get; set; }
                public abstract int Count { get; }
                public abstract bool IsReadOnly { get; }
                public abstract void Add(Node item);
                public abstract void Clear();
                public abstract bool Contains(Node item);
                public abstract void CopyTo(Node[] array, int arrayIndex);
                public abstract int IndexOf(Node item);
                public abstract void Insert(int index, Node item);
                public abstract bool Remove(Node item);
                public abstract void RemoveAt(int index);
            }
            """);
    }

    [Fact]
    public Task Empty_and_null_strings_returned_from_ToString_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Point
            {
                public override string ToString() { return {|FDG8082:null|}; }
            }

            public class Blank
            {
                public override string ToString() => {|FDG8082:""|};
            }

            public class Empty
            {
                public override string ToString() { return {|FDG8082:string.Empty|}; }
            }

            public class Maybe
            {
                private string _name;
                public override string ToString() { return _name == null ? {|FDG8082:String.Empty|} : _name; }
            }

            public class Default
            {
                public override string ToString() { return {|FDG8082:default(string)|}; }
            }

            public class Named
            {
                public override string ToString() { return "Named"; }
            }

            public class Formatted
            {
                public string ToString(string format) { return null; }
            }

            public class Hidden
            {
                public new string ToString() { return null; }
            }

            public class Deferred
            {
                public override string ToString()
                {
                    Func<string> empty = () => "";
                    return empty() + "!";
                }
            }
            """);
    }

    [Fact]
    public Task Messages_name_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            using System.Collections.Generic;

            public class Store
            {
                public List<int> Ids
                {
                    get { return {|#0:null|}; }
                }

                public int[] this[int index]
                {
                    get { return {|#1:null|}; }
                }

                public IEnumerable<string> GetList()
                {
                    return {|#2:null|};
                }

                public override string ToString()
                {
                    return {|#3:""|};
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG8026).WithLocation(0).WithArguments("Ids"),
            new DiagnosticResult(FdgDescriptors.FDG8026).WithLocation(1).WithArguments("this"),
            new DiagnosticResult(FdgDescriptors.FDG8026).WithLocation(2).WithArguments("GetList"),
            new DiagnosticResult(FdgDescriptors.FDG8082).WithLocation(3).WithArguments("Store"));
    }
}
