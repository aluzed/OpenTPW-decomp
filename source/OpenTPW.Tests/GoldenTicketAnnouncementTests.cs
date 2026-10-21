using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class GoldenTicketAnnouncementTests
{
	[TestMethod]
	public void AnnouncementsFallBackWithoutTheGameText()
	{
		// Without TAG_SYSTEM.str loaded the announcement still names the goal and its target.
		var (headline, sentence) = GoldenTicket.Announcement( "Unknown goal", 42f, 0 );
		Assert.AreEqual( "YOU'VE WON A GOLDEN TICKET!", headline );
		StringAssert.Contains( sentence, "42" );
	}

	[TestMethod]
	public void GlobalsParseFromTheSam()
	{
		var sam = new SettingsFile( new System.IO.MemoryStream( System.Text.Encoding.ASCII.GetBytes(
			"GoldenTicketGlobal.WaterLength\t50\nGoldenTicketGlobal.MinCellsCovered\t2000\n" ) ) );
		Assert.AreEqual( new GoldenTicketGlobals( 50, 2000 ), GoldenTicketGlobals.Parse( sam ) );
	}
}
