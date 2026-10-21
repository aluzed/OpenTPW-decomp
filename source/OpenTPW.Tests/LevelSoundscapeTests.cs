using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class LevelSoundscapeTests
{
	[TestMethod]
	public void FirstSeriesGroupsByLevelNumberAcrossNamingStyles()
	{
		// jungle style
		var jungle = LevelSoundscape.FirstSeries( new[] { "Level2-1.mp2", "Level1-2.mp2", "Level1-1.mp2", "level3-20.mp2" } );
		CollectionAssert.AreEquivalent( new[] { "Level1-1.mp2", "Level1-2.mp2" }, jungle.ToArray() );

		// hallow style: suffix letters, mixed case, a dash or not
		var hallow = LevelSoundscape.FirstSeries( new[] { "Level1-a.mp2", "level1-aa.mp2", "level2aa.mp2", "level1b.mp2", "Level5-xx.mp2" } );
		CollectionAssert.AreEquivalent( new[] { "Level1-a.mp2", "level1-aa.mp2", "level1b.mp2" }, hallow.ToArray() );

		// series numbering may start above 1 (fantasy / space start at Level2)
		var space = LevelSoundscape.FirstSeries( new[] { "Level3a.mp2", "Level2b.mp2", "level2c.mp2" } );
		CollectionAssert.AreEquivalent( new[] { "Level2b.mp2", "level2c.mp2" }, space.ToArray() );

		Assert.AreEqual( 0, LevelSoundscape.FirstSeries( new[] { "blank.mp2" } ).Count );
	}
}
