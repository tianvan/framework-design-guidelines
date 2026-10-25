using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Usage.SerializationAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Usage;

public sealed class SerializationTests
{
    [Fact]
    public Task Serialization_constructors_with_the_wrong_accessibility_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Runtime.Serialization;

            [Serializable]
            public class Person : ISerializable
            {
                protected Person(SerializationInfo info, StreamingContext context) { }
                void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            public class Customer : ISerializable
            {
                public {|FDG8088:Customer|}(SerializationInfo info, StreamingContext context) { }
                void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            public class Locked : ISerializable
            {
                private {|FDG8088:Locked|}(SerializationInfo info, StreamingContext context) { }
                void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            public sealed class Order : ISerializable
            {
                private Order(SerializationInfo info, StreamingContext context) { }
                void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            public sealed class Invoice : ISerializable
            {
                internal {|FDG8088:Invoice|}(SerializationInfo info, StreamingContext context) { }
                void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            public struct Amount : ISerializable
            {
                public {|FDG8088:Amount|}(SerializationInfo info, StreamingContext context) { }
                void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            public class StoreException : Exception
            {
                public StoreException() { }
                protected StoreException(SerializationInfo info, StreamingContext context) : base(info, context) { }
            }

            [Serializable]
            public class QueueException : Exception
            {
                public {|FDG8088:QueueException|}(SerializationInfo info, StreamingContext context)
                    : base(info, context)
                {
                }
            }

            public class Plain
            {
                public Plain(SerializationInfo info, StreamingContext context) { }
            }
            """);
    }

    [Fact]
    public Task Implicit_implementations_of_GetObjectData_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Runtime.Serialization;

            [Serializable]
            public class Person : ISerializable
            {
                protected Person(SerializationInfo info, StreamingContext context) { }
                public virtual void {|FDG8089:GetObjectData|}(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            public class Employee : Person
            {
                protected Employee(SerializationInfo info, StreamingContext context) : base(info, context) { }
                public override void GetObjectData(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            internal sealed class Entry : ISerializable
            {
                private Entry(SerializationInfo info, StreamingContext context) { }
                public void {|FDG8089:GetObjectData|}(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            public class StoreException : Exception
            {
                protected StoreException(SerializationInfo info, StreamingContext context) : base(info, context) { }
                public override void GetObjectData(SerializationInfo info, StreamingContext context) { }
            }

            public class Report
            {
                public void GetObjectData(SerializationInfo info, StreamingContext context) { }
            }
            """);
    }

    [Fact]
    public Task Messages_name_the_type_and_the_accessibility()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;
            using System.Runtime.Serialization;

            [Serializable]
            public class Customer : ISerializable
            {
                public {|#0:Customer|}(SerializationInfo info, StreamingContext context) { }
                public void {|#1:GetObjectData|}(SerializationInfo info, StreamingContext context) { }
            }

            [Serializable]
            public sealed class Order : ISerializable
            {
                protected {|#2:Order|}(SerializationInfo info, StreamingContext context) { }
                void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) { }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG8088).WithLocation(0).WithArguments("Customer", "protected"),
            new DiagnosticResult(FdgDescriptors.FDG8089).WithLocation(1).WithArguments("Customer"),
            new DiagnosticResult(FdgDescriptors.FDG8088).WithLocation(2).WithArguments("Order", "private"));
    }
}
