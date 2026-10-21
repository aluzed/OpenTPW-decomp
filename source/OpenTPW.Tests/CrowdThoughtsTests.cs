using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace OpenTPW.Tests;

[TestClass]
public class CrowdThoughtsTests
{
	[TestMethod]
	public void PressingNeedsWinOverTheMood()
	{
		Assert.AreEqual( CrowdThoughts.NeedToilet, CrowdThoughts.For( 2f, 2f, 1.1f, 90f, true, true ) );
		Assert.AreEqual( CrowdThoughts.HungryAndThirsty, CrowdThoughts.For( 1f, 1.2f, 0f, 90f, false, false ) );
		Assert.AreEqual( CrowdThoughts.Thirsty, CrowdThoughts.For( 0.5f, 1.2f, 0f, 90f, false, false ) );
		Assert.AreEqual( CrowdThoughts.Hungry, CrowdThoughts.For( 1.5f, 0.2f, 0f, 90f, false, false ) );
		Assert.AreEqual( CrowdThoughts.TooMessy, CrowdThoughts.For( 0f, 0f, 0f, 90f, true, true ) );
		Assert.AreEqual( CrowdThoughts.QueueTooLong, CrowdThoughts.For( 0f, 0f, 0f, 90f, false, true ) );
	}

	[TestMethod]
	public void MoodBands()
	{
		Assert.AreEqual( CrowdThoughts.SuperHappy, CrowdThoughts.For( 0, 0, 0, 90f, false, false ) );
		Assert.AreEqual( CrowdThoughts.Happy, CrowdThoughts.For( 0, 0, 0, 70f, false, false ) );
		Assert.AreEqual( CrowdThoughts.Okay, CrowdThoughts.For( 0, 0, 0, 50f, false, false ) );
		Assert.AreEqual( CrowdThoughts.Unhappy, CrowdThoughts.For( 0, 0, 0, 20f, false, false ) );
		Assert.AreEqual( CrowdThoughts.Angry, CrowdThoughts.For( 0, 0, 0, 5f, false, false ) );
	}

	[TestMethod]
	public void SummaryListsTheMostCommonFirst()
	{
		var tally = new Dictionary<int, int> { [CrowdThoughts.Happy] = 6, [CrowdThoughts.Thirsty] = 3, [CrowdThoughts.Angry] = 1 };
		var s = CrowdThoughts.Summary( tally );
		StringAssert.StartsWith( s, "Happy! 60%" );
		StringAssert.Contains( s, "Thirsty 30%" );
		Assert.IsFalse( s.Contains( "Angry" ), "only the top two" );
		Assert.AreEqual( "", CrowdThoughts.Summary( new Dictionary<int, int>() ) );
	}
}
