using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class AdvisorSpeechTests
{
	[TestMethod]
	public void EachTextHasItsRecordedTakes()
	{
		// TAG_SYSTEM 9, "Lots of your visitors are thirsty…": responses 20–22 in tp.exe's table.
		CollectionAssert.AreEqual( new[] { 20, 21, 22 }, AdvisorSpeech.ClipsByText[9] );
		var take = AdvisorSpeech.ClipFor( 9, new Random( 1 ) );
		Assert.IsTrue( take is >= 20 and <= 22 );
		Assert.IsNull( AdvisorSpeech.ClipFor( 2 ), "the welcome (text 2) is the level's local clip" );
	}

	[TestMethod]
	public void AdviceMapsToItsVoice()
	{
		Assert.AreEqual( 9, AdvisorTips.TextIndexOf( "VisitorsThirsty" ) );
		Assert.IsNull( AdvisorTips.TextIndexOf( "WelcomeTutorial" ) );
		Assert.IsTrue( AdvisorSpeech.ClipsByText.Values.All( c => c.All( n => n is >= 1 and <= 641 ) ) );
	}
}
