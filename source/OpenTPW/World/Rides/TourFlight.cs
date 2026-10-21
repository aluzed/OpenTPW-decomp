namespace OpenTPW;

/// <summary>
/// The tour ride's flight (T-048): Jurassic Tours' birds / the Halloween balloons are flying cars with no track
/// (<c>Bumper.WhichTrackType 0</c>), flown by the original's "FLY" sim — <c>FUN_0055abf0</c> (one tick every second
/// 31 ms step, ~16 a second), cars created by <c>FUN_0055a620</c>/<c>FUN_0055a720</c>, waypoints by
/// <c>FUN_0055c350</c> (tour) and <c>FUN_0055c270</c> (holding pattern). Each car runs a state machine:
/// <list type="bullet">
/// <item><b>Boarding</b> at the station; <b>TakeOff</b> to a point a grid cell ahead, climbing to cruise
/// height;</item>
/// <item><b>Tour</b>: a random walk over a 66⅔-unit grid (the original's 20000 internal units) — carry on
/// straight three times in four, else turn; a turn (or high ground ahead) tries one side, the other, straight
/// on, then back, accepting ever higher ground — flown 16⅔ units above the ground (40 at least) for
/// <see cref="TourTicks"/> ticks;</item>
/// <item><b>Hold</b>: circle the four corners 83⅓ units around the station until it's free, then
/// <b>Approach</b> a point short of it and <b>Land</b> on a 1:1 glide slope.</item>
/// </list>
/// Steering is the original's: the turn rate halves towards the bearing error and a tenth of it turns the car;
/// the speed relaxes towards 1.24 × a per-car cruise speed; the climb rate is a damped step towards the target
/// height; the bank follows ¾ of the turn rate. World units (internal / 300); pure, so it's unit-tested.
/// </summary>
public sealed class TourFlight
{
	public enum State { Boarding, TakeOff, Tour, Hold, Approach, Land }

	public const float TicksPerSecond = 1000f / 62f;
	public const float Grid = 20000f / 300f;          // tour waypoint grid
	public const float Clearance = 5000f / 300f;      // height over the ground
	public const float MinTourHeight = 12000f / 300f;
	public const float CruiseHeight = 14000f / 300f;  // take-off / holding floor
	public const float HoldOffset = 25000f / 300f;
	public const float ApproachDistance = 5000f / 300f;
	public const float ArriveDistance = 5000f / 300f;
	public const int TourTicks = 300;                 // the default tour length (~19 s)

	public sealed class Car
	{
		public State State;
		public Vector3 Position;
		public float Heading, Speed, CruiseSpeed, TurnRate, ClimbRate, Bank;
		public Vector3 Target, PreviousTarget;
		public float TargetHeight;
		public int TicksLeft, HoldCorner;
		public float BoardedFor;

		/// <summary>Nose-up angle (radians), from the climb rate.</summary>
		public float Pitch => ClimbRate * MathF.Tau / 4096f;
	}

	private readonly Func<float, float, float> ground;
	private readonly Random random;
	private readonly List<Car> cars = new();
	private float pending;
	private Car? onStation;

	public Vector3 Station { get; }
	public float StationHeading { get; }
	public IReadOnlyList<Car> Cars => cars;

	/// <summary>Seconds a car waits at the station before it takes off with its riders.</summary>
	public float BoardingSeconds { get; set; } = 3f;

	public TourFlight( Vector3 station, float stationHeading, int carCount, Func<float, float, float> groundHeight, Random? random = null )
	{
		Station = station;
		StationHeading = stationHeading;
		ground = groundHeight;
		this.random = random ?? new Random();
		for ( int i = 0; i < Math.Max( 1, carCount ); i++ )
		{
			var car = new Car { CruiseSpeed = (300 + this.random.Next( 100 )) / 300f, Heading = stationHeading };
			if ( i == 0 )
			{
				car.State = State.Boarding;
				car.Position = station;
				onStation = car;
			}
			else
			{
				// The others start in the holding pattern, as the original's late cars fly in and queue.
				car.State = State.Hold;
				car.HoldCorner = i % 4;
				car.Target = HoldPoint( car.HoldCorner );
				car.TargetHeight = HoldHeight;
				car.Position = car.Target with { Z = HoldHeight };
				car.Speed = car.CruiseSpeed;
			}
			cars.Add( car );
		}
	}

	/// <summary>Match the ride's capacity: a new car flies in to the holding pattern; a car too many is dropped
	/// (never the one at the station).</summary>
	public void SetCarCount( int count )
	{
		count = Math.Max( 1, count );
		while ( cars.Count < count )
		{
			var car = new Car { CruiseSpeed = (300 + random.Next( 100 )) / 300f, State = State.Hold, HoldCorner = cars.Count & 3 };
			car.Target = HoldPoint( car.HoldCorner );
			car.TargetHeight = HoldHeight;
			car.Position = HoldPoint( (car.HoldCorner + 2) & 3 ) with { Z = HoldHeight };
			car.Speed = car.CruiseSpeed;
			cars.Add( car );
		}
		while ( cars.Count > count )
		{
			int i = cars.FindLastIndex( c => c != onStation );
			cars.RemoveAt( i );
		}
	}

	private float HoldHeight => MathF.Max( Station.Z + Clearance, CruiseHeight );

	private Vector3 HoldPoint( int corner ) => corner switch
	{
		0 => new( Station.X + HoldOffset, Station.Y + HoldOffset, 0 ),
		1 => new( Station.X + HoldOffset, Station.Y - HoldOffset, 0 ),
		2 => new( Station.X - HoldOffset, Station.Y - HoldOffset, 0 ),
		_ => new( Station.X - HoldOffset, Station.Y + HoldOffset, 0 ),
	};

	/// <summary>Advance <paramref name="dt"/> seconds; cars leave the station only while <paramref name="running"/>.</summary>
	public void Step( float dt, bool running )
	{
		pending += dt * TicksPerSecond;
		for ( ; pending >= 1f; pending -= 1f )
			foreach ( var car in cars )
				Tick( car, running );
	}

	private static float Distance2D( Vector3 a, Vector3 b ) => MathF.Sqrt( (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) );

	private void Tick( Car c, bool running )
	{
		float targetSpeed = c.CruiseSpeed;
		float dist = Distance2D( c.Position, c.Target );
		switch ( c.State )
		{
			case State.Boarding:
				targetSpeed = 0f;
				c.BoardedFor += 1f / TicksPerSecond;
				if ( running && c.BoardedFor >= BoardingSeconds )
				{
					c.State = State.TakeOff;
					c.Target = c.Position + new Vector3( MathF.Cos( c.Heading ), MathF.Sin( c.Heading ), 0 ) * Grid;
					c.TargetHeight = MathF.Max( MathF.Max( ground( c.Target.X, c.Target.Y ), Station.Z ) + Clearance, CruiseHeight );
					c.TicksLeft = TourTicks;
					onStation = null;
				}
				break;
			case State.TakeOff:
				if ( c.Position.Z >= c.TargetHeight )
				{
					c.State = State.Tour;
					c.PreviousTarget = c.Target - new Vector3( Grid, 0, 0 );
					NextTourPoint( c );
				}
				break;
			case State.Tour:
				if ( c.TicksLeft <= 0 )
				{
					c.State = State.Hold;
					c.Target = HoldPoint( c.HoldCorner );
					c.TargetHeight = HoldHeight;
				}
				else if ( dist < ArriveDistance )
					NextTourPoint( c );
				break;
			case State.Hold:
				if ( onStation == null )
				{
					// The station is free: line up a point short of it, on the bearing from the car.
					onStation = c;
					float a = MathF.Atan2( Station.Y - c.Position.Y, Station.X - c.Position.X );
					c.State = State.Approach;
					c.Target = Station - new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0 ) * ApproachDistance;
					c.TargetHeight = HoldHeight;
				}
				else if ( dist < ArriveDistance )
				{
					c.HoldCorner = (c.HoldCorner + 1) & 3;
					c.Target = HoldPoint( c.HoldCorner );
				}
				break;
			case State.Approach:
				if ( dist < ArriveDistance )
				{
					c.State = State.Land;
					c.Target = Station;
				}
				break;
			case State.Land:
				if ( c.Position.Z <= Station.Z )
				{
					c.Position = Station;
					c.State = State.Boarding;
					c.BoardedFor = 0f;
					c.ClimbRate = 0f;
				}
				break;
		}
		c.TicksLeft--;

		// Speed: relax towards 1.24 × the cruise speed (sitting at the station: towards rest, faster).
		float keep = c.State == State.Boarding ? (25 + 100) * 4 / 1024f : (25 + 200) * 4 / 1024f;
		c.Speed = c.Speed * keep + 0.15f * targetSpeed;
		c.Position += new Vector3( MathF.Cos( c.Heading ), MathF.Sin( c.Heading ), 0 ) * c.Speed;
		if ( c.State == State.Boarding )
		{
			c.Bank *= 0.875f;
			return;
		}

		// Steering: the turn rate halves towards the bearing error, a tenth of it turns the car.
		float bearing = MathF.Atan2( c.Target.Y - c.Position.Y, c.Target.X - c.Position.X );
		float error = MathF.IEEERemainder( bearing - c.Heading, MathF.Tau );
		c.TurnRate = (error + c.TurnRate) / 2f;
		c.Heading = MathF.IEEERemainder( c.Heading + c.TurnRate / 10f, MathF.Tau );

		if ( c.State == State.Land )
		{
			// A 1:1 glide slope onto the station.
			float z = (7f * c.Position.Z + Distance2D( c.Position, Station ) + Station.Z - 10f) / 8f;
			c.Position = c.Position with { Z = z };
			c.ClimbRate = 0f;
		}
		else
		{
			// Climb: a damped step towards the target height (internal units, as the original's fixed point).
			float error300 = (c.TargetHeight - c.Position.Z) * 300f;
			float step = Math.Clamp( error300 / 20f, -16f, 16f );
			c.ClimbRate = Math.Clamp( c.ClimbRate + (MathF.Abs( error300 ) > 10000f ? 3f : 1f) * step, -512f, 512f ) * 7f / 8f;
			c.Position += new Vector3( 0, 0, c.ClimbRate * 3f / 300f );
		}
		c.Bank = (7f * c.Bank - 0.75f * c.TurnRate) / 8f;
	}

	// The next tour waypoint (FUN_0055c350): one grid cell on, straight three times in four, else turning; high
	// ground ahead forces a turn, and each round of refusals accepts ground one clearance higher.
	private void NextTourPoint( Car c )
	{
		var d = c.Target - c.PreviousTarget;
		Vector3 ahead = MathF.Abs( d.Y ) < MathF.Abs( d.X )
			? new( MathF.Sign( d.X ) * Grid, 0, 0 )
			: new( 0, MathF.Sign( d.Y ) * Grid, 0 );
		var side = new Vector3( ahead.Y, ahead.X, 0 );
		c.PreviousTarget = c.Target;

		float limit = c.Position.Z + Clearance;
		bool Clear( Vector3 p ) => ground( p.X, p.Y ) < limit;
		Vector3 next;
		if ( random.Next( 4 ) != 0 && Clear( c.Target + ahead ) )
			next = c.Target + ahead;
		else
		{
			next = c.Target + side;
			for ( int guard = 0; !Clear( c.Target + side ) && guard < 64; guard++, limit += Clearance )
			{
				if ( Clear( c.Target - side ) ) { next = c.Target - side; break; }
				if ( Clear( c.Target + ahead ) ) { next = c.Target + ahead; break; }
				if ( Clear( c.Target - ahead ) ) { next = c.Target - ahead; break; }
			}
		}

		// Snap to the grid cell's centre and fly a clearance over the higher of there and here.
		static float Snap( float v ) => MathF.Floor( v / Grid ) * Grid + Grid / 2f;
		c.Target = new Vector3( Snap( next.X ), Snap( next.Y ), 0 );
		float over = MathF.Max( ground( c.Target.X, c.Target.Y ), ground( c.Position.X, c.Position.Y ) );
		c.TargetHeight = MathF.Max( over + Clearance, MinTourHeight );
	}
}
