using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class GameClockTests
{
	// Half an in-game day in seconds — added to land safely past a boundary (never exactly on it).
	private const float HalfDay = GameClock.SecondsPerDay * 0.5f;
	private const float January = GameClock.SecondsPerDay * 31f; // the calendar starts on 1 January 2000

	[TestInitialize]
	public void OriginalPace() => GameClock.Speed = 1f;

	[TestMethod]
	public void DayLastsTheOriginalFiveSecondsSeventySix()
	{
		// tp.exe: mFunnySecsPerRealSec = 15000 → 86400 / 15000 s per day.
		Assert.AreEqual( 5.76f, GameClock.SecondsPerDay, 1e-4f );
		var c = new GameClock();
		c.Tick( 5.7f );
		Assert.AreEqual( 0, c.TotalDays );
		c.Tick( 0.1f );
		Assert.AreEqual( 1, c.TotalDays );
		Assert.AreEqual( 2, c.Day );
	}

	[TestMethod]
	public void FollowsTheGregorianCalendarFrom2000()
	{
		var c = new GameClock();
		c.Tick( GameClock.SecondsPerDay * (31 + 29) + HalfDay ); // 2000 is a leap year: Jan 31 + Feb 29 days
		Assert.AreEqual( 3, c.Month );
		Assert.AreEqual( 1, c.Day );
		Assert.AreEqual( 2, c.TotalMonths );
	}

	[TestMethod]
	public void FastClockCompressesTheMonth()
	{
		GameClock.Speed = GameClock.FastSpeed;
		var c = new GameClock();
		c.Tick( 8f * 31f / 30f + 0.1f ); // a 31-day January at 8 s per 30 days
		Assert.AreEqual( 2, c.Month );
		GameClock.Speed = 1f;
	}

	[TestMethod]
	public void DayMonthYearAdvanceWithTime()
	{
		var c = new GameClock();
		Assert.AreEqual( 1, c.Day );
		Assert.AreEqual( 1, c.Month );
		Assert.AreEqual( 1, c.Year );

		c.Tick( January + HalfDay ); // 31.5 in-game days → start of month 2
		Assert.AreEqual( 31, c.TotalDays );
		Assert.AreEqual( 1, c.TotalMonths );
		Assert.AreEqual( 1, c.Day );
		Assert.AreEqual( 2, c.Month );
		Assert.AreEqual( 1, c.Year );

		c.Tick( GameClock.SecondsPerDay * 366f ); // +366 days (leap 2000) → next year
		Assert.AreEqual( 2, c.Year );
	}

	[TestMethod]
	public void FiresOneDayAndMonthEventPerBoundary()
	{
		int days = 0, months = 0;
		var c = new GameClock();
		c.OnNewDay += () => days++;
		c.OnNewMonth += () => months++;

		c.Tick( January + HalfDay ); // 31.5 days = 31 day-boundaries, 1 month-boundary
		Assert.AreEqual( 31, days );
		Assert.AreEqual( 1, months );
	}

	[TestMethod]
	public void BigTickFiresEachBoundaryExactlyOnce()
	{
		int days = 0, months = 0;
		var c = new GameClock();
		c.OnNewDay += () => days++;
		c.OnNewMonth += () => months++;

		c.Tick( GameClock.SecondsPerDay * 91f + HalfDay ); // Jan + Feb (leap) + Mar = 91 days in a single big tick
		Assert.AreEqual( 91, days, "every crossed day fires once, even in one jump" );
		Assert.AreEqual( 3, months );
	}

	[TestMethod]
	public void DrivesMonthlyFinanceSettlement()
	{
		// The clock is the time source: finances settle on OnNewMonth, not their own timer.
		var fin = new ParkFinances( starting: 1000f, entryFee: 0f );
		var c = new GameClock();
		c.OnNewMonth += fin.SettleMonth;

		fin.TakeRideTicket( 50f );
		Assert.AreEqual( 0, fin.History.Count, "no settle before a month elapses" );

		c.Tick( January + HalfDay );
		Assert.AreEqual( 1, fin.History.Count, "one month settled → one finance sample" );
		Assert.AreEqual( 1050f, fin.History[0].Balance, 1e-3f );
	}
}
