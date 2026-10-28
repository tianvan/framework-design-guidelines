using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.CompoundWordAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class CompoundWordTests
{
    [Fact]
    public Task Book_not_forms_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace {|FDG3006:NameSpace|}.Tools
            {
                public delegate void {|FDG3006:CallBack|}(int {|FDG3006:userName|});

                public class {|FDG3006:HashTable|}
                {
                    public string {|FDG3006:FileName|};

                    public int {|FDG3006:ID|} { get; set; }

                    public bool {|FDG3006:IsWriteable|} { get; set; }

                    public void {|FDG3006:LogIn|}(string {|FDG3006:eMail|})
                    {
                    }

                    public void Read(int {|FDG3006:metaData|}, long {|FDG3006:timeStamp|})
                    {
                    }
                }

                public enum State
                {
                    {|FDG3006:Cancelled|},
                    {|FDG3006:OK|},
                }
            }
            """);
    }

    [Fact]
    public Task Book_pascal_forms_and_other_words_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace Namespace.Tools
            {
                public delegate void Callback(int username);

                public class Hashtable
                {
                    public string Filename;

                    public int Id { get; set; }

                    public int UserId { get; set; }

                    public bool IsWritable { get; set; }

                    public string Identity;

                    public string Okay;

                    public string Pipeline;

                    public void Logon(string email)
                    {
                    }

                    public void Read(int metadata, long timestamp, string filename)
                    {
                    }
                }

                public enum State
                {
                    Canceled,
                    Ok,
                }
            }
            """);
    }

    [Fact]
    public Task Log_off_and_log_on_are_two_words_in_a_method_name()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Session
            {
                public bool {|#0:IsLogOut|} { get; set; }

                public void {|#1:LogOut|}()
                {
                }

                public void {|#2:LogInAsync|}()
                {
                }

                public string {|#3:LogIn|};
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(0).WithArguments("IsLogOut", "LogOut", "Logoff"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(1).WithArguments("LogOut", "LogOut", "LogOff"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(2).WithArguments("LogInAsync", "LogIn", "LogOn"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(3).WithArguments("LogIn", "LogIn", "Logon"));
    }

    [Fact]
    public Task Other_book_not_forms_are_reported()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Directory
            {
                public int {|#0:UserID|};

                public void {|#1:SignOn|}()
                {
                }

                public void {|#2:SignOff|}()
                {
                }

                public const double {|#3:PI|} = 3.14;

                public int[] {|#4:Indices|};

                public int {|#5:Bitflag|};

                public bool {|#6:ShowGridLine|};

                public string {|#7:PlaceHolder|};
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(0).WithArguments("UserID", "ID", "Id"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(1).WithArguments("SignOn", "SignOn", "SignIn"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(2).WithArguments("SignOff", "SignOff", "SignOut"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(3).WithArguments("PI", "PI", "Pi"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(4).WithArguments("Indices", "Indices", "Indexes"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(5).WithArguments("Bitflag", "Bitflag", "BitFlag"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(6)
                .WithArguments("ShowGridLine", "GridLine", "Gridline"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(7)
                .WithArguments("PlaceHolder", "PlaceHolder", "Placeholder"));
    }

    [Fact]
    public Task Plurals_of_not_forms_are_reported_with_the_plural_book_form()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Catalog
            {
                public string[] {|#0:FileNames|};

                public string[] {|#1:userNames|};

                public long[] {|#2:TimeStamps|};

                public string[] {|#3:EndPoints|};

                public int[] {|#4:UserIDs|};

                public int[] {|#5:IDs|};

                public string[] {|#6:HashTablesByName|};
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(0)
                .WithArguments("FileNames", "FileNames", "Filenames"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(1)
                .WithArguments("userNames", "userNames", "usernames"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(2)
                .WithArguments("TimeStamps", "TimeStamps", "Timestamps"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(3)
                .WithArguments("EndPoints", "EndPoints", "Endpoints"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(4).WithArguments("UserIDs", "IDs", "Ids"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(5).WithArguments("IDs", "IDs", "Ids"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(6)
                .WithArguments("HashTablesByName", "HashTables", "Hashtables"));
    }

    [Fact]
    public Task Acronym_plurals_and_words_split_by_digits_or_underscores_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class Settings
            {
                public string[] IDEs;

                public int[] PIDs;

                public string[] OKRs;

                public string[] SupportedIDPs;

                public string File2Name;

                public string File_Name;

                public string Time_Stamp;

                public interface IEMailService
                {
                }
            }
            """);
    }

    [Fact]
    public Task A_not_form_that_is_a_whole_word_between_underscores_is_reported()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Limits
            {
                public const int {|#0:MAX_ID|} = 10;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(0).WithArguments("MAX_ID", "ID", "Id"));
    }

    [Fact]
    public Task Names_that_are_not_externally_visible_or_are_inherited_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            internal class HashTable
            {
                public string FileName;
            }

            public abstract class Base
            {
                private string fileName;

                internal int ID;

                public abstract void Save(string {|FDG3006:fileName|});
            }

            public sealed class Derived : Base
            {
                public override void Save(string fileName)
                {
                    string userName = fileName;
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_found_and_the_book_form()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Account
            {
                public void Rename(string {|#0:userName|})
                {
                }

                public string {|#1:FileName|};
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(0)
                .WithArguments("userName", "userName", "username"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(1)
                .WithArguments("FileName", "FileName", "Filename"));
    }
}
