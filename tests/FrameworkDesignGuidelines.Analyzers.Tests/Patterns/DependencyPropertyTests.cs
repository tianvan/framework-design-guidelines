using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Patterns.DependencyPropertyAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class DependencyPropertyTests
{
    /// <summary>The WPF types that the analyzer looks for, reduced to the members the tests use.</summary>
    private const string Wpf =
        """
        using System;

        namespace System.Windows
        {
            public class DependencyObject
            {
                public object GetValue(DependencyProperty dp) { return null; }

                public void SetValue(DependencyProperty dp, object value) { }

                public void SetValue(DependencyPropertyKey key, object value) { }
            }

            public sealed class DependencyProperty
            {
                public static DependencyProperty Register(
                    string name, Type propertyType, Type ownerType, PropertyMetadata typeMetadata = null)
                {
                    return null;
                }

                public static DependencyPropertyKey RegisterReadOnly(
                    string name, Type propertyType, Type ownerType, PropertyMetadata typeMetadata)
                {
                    return null;
                }

                public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType)
                {
                    return null;
                }
            }

            public sealed class DependencyPropertyKey
            {
                public DependencyProperty DependencyProperty { get { return null; } }
            }

            public class PropertyMetadata
            {
                public PropertyMetadata(object defaultValue) { }
            }
        }
        """;

    [Fact]
    public Task Registration_outside_a_dependency_object_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Windows;

            public class Plain
            {
                public static readonly DependencyProperty {|FDG9052:TextProperty|} =
                    DependencyProperty.Register("Text", typeof(string), typeof(Plain));
            }

            public class Grid
            {
                public static readonly DependencyProperty ColumnProperty =
                    DependencyProperty.RegisterAttached("Column", typeof(int), typeof(Grid));

                public static int GetColumn(DependencyObject obj) { return (int)obj.GetValue(ColumnProperty); }

                public static void SetColumn(DependencyObject obj, int value) { obj.SetValue(ColumnProperty, value); }
            }

            public class TextButton : DependencyObject
            {
                public static readonly DependencyProperty TextProperty =
                    DependencyProperty.Register("Text", typeof(string), typeof(TextButton));

                public string Text
                {
                    get { return (string)GetValue(TextProperty); }
                    set { SetValue(TextProperty, value); }
                }
            }
            """,
            ("/Wpf.cs", Wpf));
    }

    [Fact]
    public Task Field_that_is_not_public_static_readonly_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Windows;

            public class BadType : DependencyObject
            {
                private static readonly DependencyProperty {|FDG9053:SecretProperty|} =
                    DependencyProperty.Register("Secret", typeof(string), typeof(BadType));

                public static DependencyProperty {|FDG9053:NameProperty|} =
                    DependencyProperty.Register("Name", typeof(string), typeof(BadType));

                public static readonly DependencyProperty TitleProperty =
                    DependencyProperty.Register("Title", typeof(string), typeof(BadType));

                private static readonly DependencyPropertyKey CountPropertyKey =
                    DependencyProperty.RegisterReadOnly("Count", typeof(int), typeof(BadType), new PropertyMetadata(0));

                public static readonly DependencyProperty CountProperty = CountPropertyKey.DependencyProperty;
            }
            """,
            ("/Wpf.cs", Wpf));
    }

    [Fact]
    public Task Field_not_named_after_the_registered_name_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Windows;

            public class TextButton : DependencyObject
            {
                public static readonly DependencyProperty {|FDG9055:TextDp|} =
                    DependencyProperty.Register("Text", typeof(string), typeof(TextButton));

                public static readonly DependencyProperty {|FDG9055:Caption|} =
                    DependencyProperty.Register(nameof(Title), typeof(string), typeof(TextButton));

                public static readonly DependencyProperty TitleProperty =
                    DependencyProperty.Register(nameof(Title), typeof(string), typeof(TextButton));

                public static readonly DependencyProperty ColumnProperty =
                    DependencyProperty.RegisterAttached("Column", typeof(int), typeof(TextButton));

                public string Title
                {
                    get { return (string)GetValue(TitleProperty); }
                    set { SetValue(TitleProperty, value); }
                }
            }
            """,
            ("/Wpf.cs", Wpf));
    }

    [Fact]
    public Task Accessor_that_does_not_use_the_property_store_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Windows;

            public class TextButton : DependencyObject
            {
                public static readonly DependencyProperty TextProperty =
                    DependencyProperty.Register("Text", typeof(string), typeof(TextButton));

                public static readonly DependencyProperty TitleProperty =
                    DependencyProperty.Register("Title", typeof(string), typeof(TextButton));

                public static readonly DependencyProperty CaptionProperty =
                    DependencyProperty.Register("Caption", typeof(string), typeof(TextButton));

                private static readonly DependencyPropertyKey CountPropertyKey =
                    DependencyProperty.RegisterReadOnly("Count", typeof(int), typeof(TextButton), null);

                public static readonly DependencyProperty CountProperty = CountPropertyKey.DependencyProperty;

                private string _text;

                public string Text
                {
                    {|FDG9054:get|} { return _text; }
                    {|FDG9054:set|} { _text = value; }
                }

                public string Title
                {
                    {|FDG9054:get|};
                    {|FDG9054:set|};
                }

                public string Caption
                {
                    get => (string)GetValue(CaptionProperty);
                    set => SetValue(CaptionProperty, value);
                }

                public int Count
                {
                    get { return (int)GetValue(CountProperty); }
                    private set { SetValue(CountPropertyKey, value); }
                }

                public string Other { get; set; }
            }
            """,
            ("/Wpf.cs", Wpf));
    }

    [Fact]
    public Task Default_value_set_in_the_constructor_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Windows;

            public class TextButton : DependencyObject
            {
                public static readonly DependencyProperty TextProperty =
                    DependencyProperty.Register("Text", typeof(string), typeof(TextButton), new PropertyMetadata(""));

                public static readonly DependencyProperty SizeProperty =
                    DependencyProperty.Register("Size", typeof(int), typeof(TextButton));

                public static readonly DependencyProperty KindProperty =
                    DependencyProperty.Register("Kind", typeof(Type), typeof(TextButton));

                public TextButton()
                {
                    {|FDG9056:Text = String.Empty|};
                    {|FDG9056:this.Size = 12|};
                    {|FDG9056:SetValue(KindProperty, typeof(string))|};
                }

                public TextButton(string text, int size)
                {
                    Text = text;
                    Size = size;
                    Kind = new Holder().GetType();
                }

                public string Text
                {
                    get { return (string)GetValue(TextProperty); }
                    set { SetValue(TextProperty, value); }
                }

                public int Size
                {
                    get { return (int)GetValue(SizeProperty); }
                    set { SetValue(SizeProperty, value); }
                }

                public Type Kind
                {
                    get { return (Type)GetValue(KindProperty); }
                    set { SetValue(KindProperty, value); }
                }

                public void Reset()
                {
                    Text = String.Empty;
                }
            }

            public class Holder
            {
            }
            """,
            ("/Wpf.cs", Wpf));
    }

    [Fact]
    public Task Code_besides_the_standard_call_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Windows;

            public class TextButton : DependencyObject
            {
                public static readonly DependencyProperty TextProperty =
                    DependencyProperty.Register("Text", typeof(string), typeof(TextButton));

                public static readonly DependencyProperty TitleProperty =
                    DependencyProperty.Register("Title", typeof(string), typeof(TextButton));

                public string Text
                {
                    get { return (string)GetValue(TextProperty); }
                    {|FDG9057:set|}
                    {
                        if (value == null)
                        {
                            throw new ArgumentNullException(nameof(value));
                        }

                        SetValue(TextProperty, value);
                        OnTextChanged();
                    }
                }

                public string Title
                {
                    {|FDG9057:get|}
                    {
                        string title = (string)GetValue(TitleProperty);
                        return title ?? string.Empty;
                    }

                    set { SetValue(TitleProperty, value); }
                }

                private void OnTextChanged()
                {
                }
            }
            """,
            ("/Wpf.cs", Wpf));
    }

    [Fact]
    public Task Nothing_is_reported_without_the_wpf_types()
    {
        return Verifier.VerifyAsync(
            """
            public class DependencyObject
            {
            }

            public class TextButton : DependencyObject
            {
                public static readonly object TextProperty = new object();

                public string Text { get; set; }
            }
            """);
    }
}
