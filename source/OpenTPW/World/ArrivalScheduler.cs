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
/// Brings visitors into the park in groups (T-074) — the original's arrivals come by bus (its arrival object
/// serialises <c>mArrivalRate</c>, <c>mTargetVehicleCapacity</c>, <c>mPeopleOnBus</c>), replacing the fixed crowd of 30
/// recycled peeps. The exact original formula isn't reversed yet, so the five <c>Arrival.*</c> values are used as
/// follows (an interpretation, kept in one place):
/// <list type="bullet">
/// <item>a group arrives every <c>TimeBetweenArrivals</c> × <see cref="TimeUnitSeconds"/> (150 → 15 s);</item>
/// <item>its size is <c>FixedRate</c> + park rating ÷ <c>PointsPerVisitor</c> (+ <c>NewParkBonus</c> while the park is
/// in its first year), at least <c>MinPeople</c> — so a better-rated park draws bigger crowds;</item>
/// <item>the park never holds more than <see cref="MaxPeepsInPark"/> visitors (a performance cap).</item>
/// </list>
/// Pure (unit-tested): the caller spawns what <see cref="Update"/> returns.
/// </summary>
public sealed class ArrivalScheduler
{
	public const float TimeUnitSeconds = 0.1f;
	public const int MaxPeepsInPark = 250;
	public const int NewParkDays = GameClock.DaysPerMonth * GameClock.MonthsPerYear;

	private readonly ArrivalSettings settings;
	private float timer;

	public ArrivalScheduler( ArrivalSettings settings ) => this.settings = settings;

	/// <summary>Seconds between two arrival groups.</summary>
	public float Interval => MathF.Max( 1f, settings.TimeBetweenArrivals * TimeUnitSeconds );

	/// <summary>How many visitors a group brings for a park <paramref name="rating"/> (0–100; negative = no
	/// visitors yet, counted as 0) and whether the park is still new.</summary>
	public static int GroupSize( ArrivalSettings s, float rating, bool newPark )
	{
		float fromRating = s.PointsPerVisitor > 0f ? MathF.Max( 0f, rating ) / s.PointsPerVisitor : 0f;
		float size = s.FixedRate + fromRating + (newPark ? s.NewParkBonus : 0f);
		return Math.Max( s.MinPeople, (int)MathF.Round( size ) );
	}

	/// <summary>Advance by <paramref name="dt"/> seconds; returns how many visitors arrive now (0 between groups),
	/// clipped so the park stays under <see cref="MaxPeepsInPark"/>.</summary>
	public int Update( float dt, float rating, int parkAgeDays, int inPark )
	{
		timer += dt;
		if ( timer < Interval )
			return 0;
		timer -= Interval;

		int group = GroupSize( settings, rating, parkAgeDays < NewParkDays );
		return Math.Clamp( group, 0, Math.Max( 0, MaxPeepsInPark - inPark ) );
	}
}
