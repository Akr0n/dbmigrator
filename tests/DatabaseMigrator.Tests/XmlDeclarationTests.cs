using DatabaseMigrator.Core.Services;

namespace DatabaseMigrator.Tests;

/// <summary>
/// Oracle returns an XMLTYPE as text that starts with its XML declaration, e.g. &lt;?xml version="1.0" encoding="UTF-8"?&gt;.
/// SQL Server keeps an xml column in its own form (the declaration is dropped), but it refuses to read a Unicode literal
/// (N'...') whose declaration names an encoding: "unable to switch the encoding". Values bound for an xml column therefore
/// lose a leading declaration, which the server would discard anyway.
/// </summary>
public class XmlDeclarationTests
{
    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<a>2</a>\n", "<a>2</a>\n")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-16\"?><a/>", "<a/>")]
    [InlineData("<?xml version=\"1.0\"?><a/>", "<a/>")]
    [InlineData("<?xml version='1.0' encoding='UTF-8' standalone='yes'?>\r\n  <a/>", "<a/>")]
    [InlineData("  \n<?xml version=\"1.0\" encoding=\"UTF-8\"?><a/>", "<a/>")]
    public void ALeadingDeclaration_IsRemoved(string value, string expected) =>
        Assert.Equal(expected, DatabaseService.StripXmlDeclaration(value));

    [Theory]
    [InlineData("<a>2</a>")]
    [InlineData("plain text")]
    [InlineData("")]
    [InlineData("<a/><?xml version=\"1.0\"?>")]                          // not at the start: not a declaration
    [InlineData("<?xml-stylesheet href=\"x.xsl\"?><a/>")]                 // a processing instruction, not the declaration
    [InlineData("text <?xml version=\"1.0\"?> inside a sentence")]
    public void AnythingElse_IsLeftAlone(string value) =>
        Assert.Equal(value, DatabaseService.StripXmlDeclaration(value));
}
