namespace OpenTPW;

/// <summary>
/// A ride's queue + boarding info for peeps: the walk waypoints leading from the queue's outer end up
/// to the entrance, the world point where riders reappear at the exit, how long a ride lasts, and how
/// many peeps it can hold at once. <see cref="Riders"/> is the live occupancy (peeps board only while
/// it is below <see cref="Capacity"/>, so a queue forms at the entrance when the ride is full).
/// </summary>
public sealed class RideQueue
{
	public Ride Ride { get; }
	public IReadOnlyList<Vector3> Waypoints { get; }
	public Vector3 ExitPoint { get; }
	/// <summary>How long a ride lasts now (the ride's own, which the ride window can lengthen or shorten).</summary>
	public float RideDuration => Ride.RideDuration;

	/// <summary>Live capacity — follows the ride's current upgrade level (T-044).</summary>
	public int Capacity => Ride.Capacity;

	private int riders;

	// The peeps lined up for this ride, front (nearest the entrance) first. A peep's place in line
	// decides where it stands along the queue path, so a visible queue forms instead of a pile.
	private readonly List<Peep> line = new();

	public RideQueue( Ride ride, IReadOnlyList<Vector3> waypoints, Vector3 exitPoint )
	{
		Ride = ride;
		Waypoints = waypoints;
		ExitPoint = exitPoint;
		ride.Queue = this; // back-reference so the ride (and its coaster train) can read occupancy
	}

	/// <summary>How many peeps are currently on the ride.</summary>
	public int Riders => riders;

	/// <summary>A peep can board only while the ride has a free slot and isn't broken down (T-032).</summary>
	public bool HasFreeSlot => riders < Capacity && !Ride.IsBroken && !Ride.IsClosed;

	/// <summary>How many peeps are currently lined up (waiting, not yet aboard).</summary>
	public int LineLength => line.Count;

	/// <summary>Join the back of the queue (idempotent).</summary>
	public void Enqueue( Peep peep )
	{
		if ( !line.Contains( peep ) )
			line.Add( peep );
	}

	/// <summary>Leave the line without riding (a peep giving up / heading home).</summary>
	public void Dequeue( Peep peep ) => line.Remove( peep );

	/// <summary>This peep's place in line: 0 = at the front (the entrance), -1 = not queued.</summary>
	public int PositionOf( Peep peep ) => line.IndexOf( peep );

	/// <summary>
	/// Where the peep <paramref name="position"/> places back from the front should stand: the front
	/// stands at the entrance (last waypoint) and each place back steps one waypoint out along the path,
	/// clamping at the outer end so a longer line simply gathers there.
	/// </summary>
	public Vector3 StandPoint( int position )
	{
		int idx = Math.Clamp( Waypoints.Count - 1 - Math.Max( 0, position ), 0, Waypoints.Count - 1 );
		return Waypoints[idx];
	}

	/// <summary>The front peep boards: leave the line, take a slot, and start the boarding cycle on the
	/// first rider.</summary>
	public void Board( Peep peep )
	{
		line.Remove( peep );
		bool wasEmpty = riders == 0;
		riders++;
		Ride?.NotifyBoarding();      // raise VAR_LETMEON so the ride script loads this rider (T-032)
		if ( wasEmpty )
			Ride?.SetActive( true ); // edge 0 -> 1: kick off the load/start/run cycle
	}

	/// <summary>A peep's ride finishes: free its slot, idling the ride once the last rider leaves.</summary>
	public void Leave()
	{
		riders = Math.Max( 0, riders - 1 );
		if ( riders == 0 )
			Ride?.SetActive( false ); // edge 1 -> 0: run the end/unload cycle, then idle
	}
}
