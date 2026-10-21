using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class ArrivalSchedulerTests
{
	private static readonly ArrivalSettings Shipped = ArrivalSettings.Defaults; // 1, 150, 15, 20, 6

	[TestMethod]
	public void ABusComesEveryTimeBetweenArrivalsSeconds()
	{
		var s = new ArrivalScheduler( Shipped );
		Assert.AreEqual( 150f, s.Interval, 1e-4f, "TimeBetweenArrivals is in seconds (world turns / 4)" );
		Assert.AreEqual( 0, s.Update( 149f, () => 100f, true, 0 ) );
		Assert.IsTrue( s.Update( 1.5f, () => 100f, true, 0 ) > 0 );
		Assert.AreEqual( 0, s.Update( 1f, () => 100f, true, 0 ) );
	}

	[TestMethod]
	public void BusloadFollowsTheParksAttractionAndTheFee()
	{
		// (NewParkBonus 20 + points) × 0.8 when charging (1.2 free) ÷ PointsPerVisitor 6 — FUN_004cf5b0.
		Assert.AreEqual( 2, ArrivalScheduler.GroupSize( Shipped, 0f, chargesAdmission: true ), "an empty park: 20 × 0.8 / 6" );
		Assert.AreEqual( 16, ArrivalScheduler.GroupSize( Shipped, 100f, chargesAdmission: true ), "120 × 0.8 = 96 / 6" );
		Assert.AreEqual( 24, ArrivalScheduler.GroupSize( Shipped, 100f, chargesAdmission: false ), "a free park: 120 × 1.2 = 144 / 6" );
		Assert.AreEqual( 5, ArrivalScheduler.GroupSize( new ArrivalSettings( 5, 150f, 0f, 0f, 6f ), 0f, true ), "at least MinPeople" );
	}

	[TestMethod]
	public void TheParkNeverExceedsItsCap()
	{
		var s = new ArrivalScheduler( Shipped );
		Assert.AreEqual( 3, s.Update( 150f, () => 1000f, true, ArrivalScheduler.MaxPeepsInPark - 3 ) );
		Assert.AreEqual( 0, s.Update( 150f, () => 1000f, true, ArrivalScheduler.MaxPeepsInPark ) );
	}

	[TestMethod]
	public void SettingsLoadFromTheSam()
	{
		var sam = new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes(
			"Arrival.MinPeople\t2\nArrival.TimeBetweenArrivals\t100\nArrival.FixedRate\t10\nArrival.NewParkBonus\t5\nArrival.PointsPerVisitor\t2\n" ) ) );
		Assert.AreEqual( new ArrivalSettings( 2, 100f, 10f, 5f, 2f ), ArrivalSettings.Load( sam ) );
	}
}
