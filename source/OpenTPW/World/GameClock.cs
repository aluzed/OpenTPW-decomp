namespace OpenTPW;

/// <summary>
/// The park's in-game calendar (T-053, T-076): real elapsed seconds → in-game date, the single time source the
/// progression systems key off (finances settle, challenges count down in days, seasons roll). One
/// <see cref="Current"/> per level.
/// <para>The cadence is the original's (reversed from <c>tp.exe</c>, T-076): its "funny time" is a FILETIME that
/// starts at <b>1 January 2000</b> and advances <c>mFunnySecsPerRealSec</c> = <see cref="FunnySecondsPerRealSecond"/>
/// (15000) game seconds per real second — the world turn runs every 8th 31 ms step (≈4 turns/s) and adds
/// <c>15000 / 4</c> seconds. A day therefore lasts 86400 / 15000 = 5.76 s and a month about three minutes. The
/// calendar is Gregorian (31-day January, 28/29-day February), as the original converts the FILETIME to a
/// SYSTEMTIME.</para>
/// <para>Subscribe to <see cref="OnNewDay"/> / <see cref="OnNewMonth"/> for boundary work; both fire once per
/// crossed boundary even if a big <c>dt</c> skips several at once.</para>
/// </summary>
public sealed class GameClock
{
	/// <summary>The active level's clock (null before a level loads).</summary>
	public static GameClock? Current { get; set; }

	/// <summary>The original's <c>mFunnySecsPerRealSec</c> (its clock constructor sets 15000).</summary>
	public const float FunnySecondsPerRealSecond = 15000f;
	/// <summary>Real seconds per in-game day at the original pace (5.76 s).</summary>
	public const float SecondsPerDay = 86400f / FunnySecondsPerRealSecond;
	/// <summary>The nominal month (30 days) the data's "months" and "days" convert with, e.g. a ride's age.</summary>
	public const int DaysPerMonth = 30;
	public const int MonthsPerYear = 12;
	/// <summary>Real seconds per nominal (30-day) month at the original pace (172.8 s).</summary>
	public const float SecondsPerMonth = SecondsPerDay * DaysPerMonth;

	/// <summary>The date the original's funny time starts at.</summary>
	public static readonly DateTime Epoch = new( 2000, 1, 1 );

	/// <summary>Calendar speed-up for development (1 = the original pace). <c>OPENTPW_FAST_CLOCK=1</c> picks
	/// <see cref="FastSpeed"/> (a 30-day month in 8 s, the pace the economy was first tuned at); any other number is
	/// taken as the multiplier itself.</summary>
	public static float Speed { get; set; } = SpeedFromEnvironment();

	/// <summary>The <c>OPENTPW_FAST_CLOCK=1</c> multiplier: a 30-day month in 8 real seconds.</summary>
	public const float FastSpeed = SecondsPerMonth / 8f;

	private static float SpeedFromEnvironment()
	{
		var v = Environment.GetEnvironmentVariable( "OPENTPW_FAST_CLOCK" );
		if ( string.IsNullOrEmpty( v ) || v == "0" )
			return 1f;
		if ( v == "1" )
			return FastSpeed;
		return float.TryParse( v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s ) && s > 0f
			? s : FastSpeed;
	}

	private double gameSeconds; // game ("funny") seconds since the epoch
	private int firedDays;      // in-game days already announced via OnNewDay
	private int firedMonths;    // in-game months already announced via OnNewMonth

	/// <summary>Elapsed time in real seconds at the original pace (what a save stores).</summary>
	public float ElapsedSeconds => (float)(gameSeconds / FunnySecondsPerRealSecond);

	/// <summary>The current in-game date and time.</summary>
	public DateTime Date => Epoch.AddSeconds( gameSeconds );

	/// <summary>Total whole in-game days elapsed since the level started.</summary>
	public int TotalDays => (int)(gameSeconds / 86400.0);
	/// <summary>Total calendar months elapsed (month boundaries crossed).</summary>
	public int TotalMonths => MonthsSinceEpoch( Date );

	/// <summary>Day of the month, 1..31.</summary>
	public int Day => Date.Day;
	/// <summary>Month of the year, 1..<see cref="MonthsPerYear"/>.</summary>
	public int Month => Date.Month;
	/// <summary>Year of play, starting at 1.</summary>
	public int Year => Date.Year - Epoch.Year + 1;
	/// <summary>How far through the current day, 0..1 (midnight = 0).</summary>
	public float DayFraction => (float)(gameSeconds / 86400.0 - Math.Floor( gameSeconds / 86400.0 ));

	private static int MonthsSinceEpoch( DateTime d ) => (d.Year - Epoch.Year) * MonthsPerYear + d.Month - Epoch.Month;

	/// <summary>Fired once for each in-game day crossed (challenges / seasons count down on this).</summary>
	public event Action? OnNewDay;
	/// <summary>Fired once for each in-game month crossed (finances settle on this).</summary>
	public event Action? OnNewMonth;

	/// <summary>Restore the clock to <paramref name="elapsedSeconds"/> real seconds at the original pace (a loaded
	/// save, T-059) without firing any boundary events for the skipped time.</summary>
	public void SetElapsed( float elapsedSeconds )
	{
		gameSeconds = Math.Max( 0.0, elapsedSeconds ) * FunnySecondsPerRealSecond;
		firedDays = TotalDays;
		firedMonths = TotalMonths;
	}

	/// <summary>Advance the clock by <paramref name="dt"/> real seconds, firing day/month boundaries crossed.</summary>
	public void Tick( float dt )
	{
		if ( dt <= 0f )
			return;
		gameSeconds += (double)dt * FunnySecondsPerRealSecond * Speed;

		int days = TotalDays;
		while ( firedDays < days ) { firedDays++; OnNewDay?.Invoke(); }

		int months = TotalMonths;
		while ( firedMonths < months ) { firedMonths++; OnNewMonth?.Invoke(); }
	}
}
