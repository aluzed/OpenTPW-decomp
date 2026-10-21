using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class TourFlightTests
{
	private static TourFlight Flat( int cars = 1, Func<float, float, float>? ground = null )
		=> new( new Vector3( 500, 400, 5 ), 0f, cars, ground ?? ((x, y) => 0f), new Random( 3 ) ) { BoardingSeconds = 1f };

	private static void Run( TourFlight f, float seconds, bool running = true )
	{
		for ( float t = 0; t < seconds; t += 0.05f )
			f.Step( 0.05f, running );
	}

	[TestMethod]
	public void WaitsAtTheStationUntilTheRideRuns()
	{
		var f = Flat();
		Run( f, 5f, running: false );
		Assert.AreEqual( TourFlight.State.Boarding, f.Cars[0].State );
		Assert.AreEqual( 5f, f.Cars[0].Position.Z, 1e-3f );
	}

	[TestMethod]
	public void TakesOffClimbsAndToursTheGrid()
	{
		var f = Flat();
		Run( f, 8f );
		var c = f.Cars[0];
		Assert.AreEqual( TourFlight.State.Tour, c.State );
		Assert.IsTrue( c.Position.Z > 30f, $"cruising, at {c.Position.Z}" );
		// Tour waypoints sit on grid-cell centres.
		float Off( float v ) => ((v % TourFlight.Grid) + TourFlight.Grid) % TourFlight.Grid;
		Assert.AreEqual( TourFlight.Grid / 2f, Off( c.Target.X ), 1e-2f );
		Assert.AreEqual( TourFlight.Grid / 2f, Off( c.Target.Y ), 1e-2f );
	}

	[TestMethod]
	public void ComesBackAndLandsAfterTheTour()
	{
		var f = Flat();
		bool landedAgain = false, toured = false;
		for ( float t = 0; t < 120f && !landedAgain; t += 0.05f )
		{
			f.Step( 0.05f, true );
			toured |= f.Cars[0].State == TourFlight.State.Tour;
			landedAgain = toured && f.Cars[0].State == TourFlight.State.Boarding;
		}
		Assert.IsTrue( landedAgain, $"ended in {f.Cars[0].State}" );
		Assert.IsTrue( f.Cars[0].Position.Distance( f.Station ) < 5f, "it glides to a stop on the station" );
	}

	[TestMethod]
	public void OneCarAtTheStationTheOthersHold()
	{
		var f = Flat( cars: 3 );
		Assert.AreEqual( 1, f.Cars.Count( c => c.State == TourFlight.State.Boarding ) );
		Assert.AreEqual( 2, f.Cars.Count( c => c.State == TourFlight.State.Hold ) );
		f.SetCarCount( 1 );
		Assert.AreEqual( TourFlight.State.Boarding, f.Cars.Single().State, "the car at the station stays" );
	}

	[TestMethod]
	public void FliesAboveHighGround()
	{
		// A 60-unit plateau east of x = 600: the tour keeps a clearance over it.
		var f = Flat( ground: ( x, y ) => x > 600 ? 60f : 0f );
		float lowest = float.MaxValue;
		for ( float t = 0; t < 30f; t += 0.05f )
		{
			f.Step( 0.05f, true );
			var c = f.Cars[0];
			if ( c.State == TourFlight.State.Tour && c.Position.X > 700 )
				lowest = MathF.Min( lowest, c.Position.Z );
		}
		Assert.IsTrue( lowest == float.MaxValue || lowest > 60f, $"lowest over the plateau {lowest}" );
	}
}
