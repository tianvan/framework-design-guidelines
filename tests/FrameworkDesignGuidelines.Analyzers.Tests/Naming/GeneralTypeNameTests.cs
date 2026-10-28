using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.GeneralTypeNameAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class GeneralTypeNameTests
{
    [Fact]
    public Task Type_names_the_rule_lists_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace Sample
            {
                public class {|FDG3033:Element|}
                {
                }

                public struct {|FDG3033:Node|}<T>
                {
                }

                public interface ILogger
                {
                }

                public enum {|FDG3033:Log|}
                {
                    None,
                }

                public delegate void {|FDG3033:Message|}();

                public class Tree
                {
                    public sealed class {|FDG3033:Node|}
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Qualified_names_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class FormElement
            {
            }

            public class XmlNode
            {
            }

            public class EventLog
            {
            }

            public class SoapMessage
            {
            }

            public class Elements
            {
                public string Node { get; set; }

                public void Log(string Message)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Types_that_are_not_externally_visible_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            internal class Element
            {
            }

            public class Tree
            {
                private sealed class Node
                {
                }
            }

            public sealed class List
            {
                protected internal class Message
                {
                }
            }
            """);
    }

    [Fact]
    public Task Records_are_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public record {|FDG3033:Message|}(string Text);

            public record struct {|FDG3033:Element|}(int Value);
            """);
    }
}
