using System.Globalization;

namespace OpenTPW;

/// <summary>The level's visitor arrival settings (T-074), from the <c>Arrival.*</c> block of <c>levels/Standard.sam</c>
/// (shipped: MinPeople 1, TimeBetweenArrivals 150, FixedRate 15, NewParkBonus 20, PointsPerVisitor 6).</summary>
public readonly record struct ArrivalSettings( int MinPeople, float TimeBetweenArrivals, float FixedRate, float NewParkBonus, float PointsPerVisitor )
{
	public static ArrivalSettings Defaults => new( 1, 150f, 15f, 20f, 6f );

	public static ArrivalSettings Load( SettingsFile sam )
	{
		float G( string key, float fallback )
			=> float.TryParse( sam[$"Arrival.{key}"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : fallback;
		return new ArrivalSettings( (int)G( "MinPeople", 1f ), G( "TimeBetweenArrivals", 150f ), G( "FixedRate", 15f ),
			G( "NewParkBonus", 20f ), G( "PointsPerVisitor", 6f ) );
	}
}

/// <summary>
/// Brings visitors into the park by the busload (T-074), as the original does (reversed from <c>tp.exe</c>:
/// <c>FUN_004cf3e0</c> / <c>FUN_004cf5b0</c>):
/// <list type="bullet">
/// <item>a bus comes once <c>TimeBetweenArrivals</c> real seconds (150) have passed since the last one left — the
/// game counts world turns ÷ 4;</item>
/// <item>it brings <c>(NewParkBonus + attraction points) × k ÷ PointsPerVisitor</c> visitors, at least
/// <c>MinPeople</c>, where k is 1.2 for a free park and 0.8 when it charges admission, and the attraction points
/// are the park's rides and sideshows (<see cref="Ride.ParkAttractionPoints"/>: each kind of ride once, its
/// <c>AttractionValue</c> plus its <c>NewBonus</c> while it's new);</item>
/// <item>the park never holds more than <see cref="MaxPeepsInPark"/> visitors (the original caps at 500; 250 is
/// OpenTPW's performance cap). <c>FixedRate</c> isn't read by the arrival code.</item>
/// </list>
/// Pure (unit-tested): the caller dispatches what <see cref="Update"/> returns.
/// </summary>
public sealed class ArrivalScheduler
{
	public const int MaxPeepsInPark = 250;

	/// <summary>The bus-size multiplier for a free park / one that charges admission (tp.exe 0x700364 / 0x700368).</summary>
	public const float FreeParkFactor = 1.2f, PaidParkFactor = 0.8f;

	private readonly ArrivalSettings settings;
	private float timer;

	public ArrivalScheduler( ArrivalSettings settings ) => this.settings = settings;

	/// <summary>Seconds between two buses.</summary>
	public float Interval => MathF.Max( 1f, settings.TimeBetweenArrivals );

	/// <summary>How many visitors a bus brings to a park of <paramref name="attractionPoints"/> that does or doesn't
	/// charge admission.</summary>
	public static int GroupSize( ArrivalSettings s, float attractionPoints, bool chargesAdmission )
	{
		float scaled = (s.NewParkBonus + MathF.Max( 0f, attractionPoints )) * (chargesAdmission ? PaidParkFactor : FreeParkFactor);
		int perVisitor = Math.Max( 1, (int)s.PointsPerVisitor );
		return Math.Max( s.MinPeople, (int)scaled / perVisitor );
	}

	/// <summary>Advance by <paramref name="dt"/> seconds; returns how many visitors arrive now (0 between buses),
	/// clipped so the park stays under <see cref="MaxPeepsInPark"/>. The attraction points are only asked for when a
	/// bus is due.</summary>
	public int Update( float dt, Func<float> attractionPoints, bool chargesAdmission, int inPark )
	{
		timer += dt;
		if ( timer < Interval )
			return 0;
		timer -= Interval;

		int group = GroupSize( settings, attractionPoints(), chargesAdmission );
		return Math.Clamp( group, 0, Math.Max( 0, MaxPeepsInPark - inPark ) );
	}
}
