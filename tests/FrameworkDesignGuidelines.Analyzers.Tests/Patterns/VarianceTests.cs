using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Patterns.VarianceAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class VarianceTests
{
    [Fact]
    public Task Delegate_type_parameters_used_one_way_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;

            public delegate void Invariant<{|FDG9111:T|}>(T arg);

            public delegate void Contravariant<in T>(T arg);

            public delegate T Produce<{|FDG9115:T|}>();

            public delegate T Covariant<out T>();

            public delegate T Transform<T>(T value);

            public delegate void ByRef<T>(ref T value);

            public delegate void Output<T>(out T value);

            public delegate void Many<{|FDG9111:T|}, {|FDG9115:TResult|}>(
                IEnumerable<T> items, Action<TResult> callback);

            public delegate void Unused<T>();

            internal delegate void Hidden<T>(T arg);
            """);
    }

    [Fact]
    public Task Interface_type_parameters_used_one_way_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;

            public interface IConsumer<{|FDG9112:T|}>
            {
                void Consume(T value);

                int Sum(IEnumerable<T> inputs);

                T this[int index] { set; }
            }

            public interface IProducer<{|FDG9116:T|}>
            {
                T Produce();

                T Current { get; }

                IEnumerable<T> Filter(Func<T, bool> predicate);

                void Each(Action<T> action);

                event Action<T> Changed;
            }

            public interface IDerived<{|FDG9116:T|}> : IEnumerable<T>
            {
            }

            public interface IList2<T>
            {
                T Get();

                void Set(T value);

                T Value { get; set; }
            }

            public interface IRefs<T>
            {
                ref T Get();
            }

            public interface IOuts<T>
            {
                bool TryGet(out T value);
            }

            public interface IConstraint<{|FDG9112:T|}>
            {
                void Add<U>(U item) where U : T;
            }

            public interface IReadOnly<out T>
            {
                T Get();
            }

            public interface IWriteOnly<in T>
            {
                void Set(T value);
            }

            internal interface IHidden<T>
            {
                void Set(T value);
            }

            public interface IMixed<{|FDG9112:TIn|}, {|FDG9116:TOut|}, TBoth>
            {
                TOut Map(TIn input, TBoth both);

                TBoth Both();
            }

            public interface IUnused<T>
            {
                void Clear();
            }

            public interface ICalculator<in T>
            {
                int Sum(IEnumerable<T> inputs);
            }

            public interface IFilter<out T>
            {
                IEnumerable<T> Filter(Func<T, bool> predicate);
            }
            """);
    }

    [Fact]
    public Task Positions_that_keep_a_type_parameter_invariant_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;

            public interface IListOf<T>
            {
                List<T> Items();
            }

            public interface IArrays<T>
            {
                T[] ToArray();
            }

            public interface IConstrained<T, {|FDG9112:U|}>
                where U : T
            {
                void Set(T value);

                void Put(U value);
            }

            public interface IArrayInput<{|FDG9112:T|}>
            {
                void Set(T[] values);
            }
            """);
    }

    [Fact]
    public Task Nested_types_and_static_members_keep_a_type_parameter_invariant()
    {
        return Verifier.VerifyLatestAsync(
            """
            public interface INested<T>
            {
                class Helper
                {
                }

                void Set(T value);
            }

            public interface IStatic<T>
            {
                static T Create() => default;

                void Set(T value);
            }

            public interface IWithNestedInterface<{|FDG9112:T|}>
            {
                interface IInner
                {
                }

                void Set(T value);
            }
            """);
    }

    [Fact]
    public Task Book_examples_of_variant_interfaces()
    {
        // The IType2<T> of the Simulated Covariance example uses T only in a getter, so it is reported.
        return Verifier.VerifyAsync(
            """
            using System.Collections.Generic;

            public interface ISequenceEqual<in T>
            {
                bool SequenceEqual(IEnumerable<T> other);
            }

            public interface IEqualityCollection<T> : ICollection<T>, ISequenceEqual<T>
            {
            }

            public interface IReadOnlyEasyCollection<out T> : IEnumerable<T>
            {
                int Count { get; }

                bool IsReadOnly { get; }
            }

            public interface IEasyCollection<T> : IReadOnlyEasyCollection<T>
            {
                void Add(T item);

                void Clear();
            }

            public interface ICountedReference<out T>
            {
                T Value { get; }

                int Count { get; }

                void AddReference();

                void ReleaseReference();
            }

            public interface IType2<{|FDG9116:T|}>
            {
                T Property { get; }
            }
            """);
    }

    [Fact]
    public Task Type_parameter_message_names_the_type()
    {
        return Verifier.VerifyWithAsync(
            """
            public interface IConsumer<{|#0:T|}>
            {
                void Consume(T value);
            }

            public delegate TResult Factory<{|#1:TResult|}>();
            """,
            new DiagnosticResult(FdgDescriptors.FDG9112).WithLocation(0).WithArguments("T", "IConsumer<T>"),
            new DiagnosticResult(FdgDescriptors.FDG9115).WithLocation(1).WithArguments("TResult", "Factory<TResult>"));
    }

    [Fact]
    public Task Covariant_interface_returning_an_array_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections.Generic;

            public interface IArrayProducer<out T>
            {
                T[] {|FDG9117:ProduceArray|}(int length);
            }

            public class ArrayProducer<T> : IArrayProducer<T>
            {
                public T[] ProduceArray(int length)
                {
                    return new T[length];
                }
            }

            internal interface IHidden<out T>
            {
                T[] {|FDG9117:Items|} { get; }

                IEnumerable<T[]> {|FDG9117:Batches|}();

                T[][] {|FDG9117:Jagged|}();

                T this[int index] { get; }

                IEnumerable<T> Sequence();

                int[] Counts();

                void Fill(System.Action<T[]> filler);
            }

            public interface IInvariant<T>
            {
                T[] ToArray();
            }
            """);
    }

    [Fact]
    public Task Covariant_array_message_names_the_array_member_and_interface()
    {
        return Verifier.VerifyWithAsync(
            """
            public interface IArrayProducer<out T>
            {
                T[] {|#0:ProduceArray|}(int length);
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9117).WithLocation(0)
                .WithArguments("T[]", "ProduceArray(int)", "IArrayProducer<T>"));
    }
}
