using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class NeedChoiceScorerTests
{
	[TestMethod]
	public void NoNeedPastItsThresholdMeansNoDetour()
	{
		var c = new[] { (ShopKind.Drink, 0.9f, 10f), (ShopKind.Food, 0.5f, 5f) };
		Assert.AreEqual( -1, NeedChoiceScorer.Choose( c, NeedWeights.Defaults, 1f ) );
	}

	[TestMethod]
	public void TheNearerOfTwoStallsForTheSameNeedWins()
	{
		var c = new[] { (ShopKind.Drink, 1.2f, 300f), (ShopKind.Drink, 1.2f, 40f) };
		Assert.AreEqual( 1, NeedChoiceScorer.Choose( c, NeedWeights.Defaults, 1f ) );
	}

	[TestMethod]
	public void AMoreUrgentNeedCanJustifyALongerWalk()
	{
		// Mildly hungry next to a food stall vs very thirsty with the drink stall further away.
		var c = new[] { (ShopKind.Food, 1.0f, 20f), (ShopKind.Drink, 1.8f, 150f) };
		Assert.AreEqual( 1, NeedChoiceScorer.Choose( c, NeedWeights.Defaults, 1f ) );
	}

	[TestMethod]
	public void TheAuthoredWeightsSteerTheChoice()
	{
		var c = new[] { (ShopKind.Food, 1.2f, 50f), (ShopKind.Toilet, 1.2f, 50f) };
		Assert.AreEqual( 1, NeedChoiceScorer.Choose( c, new NeedWeights( 2f, 1f, 3f ), 1f ), "toilet weighs more" );
		Assert.AreEqual( 0, NeedChoiceScorer.Choose( c, new NeedWeights( 2f, 3f, 1f ), 1f ), "hunger weighs more" );
	}

	[TestMethod]
	public void WeightsLoadFromTheSamWithDefaults()
	{
		var sam = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes(
			"PeepInfo.DecisionVarThirstWeight\t3\nPeepInfo.DecisionVarToiletWeight\t4\n" ) ) );
		Assert.AreEqual( new NeedWeights( 3f, 2f, 4f ), NeedWeights.Load( sam ) );
	}
}
