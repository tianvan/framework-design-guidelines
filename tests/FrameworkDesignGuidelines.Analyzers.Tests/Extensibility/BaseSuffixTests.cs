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

            public abstract class {|FDG6019:CollectionBase|}
            {
            }

            public abstract class {|FDG6019:NodeBase|}
            {
            }

            public class {|FDG6019:ItemBase|}<T>
            {
            }

            public class Item : ItemBase<int>
            {
            }

            public class {|FDG6019:ValueBase|}
            {
                protected int Value;
            }

            public class {|FDG6019:HandlerBase|}
            {
                public virtual void Handle()
                {
                }
            }

            public class {|FDG6019:SourceBase|}
            {
                protected internal int Id { get; set; }
            }

            public abstract class {|FDG6019:CloneableBase|}
            {
                public CloneableBase Clone() => this;
            }

            public abstract class {|FDG6019:PluginBase|}
            {
            }

            public class {|FDG6019:StepBase|}
            {
            }

            public class Step : StepBase
            {
            }

            public class Registry<TPlugin>
                where TPlugin : PluginBase
            {
            }

            public delegate void Visit(VisitorBase visitor);

            public abstract class {|FDG6019:VisitorBase|}
            {
            }

            public abstract class {|FDG6019:ChangedEventArgsBase|} : EventArgs
            {
            }

            public abstract class {|FDG6019:OptionsBase|}
            {
            }

            public class Directory
            {
                public Directory(OptionsBase options)
                {
                }

                public CollectionBase GetFilenames() => null;

                public void Add(NodeBase node)
                {
                }

                public ItemBase<int> First { get; set; }

                public IEnumerable<ValueBase> Values => null;

                public HandlerBase[] Handlers;

                protected SourceBase this[int index] => null;

                public event EventHandler<ChangedEventArgsBase> Changed;

                public void Run<TStep>(TStep step)
                    where TStep : StepBase
                {
                }
            }
            """);
    }

    [Fact]
    public Task Class_that_is_not_a_base_class_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class KnowledgeBase
            {
                public string Find(string question) => question;
            }

            public class Assistant
            {
                public KnowledgeBase GetKnowledgeBase() => new KnowledgeBase();
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

            public class Controller : ControllerBase
            {
            }

            public abstract class EntityBase<TSelf>
                where TSelf : EntityBase<TSelf>
            {
            }

            public class Customer : EntityBase<Customer>
            {
            }

            public abstract class RepositoryBase
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
    public Task Self_constraint_still_counts_its_type_arguments()
    {
        return Verifier.VerifyAsync(
            """
            public abstract class {|FDG6019:KeyBase|}
            {
            }

            public abstract class RecordBase<TSelf, TKey>
                where TSelf : RecordBase<TSelf, KeyBase>
            {
            }
            """);
    }

    [Fact]
    public Task Names_types_and_visibility_outside_the_rule_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public abstract class Database
            {
            }

            public abstract class Base
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

            internal abstract class InternalBase
            {
            }

            public class Outer
            {
                private abstract class HiddenBase
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
            public abstract class {|#0:ItemBase|}<T>
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
