using System.Globalization;

namespace OpenTPW;

/// <summary>
/// Staff grades and training (T-049 follow-up), from <c>levels/Standard.sam</c>: every member of staff has a grade
/// 0–4. Better-trained staff are paid more (a monthly <c>PerGradeStaffConsts[g].BaseWage</c> 4 / 5 / 6 / 8 / 12, times
/// the role's <c>PayMultiplier</c> — <see cref="Staff.MonthlyWage"/>), rest less (<c>IdleDuration</c>), recover faster (<c>RecuperationRate</c>,
/// <c>HappinessRecuperationRate</c>) and work better: a handyman sees litter further (<c>DetectionRange</c> cells),
/// a mechanic repairs faster (<c>WorkDuration</c>), an entertainer reaches further and cheers harder
/// (<c>ActivationDistance</c>, <c>HappinessEffectOnCell</c>), a researcher researches faster
/// (<c>ResearchAbility</c>). Training raises a grade one percent at a time at <c>PoundsPerTrainingPoint</c>; the
/// staff pool hires around <c>StaffPoolInfo.AvgGradeOf*</c>, with a <c>ChanceToGetGreat*</c> percent of a top
/// recruit. Durations are in the original's world turns (4 a second). Pure (unit-tested).
/// </summary>
public sealed class StaffGrades
{
	public const int MaxGrade = 4;
	public const int PointsPerGrade = 100;
	/// <summary>The original's world turns per real second (T-076): authored durations are in turns.</summary>
	public const float TurnsPerSecond = 4f;

	public float[] BaseWage { get; init; } = { 4, 5, 6, 8, 12 };
	public float[] IdleDuration { get; init; } = { 40, 30, 20, 10, 5 };
	public float[] RecuperationRate { get; init; } = { 0.2f, 0.3f, 0.4f, 0.5f, 0.75f };
	public float[] HappinessRecuperationRate { get; init; } = { 1, 2, 2, 3, 3 };
	public float RestLevel { get; init; } = 1f;
	public float HappyHitNoRestArea { get; init; } = 2f;

	public float[] HandymanDetectionRange { get; init; } = { 2, 3, 3, 4, 5 };
	public float[] MechanicWorkDuration { get; init; } = { 80, 60, 40, 30, 20 };
	public float[] GuardWorkDuration { get; init; } = { 10, 20, 30, 50, 75 };
	public float[] EntertainerHappinessEffect { get; init; } = { 4, 8, 12, 16, 20 };
	public float[] EntertainerActivationDistance { get; init; } = { 3, 3, 4, 4, 5 };
	public float[] ResearchAbility { get; init; } = { 2, 3, 4, 5, 6 };

	// Per role, in the .sam's type order: handyman, mechanic, entertainer, guard, researcher.
	private readonly Dictionary<StaffRole, float[]> poundsPerPoint = new()
	{
		[StaffRole.Handyman] = new float[] { 5, 8, 12, 15, 0 },
		[StaffRole.Mechanic] = new float[] { 5, 8, 12, 15, 0 },
		[StaffRole.Entertainer] = new float[] { 5, 8, 12, 15, 0 },
		[StaffRole.Guard] = new float[] { 5, 8, 12, 15, 0 },
		[StaffRole.Researcher] = new float[] { 8, 12, 15, 18, 0 },
	};
	private readonly Dictionary<StaffRole, float> averageGrade = new()
	{
		[StaffRole.Handyman] = 2, [StaffRole.Mechanic] = 1, [StaffRole.Entertainer] = 1,
		[StaffRole.Guard] = 2, [StaffRole.Researcher] = 1,
	};
	// StaffPoolInfo.Max<Role>InPark: how many of each a park may employ.
	private readonly Dictionary<StaffRole, int> maxInPark = new()
	{
		[StaffRole.Handyman] = 30, [StaffRole.Mechanic] = 15, [StaffRole.Entertainer] = 30,
		[StaffRole.Guard] = 15, [StaffRole.Researcher] = 10,
	};

	/// <summary>How many of <paramref name="role"/> a park may employ (<c>StaffPoolInfo.Max&lt;Role&gt;InPark</c>).</summary>
	public int MaxInPark( StaffRole role ) => maxInPark.GetValueOrDefault( role, 30 );

	private readonly Dictionary<StaffRole, float> chanceOfGreat = new()
	{
		[StaffRole.Handyman] = 20, [StaffRole.Mechanic] = 10, [StaffRole.Entertainer] = 35,
		[StaffRole.Guard] = 2, [StaffRole.Researcher] = 15,
	};

	/// <summary>The active level's tables (the shipped values until a level loads its .sam).</summary>
	public static StaffGrades Current { get; set; } = new();

	private static string SamName( StaffRole role ) => role switch
	{
		StaffRole.Handyman => "Handymen", StaffRole.Mechanic => "Mechanics", StaffRole.Entertainer => "Entertainers",
		StaffRole.Guard => "Guards", _ => "Researchers",
	};

	private static string ConstsName( StaffRole role ) => role switch
	{
		StaffRole.Handyman => "Handyman", StaffRole.Mechanic => "Mechanic", StaffRole.Entertainer => "Entertainer",
		StaffRole.Guard => "Guard", _ => "Researcher",
	};

	/// <summary>Read the tables from a level's <c>Standard.sam</c> (missing keys keep the shipped values).</summary>
	public static StaffGrades Load( SettingsFile sam )
	{
		float? F( string key ) => float.TryParse( sam[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : null;
		float[] Table( string prefix, string field, float[] fallback )
			=> Enumerable.Range( 0, MaxGrade + 1 ).Select( g => F( $"{prefix}[{g}].{field}" ) ?? fallback[g] ).ToArray();

		var d = new StaffGrades();
		var s = new StaffGrades
		{
			BaseWage = Table( "PerGradeStaffConsts", "BaseWage", d.BaseWage ),
			IdleDuration = Table( "PerGradeStaffConsts", "IdleDuration", d.IdleDuration ),
			RecuperationRate = Table( "PerGradeStaffConsts", "RecuperationRate", d.RecuperationRate ),
			HappinessRecuperationRate = Table( "PerGradeStaffConsts", "HappinessRecuperationRate", d.HappinessRecuperationRate ),
			RestLevel = F( "AllStaffConstants.RestLevel" ) ?? d.RestLevel,
			HappyHitNoRestArea = F( "AllStaffConstants.HappyHitCosNoRestArea" ) ?? d.HappyHitNoRestArea,
			HandymanDetectionRange = Table( "HandymanConstsPerGrade", "DetectionRange", d.HandymanDetectionRange ),
			MechanicWorkDuration = Table( "MechanicConstsPerGrade", "WorkDuration", d.MechanicWorkDuration ),
			GuardWorkDuration = Table( "GuardConstsPerGrade", "WorkDuration", d.GuardWorkDuration ),
			EntertainerHappinessEffect = Table( "EntertainerConstsPerGrade", "HappinessEffectOnCell", d.EntertainerHappinessEffect ),
			EntertainerActivationDistance = Table( "EntertainerConstsPerGrade", "ActivationDistance", d.EntertainerActivationDistance ),
			ResearchAbility = Table( "ResearcherConstsPerGrade", "ResearchAbility", d.ResearchAbility ),
		};
		foreach ( var role in Enum.GetValues<StaffRole>() )
		{
			s.poundsPerPoint[role] = Table( $"{ConstsName( role )}ConstsPerGrade", "PoundsPerTrainingPoint", d.poundsPerPoint[role] );
			s.averageGrade[role] = F( $"StaffPoolInfo.AvgGradeOf{SamName( role )}" ) ?? d.averageGrade[role];
			s.chanceOfGreat[role] = F( $"StaffPoolInfo.ChanceToGetGreat{SamName( role )}" ) ?? d.chanceOfGreat[role];
			s.maxInPark[role] = (int)(F( $"StaffPoolInfo.Max{SamName( role )}InPark" ) ?? d.maxInPark[role]);
		}
		return s;
	}

	private static int Clamp( int grade ) => Math.Clamp( grade, 0, MaxGrade );

	/// <summary>The grade of a new recruit: the pool's average grade for the role, one either side at random, or
	/// (<c>ChanceToGetGreat</c> percent of the time) two above.</summary>
	public int RollGrade( StaffRole role, Random rng )
	{
		int avg = (int)MathF.Round( averageGrade[role] );
		if ( rng.NextDouble() * 100.0 < chanceOfGreat[role] )
			return Clamp( avg + 2 );
		return Clamp( avg + rng.Next( -1, 2 ) );
	}

	/// <summary>What one training percent costs at <paramref name="grade"/> (0 at the top grade: it can't improve).</summary>
	public float CostPerTrainingPoint( StaffRole role, int grade ) => poundsPerPoint[role][Clamp( grade )];

	/// <summary>Seconds a member of staff idles between jobs.</summary>
	public float IdleSeconds( int grade ) => IdleDuration[Clamp( grade )] / TurnsPerSecond;

	/// <summary>Seconds a mechanic of this grade spends on a repair.</summary>
	public float RepairSeconds( int grade ) => MechanicWorkDuration[Clamp( grade )] / TurnsPerSecond;

	/// <summary>Seconds a guard of this grade keeps after one troublemaker before giving up.</summary>
	public float PursuitSeconds( int grade ) => GuardWorkDuration[Clamp( grade )] / TurnsPerSecond;

	/// <summary>Energy a resting member of staff gets back per second (<c>RecuperationRate</c> per turn).</summary>
	public float EnergyRecoveryPerSecond( int grade ) => RecuperationRate[Clamp( grade )] * TurnsPerSecond;

	/// <summary>A researcher's speed relative to a grade-1 researcher.</summary>
	public float ResearchFactor( int grade ) => ResearchAbility[Clamp( grade )] / MathF.Max( 1e-3f, ResearchAbility[1] );
}
