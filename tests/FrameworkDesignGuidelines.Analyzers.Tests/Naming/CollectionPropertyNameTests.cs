using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.CollectionPropertyNameAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class CollectionPropertyNameTests
{
    [Fact]
    public Task Book_example_bad_naming_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections.Generic;
            using System.Collections.ObjectModel;

            public class ItemCollection : Collection<object>
            {
            }

            public class ListView
            {
                // good naming
                public ItemCollection Items { get; }

                // bad naming
                public ItemCollection {|FDG3068:ItemCollection|} { get; }

                public List<string> {|FDG3068:NameList|} { get; set; }

                public int[] {|FDG3068:ValueCollection|} { get; set; }

                public IEnumerable<string> {|FDG3068:FileList|} => null;
            }
            """);
    }

    [Fact]
    public Task Other_properties_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections.Generic;

            public class Settings
            {
                public string ConnectionList { get; set; }

                public int ItemCollection { get; set; }

                public List<string> List { get; set; }

                public List<string> Playlist { get; set; }

                public List<string> Names { get; set; }

                internal List<string> NameList { get; set; }

                private List<string> ValueList { get; set; }
            }

            internal class Hidden
            {
                public List<string> NameList { get; set; }
            }
            """);
    }

    [Fact]
    public Task Overrides_and_interface_implementations_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections.Generic;

            public interface ISource
            {
                List<string> {|FDG3068:NameList|} { get; }
            }

            public abstract class Base
            {
                public abstract List<string> {|FDG3068:ValueList|} { get; }
            }

            public class Derived : Base, ISource
            {
                public override List<string> ValueList => null;

                public List<string> NameList => null;
            }
            """);
    }
}
