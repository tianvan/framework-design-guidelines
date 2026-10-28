using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Extensibility.PublicVirtualMemberAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Extensibility;

public sealed class PublicVirtualMemberTests
{
    [Fact]
    public Task Public_virtual_and_abstract_members_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Control
            {
                public virtual void {|FDG6013:SetBounds|}(int x, int y)
                {
                }

                public virtual string {|FDG6013:Text|} { get; set; }

                public virtual int {|FDG6013:this|}[int index] => index;

                public virtual event EventHandler {|FDG6013:Resized|};

                public virtual event EventHandler {|FDG6013:Moved|}
                {
                    add { }
                    remove { }
                }

                public class Part
                {
                    public virtual void {|FDG6013:Select|}()
                    {
                    }
                }
            }

            public abstract class Stream
            {
                public abstract int {|FDG6013:Read|}(byte[] buffer, int offset, int count);

                public abstract long {|FDG6013:Length|} { get; }
            }

            public class Button : Control
            {
                public new virtual void {|FDG6013:SetBounds|}(int x, int y)
                {
                }
            }

            public abstract class Shape
            {
                public abstract void {|FDG6013:Draw|}();
            }

            public abstract class Polygon : Shape
            {
                public abstract override void Draw();
            }
            """);
    }

    [Fact]
    public Task Class_with_only_an_internal_constructor_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            public abstract class LivingOrganism
            {
                internal LivingOrganism()
                {
                }

                public virtual void {|FDG6013:Grow|}()
                {
                }
            }

            public class Animal : LivingOrganism
            {
                public override void Grow()
                {
                }
            }
            """);
    }

    [Fact]
    public Task Protected_nonvirtual_override_and_invisible_members_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Control
            {
                public void SetBounds(int x, int y)
                {
                    SetBoundsCore(x, y);
                }

                protected virtual void SetBoundsCore(int x, int y)
                {
                }

                protected internal virtual void Refresh()
                {
                }

                private protected virtual void Invalidate()
                {
                }

                private class Painter
                {
                    public virtual void Paint()
                    {
                    }
                }

                public static void Create()
                {
                }
            }

            public class Button : Control
            {
                public override string ToString() => "Button";

                public sealed override bool Equals(object obj) => false;

                public override int GetHashCode() => 0;

                protected override void SetBoundsCore(int x, int y)
                {
                }
            }

            internal class Cache
            {
                public virtual void Clear()
                {
                }

                public class Entry
                {
                    public virtual void Evict()
                    {
                    }
                }
            }

            public interface IShape
            {
                void Draw();

                event EventHandler Changed;
            }
            """);
    }

    [Fact]
    public Task Protected_nested_class_of_a_public_class_is_externally_visible()
    {
        return Verifier.VerifyAsync(
            """
            public class Control
            {
                protected class Painter
                {
                    public virtual void {|FDG6013:Paint|}()
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Record_equality_member_is_not_reported_but_other_members_are()
    {
        return Verifier.VerifyLatestAsync(
            """
            public record Point(int X, int Y)
            {
                public virtual bool Equals(Point other) => other is object && X == other.X && Y == other.Y;

                public override int GetHashCode() => X ^ Y;

                public virtual double {|FDG6013:Length|}() => 0;
            }

            public record Point3(int X, int Y, int Z) : Point(X, Y);
            """);
    }

    [Fact]
    public Task Interface_default_members_are_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public interface IShape
            {
                void Draw()
                {
                }

                public virtual void Fill()
                {
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Control
            {
                public virtual void {|#0:SetBounds|}(int x, int y)
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG6013).WithLocation(0).WithArguments("SetBounds"));
    }

    [Fact]
    public Task Message_names_an_indexer_with_its_parameter_types()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Table
            {
                public virtual string {|#0:this|}[int row, string column] => column;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG6013).WithLocation(0).WithArguments("this[int, string]"));
    }
}
