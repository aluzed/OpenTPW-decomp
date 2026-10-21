using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class ArrivalSchedulerTests
{
	private static readonly ArrivalSettings Shipped = ArrivalSettings.Defaults; // 1, 150, 15, 20, 6

	[TestMethod]
	public void GroupsArriveEveryIntervalAndNotBetween()
	{
		var s = new ArrivalScheduler( Shipped );
		Assert.AreEqual( 15f, s.Interval, 1e-4f, "TimeBetweenArrivals 150 × 0.1 s" );
		Assert.AreEqual( 0, s.Update( 14f, 50f, 999, 0 ) );
		Assert.IsTrue( s.Update( 1.5f, 50f, 999, 0 ) > 0 );
		Assert.AreEqual( 0, s.Update( 1f, 50f, 999, 0 ) );
	}

	[TestMethod]
	public void GroupSizeGrowsWithRatingAndANewParkBonus()
	{
		Assert.AreEqual( 15, ArrivalScheduler.GroupSize( Shipped, -1f, newPark: false ), "no rating yet → FixedRate" );
		Assert.AreEqual( 23, ArrivalScheduler.GroupSize( Shipped, 50f, newPark: false ), "15 + 50/6" );
		Assert.AreEqual( 43, ArrivalScheduler.GroupSize( Shipped, 50f, newPark: true ), "+ NewParkBonus 20" );
		Assert.AreEqual( 5, ArrivalScheduler.GroupSize( new ArrivalSettings( 5, 150f, 0f, 0f, 6f ), 0f, false ), "at least MinPeople" );
	}

	[TestMethod]
	public void TheParkNeverExceedsItsCap()
	{
		var s = new ArrivalScheduler( Shipped );
		Assert.AreEqual( 3, s.Update( 15f, 100f, 0, ArrivalScheduler.MaxPeepsInPark - 3 ) );
		Assert.AreEqual( 0, s.Update( 15f, 100f, 0, ArrivalScheduler.MaxPeepsInPark ) );
	}

	[TestMethod]
	public void SettingsLoadFromTheSam()
	{
		var sam = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes(
			"Arrival.MinPeople\t2\nArrival.TimeBetweenArrivals\t100\nArrival.FixedRate\t10\nArrival.NewParkBonus\t5\nArrival.PointsPerVisitor\t2\n" ) ) );
		Assert.AreEqual( new ArrivalSettings( 2, 100f, 10f, 5f, 2f ), ArrivalSettings.Load( sam ) );
	}
}
