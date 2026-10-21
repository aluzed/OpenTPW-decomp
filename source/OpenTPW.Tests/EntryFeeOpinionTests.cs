using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class EntryFeeOpinionTests
{
	private static readonly EntryFeeSettings S = EntryFeeSettings.Defaults;

	[TestMethod]
	public void PriceBandsMapToTheAuthoredOpinions()
	{
		// 5 rides of excitement 80 → 400 / 4 = an ideal fee of 100.
		const float park = 400f;
		Assert.AreEqual( 100f, EntryFeeOpinion.IdealFee( S, park ) );
		Assert.AreEqual( 100f, EntryFeeOpinion.ArrivalHappiness( S, 15f, park ), "under MinimumEntryFee: happy" );
		Assert.AreEqual( 100f, EntryFeeOpinion.ArrivalHappiness( S, 70f, park ), "≤ 0.75 × ideal: cheap" );
		Assert.AreEqual( 50f, EntryFeeOpinion.ArrivalHappiness( S, 120f, park ), "≤ 1.25 × ideal: about right" );
		Assert.AreEqual( 20f, EntryFeeOpinion.ArrivalHappiness( S, 150f, park ), "below 2 × ideal: too high" );
		Assert.AreEqual( 0f, EntryFeeOpinion.ArrivalHappiness( S, 200f, park ), "≥ 2 × ideal: much too high" );
	}

	[TestMethod]
	public void AnEmptyParkOnlyJustifiesTheMinimumFee()
	{
		Assert.AreEqual( 100f, EntryFeeOpinion.ArrivalHappiness( S, 10f, 0f ) );
		Assert.AreEqual( 0f, EntryFeeOpinion.ArrivalHappiness( S, 25f, 0f ) );
	}

	[TestMethod]
	public void SettingsLoadFromTheSam()
	{
		var sam = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes(
			"PeepInfo.MinimumEntryFee\t30\nPeepInfo.OpinionForAboutRight\t60\n" ) ) );
		var s = EntryFeeSettings.Load( sam );
		Assert.AreEqual( 30f, s.MinimumEntryFee );
		Assert.AreEqual( 60f, s.OpinionAboutRight );
		Assert.AreEqual( 4f, s.ExcitementToCostDivisor, "unset keys keep the shipped defaults" );
	}
}
