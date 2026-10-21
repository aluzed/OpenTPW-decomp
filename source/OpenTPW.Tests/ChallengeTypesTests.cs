using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace OpenTPW.Tests;

[TestClass]
public class ChallengeTypesTests
{
	private static Challenge Ch( int type, int target, int time, int obj = 0, int obj2 = 0, bool checkEnd = false )
		=> new( 1, type, 0, time, target, obj, obj2, 0, 1000f, checkEnd, true );

	[TestMethod]
	public void KeepChallengesAreLostTheDayTheySlip()
	{
		float level = 80f;
		var mgr = new ChallengeManager( new[] { Ch( 25, 50, 3 ) }, _ => level, _ => { } ) { DaysUntilFirst = 1 };
		mgr.OnNewDay(); mgr.Accept();
		mgr.OnNewDay();
		Assert.AreEqual( ChallengeManager.Phase.Active, mgr.State, "held, but not over yet — no early win" );
		level = 40f; mgr.OnNewDay();
		Assert.AreEqual( 1, mgr.Lost, "slipped under 50" );
	}

	[TestMethod]
	public void KeepChallengesAreWonWhenTheTimeIsUp()
	{
		var mgr = new ChallengeManager( new[] { Ch( 27, 70, 2 ) }, _ => 90f, _ => { } ) { DaysUntilFirst = 1 };
		mgr.OnNewDay(); mgr.Accept();
		mgr.OnNewDay(); mgr.OnNewDay();
		Assert.AreEqual( 1, mgr.Won );
	}

	[TestMethod]
	public void BuildAndUpgradeGoalsNeedAtLeastOne()
	{
		// "Build Taptastic Rapids" / "Add upgrade to Taptastic Rapids" leave TargetVal at 0.
		Assert.AreEqual( 1f, ChallengeMetrics.Target( Ch( 30, 0, 30, obj: 4160 ) ) );
		Assert.AreEqual( 1f, ChallengeMetrics.Target( Ch( 31, 0, 30, obj: 4160, obj2: 4502 ) ) );
		Assert.IsTrue( ChallengeMetrics.IsLevel( Ch( 31, 0, 30, obj: 4160, obj2: 4502 ) ), "an upgrade is a level" );
		Assert.IsFalse( ChallengeMetrics.IsLevel( Ch( 31, 0, 30, obj: 4160 ) ), "a build counts since accepting" );
		Assert.AreEqual( 100f, ChallengeMetrics.Target( Ch( 17, 100, 60, obj: 2103 ) ) );

		float built = 0;
		var mgr = new ChallengeManager( new[] { Ch( 30, 0, 30, obj: 4160 ) }, _ => built, _ => { } ) { DaysUntilFirst = 1 };
		mgr.OnNewDay(); mgr.Accept();
		mgr.OnNewDay();
		Assert.AreEqual( ChallengeManager.Phase.Active, mgr.State, "nothing built yet: not won at 0" );
		built = 1; mgr.OnNewDay();
		Assert.AreEqual( 1, mgr.Won );
	}

	[TestMethod]
	public void OnlyChallengesAboutThisWorldsRidesAreAvailable()
	{
		var before = ChallengeMetrics.Objects;
		ChallengeMetrics.Objects = new Dictionary<int, string> { [1180] = "Minecart" };
		try
		{
			Assert.IsTrue( ChallengeMetrics.IsAvailable( Ch( 18, 1, 60, obj: 1180 ) ) );
			Assert.IsFalse( ChallengeMetrics.IsAvailable( Ch( 20, 100, 60, obj: 2103 ) ), "the Jaw Dropper isn't in this world" );
			Assert.IsTrue( ChallengeMetrics.IsAvailable( Ch( 3, 30, 60 ) ), "no ride named" );
			StringAssert.Contains( ChallengeMetrics.Describe( Ch( 18, 1, 60, obj: 1180 ) ), "Minecart" );
		}
		finally { ChallengeMetrics.Objects = before; }
	}
}
