using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Usage.InterfaceImplementationAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Usage;

public sealed class InterfaceImplementationTests
{
    private const string EnumeratorMembers =
        """
            public int Current { get { return 0; } }
            object IEnumerator.Current { get { return null; } }
            public void Dispose() { }
            public bool MoveNext() { return false; }
            public void Reset() { }
        """;

    [Fact]
    public Task Types_that_are_both_a_collection_and_an_enumerator_are_reported()
    {
        return Verifier.VerifyAsync(
            $$"""
            using System.Collections;
            using System.Collections.Generic;

            public class {|FDG8017:Cursor|} : IEnumerable<int>, IEnumerator<int>
            {
            {{EnumeratorMembers}}
                public IEnumerator<int> GetEnumerator() { return this; }
                IEnumerator IEnumerable.GetEnumerator() { return this; }
            }

            internal class {|FDG8017:Legacy|} : IEnumerable, IEnumerator
            {
                public object Current { get { return null; } }
                public bool MoveNext() { return false; }
                public void Reset() { }
                public IEnumerator GetEnumerator() { return this; }
            }

            public class DerivedCursor : Cursor
            {
            }

            public class Numbers : IEnumerable<int>
            {
                public IEnumerator<int> GetEnumerator() { return new Enumerator(); }
                IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

                public struct Enumerator : IEnumerator<int>, System.IEquatable<Enumerator>
                {
            {{EnumeratorMembers}}
                    public bool Equals(Enumerator other) { return true; }
                }
            }

            public class Iterators
            {
                public IEnumerable<int> Values()
                {
                    yield return 1;
                }
            }
            """);
    }

    [Fact]
    public Task Types_that_implement_ICloneable_themselves_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class {|FDG8050:Customer|} : ICloneable
            {
                public object Clone() { return MemberwiseClone(); }
            }

            public interface IDeepCloneable : ICloneable
            {
            }

            internal class {|FDG8050:Document|} : IDeepCloneable
            {
                object ICloneable.Clone() { return null; }
            }

            public class PreferredCustomer : Customer
            {
            }

            public class Order
            {
                public Order Clone() { return (Order)MemberwiseClone(); }
            }
            """);
    }

    [Fact]
    public Task Structs_without_IEquatable_of_themselves_are_reported()
    {
        return Verifier.VerifyAsync(
            $$"""
            using System;
            using System.Collections;
            using System.Collections.Generic;

            public struct {|FDG8053:Point|}
            {
                public int X;
            }

            internal struct {|FDG8053:Pair|}<T>
            {
                public T First;
            }

            public struct {|FDG8053:Wrapper|} : IEquatable<int>
            {
                public int Value { get; set; }
                public bool Equals(int other) { return false; }
            }

            public struct MyStruct : IEquatable<MyStruct>
            {
                public int Value;
                public bool Equals(MyStruct other) { return Value == other.Value; }
            }

            public struct Box<T> : IEquatable<Box<T>>
            {
                public bool Equals(Box<T> other) { return true; }
            }

            public struct Walker : IEnumerator<int>
            {
            {{EnumeratorMembers}}
            }

            public ref struct Slice
            {
                public int Start;
            }

            public struct Names
            {
                public const string Default = "default";
                public static readonly string Empty = "";
            }

            public enum Color
            {
                Red,
            }
            """);
    }

    [Fact]
    public Task Record_structs_implement_IEquatable_already()
    {
        return Verifier.VerifyLatestAsync(
            """
            public record struct Point(int X, int Y);
            """);
    }

    [Fact]
    public Task Types_that_are_comparable_but_not_equatable_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class {|FDG8057:Version2|} : IComparable<Version2>
            {
                public int CompareTo(Version2 other) { return 0; }
            }

            public class {|FDG8057:Mixed|} : IComparable<Mixed>, IEquatable<string>
            {
                public int CompareTo(Mixed other) { return 0; }
                public bool Equals(string other) { return false; }
            }

            public class Money : IComparable<Money>, IEquatable<Money>
            {
                public int CompareTo(Money other) { return 0; }
                public bool Equals(Money other) { return true; }
            }

            public class Release : Version2
            {
            }

            public class Legacy : IComparable
            {
                public int CompareTo(object other) { return 0; }
            }
            """);
    }

    [Fact]
    public Task Messages_name_the_type_and_the_compared_type()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public struct {|#0:Pair|}<T> : IComparable<Pair<T>>
            {
                public T First;
                public int CompareTo(Pair<T> other) { return 0; }
            }

            public class {|#1:Customer|} : ICloneable
            {
                public object Clone() { return null; }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG8053).WithLocation(0).WithArguments("Pair<T>"),
            new DiagnosticResult(FdgDescriptors.FDG8057).WithLocation(0).WithArguments("Pair<T>", "Pair<T>"),
            new DiagnosticResult(FdgDescriptors.FDG8050).WithLocation(1).WithArguments("Customer"));
    }
}
