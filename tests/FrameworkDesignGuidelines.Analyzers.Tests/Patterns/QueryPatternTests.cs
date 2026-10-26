using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Patterns.QueryPatternAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class QueryPatternTests
{
    [Fact]
    public Task Query_pattern_type_without_IEnumerable_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;
            using System.Linq.Expressions;

            public class {|FDG9100:DataSet|}<T>
            {
                public DataSet<T> Where(Func<T, bool> predicate) => this;
            }

            internal struct {|FDG9100:Remote|}
            {
                public Remote Select(Expression<Func<int, int>> selector) => this;
            }

            public interface {|FDG9100:IQuerySource|}
            {
                IQuerySource SkipWhile(Func<int, bool> predicate);
            }

            public class MyDataSet<T> : IEnumerable<T>
            {
                public MyDataSet<T> Where(Func<T, bool> query) => this;

                public IEnumerator<T> GetEnumerator() => null;

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public abstract class Queryable<T> : IQueryable<T>
            {
                public abstract Type ElementType { get; }

                public abstract Expression Expression { get; }

                public abstract IQueryProvider Provider { get; }

                public Queryable<T> Where(Expression<Func<T, bool>> predicate) => this;

                public abstract IEnumerator<T> GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class Picker
            {
                public void Select(Func<int, bool> predicate)
                {
                }

                public Picker Where(int index) => this;

                public static Picker OrderBy(Func<int, int> key) => null;

                public Picker Union(Picker other) => this;
            }

            public interface ISource : IEnumerable<int>
            {
                ISource Where(Func<int, bool> predicate);
            }

            public class Source : ISource, IEnumerable<int>
            {
                public ISource Where(Func<int, bool> predicate) => this;

                public IEnumerator<int> GetEnumerator() => null;

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """);
    }

    [Fact]
    public Task Interface_implementations_and_overrides_do_not_make_a_query_pattern_type()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public interface {|FDG9100:IFilter|}
            {
                IFilter Where(Func<int, bool> predicate);
            }

            public class Filter : IFilter
            {
                public IFilter Where(Func<int, bool> predicate) => this;
            }

            public class ExplicitFilter : IFilter
            {
                IFilter IFilter.Where(Func<int, bool> predicate) => this;
            }

            public abstract class {|FDG9100:Base|}
            {
                public abstract Base Select(Func<int, int> selector);
            }

            public class Derived : Base
            {
                public override Base Select(Func<int, int> selector) => this;
            }
            """);
    }

    [Fact]
    public Task Query_pattern_message_names_the_type_and_method()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public class {|#0:Rows|}
            {
                public Rows Where(Func<int, bool> predicate) => this;

                public Rows Join(Rows inner, Func<int, int> outerKey, Func<int, int> innerKey) => this;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9100).WithLocation(0).WithArguments("Rows", "Join"));
    }

    [Fact]
    public Task OrderBy_returning_the_unordered_type_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;

            public class Rows<T> : IEnumerable<T>
            {
                public Rows<T> {|FDG9103:OrderBy|}<TKey>(Func<T, TKey> key) => this;

                public IEnumerable<T> {|FDG9103:OrderByDescending|}<TKey>(Func<T, TKey> key) => this;

                public IEnumerator<T> GetEnumerator() => null;

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class OrderedRows<T> : Rows<T>
            {
                public OrderedRows<T> ThenBy<TKey>(Func<T, TKey> key, int unused) => this;
            }

            public class Sorted<T> : IEnumerable<T>
            {
                public SortedView<T> OrderBy<TKey>(Func<T, TKey> key) => null;

                public IOrderedEnumerable<T> OrderByDescending<TKey>(Func<T, TKey> key) => null;

                public IEnumerator<T> GetEnumerator() => null;

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class SortedView<T> : Sorted<T>
            {
            }

            namespace Library.Linq
            {
                public static class RowsQueries
                {
                    public static Rows<T> {|FDG9103:OrderBy|}<T, TKey>(this Rows<T> source, Func<T, TKey> key) =>
                        source;

                    public static IEnumerable<T> {|FDG9103:OrderByDescending|}<T, TKey>(
                        this IEnumerable<T> source, Func<T, TKey> key) => source;

                    public static IOrderedEnumerable<T> OrderBy<T>(this T[] source, Func<T, int> key) => null;

                    public static Rows<T> OrderBy<T>(this Rows<T> source, int column) => source;
                }
            }
            """);
    }

    [Fact]
    public Task OrderBy_on_an_ordered_sequence_type_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;

            public class Rows<T> : IEnumerable<T>
            {
                public OrderedRows<T> OrderBy<TKey>(Func<T, TKey> key) => null;

                public IEnumerator<T> GetEnumerator() => null;

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public class OrderedRows<T> : Rows<T>
            {
                public new OrderedRows<T> OrderBy<TKey>(Func<T, TKey> key) => this;

                public OrderedRows<T> ThenBy<TKey>(Func<T, TKey> key) => this;
            }

            public interface IOrderedSeq<T> : IEnumerable<T>
            {
            }

            namespace Library.Linq
            {
                public static class OrderedSeqQueries
                {
                    public static IOrderedSeq<T> OrderBy<T, TKey>(this IOrderedSeq<T> source, Func<T, TKey> key) =>
                        source;

                    public static IOrderedSeq<T> ThenBy<T, TKey>(this IOrderedSeq<T> source, Func<T, TKey> key) =>
                        source;

                    public static IOrderedEnumerable<T> OrderBy<T, TKey>(
                        this IOrderedEnumerable<T> source, Func<T, TKey> key) => source;
                }

                public static class OtherQueries
                {
                    public static IOrderedSeq<T> {|FDG9103:OrderBy|}<T, TKey>(
                        this IOrderedSeq<T> source, Func<T, TKey> key, int unused) => source;
                }
            }
            """);
    }

    [Fact]
    public Task OrderBy_message_names_the_method_and_type()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public class {|#1:Rows|}
            {
                public Rows {|#0:OrderBy|}(Func<int, int> key) => this;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9100).WithLocation(1).WithArguments("Rows", "OrderBy"),
            new DiagnosticResult(FdgDescriptors.FDG9103).WithLocation(0).WithArguments("Rows.OrderBy", "Rows"));
    }

    [Fact]
    public Task Query_extension_methods_outside_a_Linq_namespace_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;

            public static class GlobalQueries
            {
                public static IEnumerable<T> {|FDG9106:Where|}<T>(
                    this IEnumerable<T> source, Func<T, bool> predicate) => source;
            }

            namespace Library.Extensions
            {
                public static class SequenceQueries
                {
                    public static IEnumerable<TResult> {|FDG9106:Select|}<T, TResult>(
                        this IEnumerable<T> source, Func<T, TResult> selector) => null;

                    public static IEnumerable<T> Take<T>(this IEnumerable<T> source, int count) => source;

                    public static IEnumerable<T> Where<T>(IEnumerable<T> source, Func<T, bool> predicate) => source;

                    internal static IEnumerable<T> SkipWhile<T>(this IEnumerable<T> source, Func<T, bool> predicate) =>
                        source;
                }

                internal static class HiddenQueries
                {
                    public static IEnumerable<T> SkipWhile<T>(this IEnumerable<T> source, Func<T, bool> predicate) =>
                        source;
                }
            }

            namespace Library.Data.Linq
            {
                public static class DataQueries
                {
                    public static IEnumerable<T> Where<T>(this IEnumerable<T> source, Func<T, bool> predicate) =>
                        source;
                }
            }

            namespace Library.Linq.Helpers
            {
                public static class HelperQueries
                {
                    public static IEnumerable<T> {|FDG9106:Where|}<T>(
                        this IEnumerable<T> source, Func<T, bool> predicate) => source;
                }
            }
            """);
    }

    [Fact]
    public Task Query_extension_message_names_the_method_and_namespace()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;
            using System.Collections.Generic;

            namespace Library.Extensions
            {
                public static class SequenceQueries
                {
                    public static IEnumerable<T> {|#0:Where|}<T>(this IEnumerable<T> source, Func<T, bool> predicate) =>
                        source;
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9106).WithLocation(0)
                .WithArguments("SequenceQueries.Where", "Library.Extensions"));
    }
}
