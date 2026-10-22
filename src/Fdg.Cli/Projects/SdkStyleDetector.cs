using System.Xml;
using System.Xml.Linq;

namespace Fdg.Cli.Projects;

/// <summary>
/// SDK-style projects have <c>Sdk</c> on the root <c>&lt;Project&gt;</c>, an <c>&lt;Sdk Name=...&gt;</c> element, or an
/// <c>&lt;Import Sdk=...&gt;</c>. Legacy projects have none of these.
/// </summary>
public static class SdkStyleDetector
{
    public static bool IsSdkStyle(string csprojPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(csprojPath);
        }
        catch (XmlException)
        {
            return false;
        }

        XElement? root = document.Root;
        if (root is null || root.Name.LocalName != "Project")
        {
            return false;
        }

        if (root.Attribute("Sdk") is not null)
        {
            return true;
        }

        return root.Elements().Any(e =>
            (e.Name.LocalName == "Sdk" && e.Attribute("Name") is not null) ||
            (e.Name.LocalName == "Import" && e.Attribute("Sdk") is not null));
    }
}
