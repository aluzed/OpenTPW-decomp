using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class StaffDepthTests
{
	[TestMethod]
	public void GradesScaleWagesAndWorkFromTheShippedTables()
	{
		var g = new StaffGrades();
		Assert.AreEqual( 1f, g.WageFactor( 1 ), 1e-5f, "grade 1 is the tuned wage level" );
		Assert.AreEqual( 12f / 5f, g.WageFactor( 4 ), 1e-5f, "BaseWage 12 vs 5" );
		Assert.AreEqual( 20f, g.RepairSeconds( 0 ), 1e-5f, "MechanicConstsPerGrade[0].WorkDuration 80 turns" );
		Assert.AreEqual( 5f, g.RepairSeconds( 4 ), 1e-5f );
		Assert.AreEqual( 2f, g.ResearchFactor( 4 ), 1e-5f, "ResearchAbility 6 vs 3" );
		Assert.AreEqual( 0f, g.CostPerTrainingPoint( StaffRole.Handyman, 4 ), "the top grade can't be trained" );
		Assert.AreEqual( 8f, g.CostPerTrainingPoint( StaffRole.Researcher, 0 ) );
	}

	[TestMethod]
	public void RecruitsCentreOnThePoolsAverageGrade()
	{
		var g = new StaffGrades();
		var rng = new Random( 7 );
		var guards = Enumerable.Range( 0, 2000 ).Select( _ => g.RollGrade( StaffRole.Guard, rng ) ).ToList();
		Assert.IsTrue( guards.All( x => x is >= 1 and <= 4 ), "AvgGradeOfGuards 2 → 1..3, or 4 when great" );
		Assert.AreEqual( 2.0, guards.Average(), 0.15 );
		var entertainers = Enumerable.Range( 0, 2000 ).Select( _ => g.RollGrade( StaffRole.Entertainer, rng ) ).ToList();
		Assert.AreEqual( 0.35, entertainers.Count( x => x == 3 ) / 2000.0, 0.05, "ChanceToGetGreatEntertainers 35%" );
	}

	[TestMethod]
	public void GrievanceNeedsMoreThanThreeTiredOrUnhappyStaff()
	{
		Assert.IsFalse( StaffStrikes.HasGrievance( 3, 0f, 0f ), "three can't strike" );
		Assert.IsTrue( StaffStrikes.HasGrievance( 4, 10f, 80f ), "through fatigue" );
		Assert.IsTrue( StaffStrikes.HasGrievance( 4, 80f, 10f ), "through unhappiness" );
		Assert.IsFalse( StaffStrikes.HasGrievance( 4, 50f, 50f ) );
	}

	[TestMethod]
	public void StrikesEscalateFromTheTwentyFourthMonth()
	{
		var s = new StaffStrikes();
		s.ConsiderMonth( StaffRole.Handyman, 23, grievance: true );
		Assert.AreEqual( 0, s.Level( StaffRole.Handyman ), "not before month 24" );

		s.ConsiderMonth( StaffRole.Handyman, 24, true );
		Assert.AreEqual( 1, s.Level( StaffRole.Handyman ) );
		Assert.IsFalse( s.IsStriking( StaffRole.Handyman ), "a warning first" );
		CollectionAssert.AreEqual( new[] { "StaffUnhappyHandymen" }, s.TakeNotices() );

		s.OnNewDay( 15 );
		CollectionAssert.AreEqual( new[] { "StaffVeryUnhappyHandymen" }, s.TakeNotices(), "the mid-month reminder" );

		s.ConsiderMonth( StaffRole.Handyman, 25, true );
		Assert.IsTrue( s.IsStriking( StaffRole.Handyman ), "then a week's strike" );
		CollectionAssert.AreEqual( new[] { "StaffStrikingHandymen" }, s.TakeNotices() );
		for ( int d = 0; d < 7; d++ )
			s.OnNewDay( 2 + d );
		Assert.IsFalse( s.IsStriking( StaffRole.Handyman ) );
		CollectionAssert.AreEqual( new[] { "StrikeEndBadHandymen" }, s.TakeNotices() );

		s.ConsiderMonth( StaffRole.Handyman, 26, true );
		Assert.AreEqual( 3, s.Level( StaffRole.Handyman ), "two weeks next" );
		s.ConsiderMonth( StaffRole.Handyman, 27, false );
		Assert.AreEqual( 0, s.Level( StaffRole.Handyman ), "content again" );
	}

	[TestMethod]
	public void AWarningCalledOffThanksThePlayer()
	{
		var s = new StaffStrikes();
		s.ConsiderMonth( StaffRole.Mechanic, 30, true );
		s.TakeNotices();
		s.ConsiderMonth( StaffRole.Mechanic, 31, false );
		CollectionAssert.AreEqual( new[] { "StaffHappierMechanics" }, s.TakeNotices() );
	}

	[TestMethod]
	public void AdvisorSpeaksStrikesAndPoorTraining()
	{
		var c = new AdvisorConfig( Array.Empty<SettingsPair>() );
		var f = new ParkFacilities( 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 100,
			AverageGrades: new[] { 0.5f, 2f, float.NaN, float.NaN, float.NaN },
			StaffNotices: new[] { "StaffStrikingHandymen" } );
		var ids = AdvisorAdvice.Evaluate( new ParkSnapshot( 1000f, 0, 0, 0, 50f, false, Facilities: f ), c ).Select( x => x.Id ).ToList();
		CollectionAssert.Contains( ids, "StaffStrikingHandymen" );
		CollectionAssert.Contains( ids, "StaffTrainMechanics", "mechanics average grade 0.5 < WorseThan 1" );
		CollectionAssert.DoesNotContain( ids, "StaffTrainHandymen" );
	}
}
