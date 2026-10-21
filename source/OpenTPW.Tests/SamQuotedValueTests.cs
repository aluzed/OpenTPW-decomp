using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class SamQuotedValueTests
{
	[TestMethod]
	public void QuotedValuesKeepTheirSpacesAndLoseTheirQuotes()
	{
		var sam = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes(
			"Info.Name\t\t\"Crazy Monkey\"\t# the ride title\nasTextureData[0].pcTextureFilename\t\"Gutter.tga\"\nInfo.Id\t1180\t\tcomment\n" ) ) );
		Assert.AreEqual( "Crazy Monkey", sam["Info.Name"] );
		Assert.AreEqual( "Gutter.tga", sam["asTextureData[0].pcTextureFilename"] );
		Assert.AreEqual( "1180", sam["Info.Id"], "unquoted values still stop at whitespace" );
	}
}
