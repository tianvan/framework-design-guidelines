using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Extensibility.BaseSuffixAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Extensibility;

public sealed class BaseSuffixTests
{
    [Fact]
    public Task Base_class_used_in_a_public_signature_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;

            public class {|FDG6019:CollectionBase|}
            {
            }

            public abstract class {|FDG6019:NodeBase|}
            {
            }

            public class {|FDG6019:ItemBase|}<T>
            {
            }

            public class {|FDG6019:ValueBase|}
            {
            }

            public class {|FDG6019:HandlerBase|}
            {
            }

            public class {|FDG6019:SourceBase|}
            {
            }

            public class {|FDG6019:CloneableBase|}
            {
                public CloneableBase Clone() => this;
            }

            public class {|FDG6019:PluginBase|}
            {
            }

            public class {|FDG6019:StepBase|}
            {
            }

            public class Registry<TPlugin>
                where TPlugin : PluginBase
            {
            }

            public delegate void Visit(VisitorBase visitor);

            public class {|FDG6019:VisitorBase|}
            {
            }

            public class Directory
            {
                public CollectionBase GetFilenames() => null;

                public void Add(NodeBase node)
                {
                }

                public ItemBase<int> First { get; set; }

                public IEnumerable<ValueBase> Values => null;

                public HandlerBase[] Handlers;

                protected SourceBase this[int index] => null;

                public void Run<TStep>(TStep step)
                    where TStep : StepBase
                {
                }
            }
            """);
    }

    [Fact]
    public Task Base_class_not_used_in_a_public_signature_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public abstract class ControllerBase
            {
            }

            public abstract class EntityBase<TSelf>
                where TSelf : EntityBase<TSelf>
            {
            }

            public class Customer : EntityBase<Customer>
            {
            }

            public class Controller : ControllerBase
            {
            }

            public class RepositoryBase
            {
            }

            public class Store
            {
                internal RepositoryBase Repository => null;

                private void Save(RepositoryBase repository)
                {
                }
            }

            internal class Cache
            {
                public RepositoryBase Repository => null;
            }

            public sealed class Directory
            {
                protected RepositoryBase Protected => null;
            }
            """);
    }

    [Fact]
    public Task Names_types_and_visibility_outside_the_rule_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class Database
            {
            }

            public class Base
            {
            }

            public sealed class FinalBase
            {
            }

            public struct ValueBase
            {
            }

            public interface IVisitorBase
            {
            }

            internal class InternalBase
            {
            }

            public class Outer
            {
                private class HiddenBase
                {
                }

                public Database Data => null;

                public Base Root => null;

                public FinalBase Final => null;

                public ValueBase Value => default(ValueBase);

                public IVisitorBase Visitor => null;

                internal InternalBase Internal => null;
            }
            """);
    }

    [Fact]
    public Task Members_that_the_compiler_declares_do_not_count()
    {
        return Verifier.VerifyLatestAsync(
            """
            public abstract record NodeBase(int Id);

            public record Leaf(int Id) : NodeBase(Id);
            """);
    }

    [Fact]
    public Task Message_names_the_class()
    {
        return Verifier.VerifyWithAsync(
            """
            public class {|#0:ItemBase|}<T>
            {
            }

            public class Directory
            {
                public ItemBase<string> First => null;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG6019).WithLocation(0).WithArguments("ItemBase<T>"));
    }
}
