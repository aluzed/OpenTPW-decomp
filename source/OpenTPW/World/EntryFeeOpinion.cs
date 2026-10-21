using System.Globalization;

namespace OpenTPW;

/// <summary>The entry-fee opinion settings, from <c>PeepInfo.*</c> in <c>levels/Standard.sam</c> (shipped:
/// ExcitementToCostDivisor 4, MinimumEntryFee 20, Cheap/Average/Expensive multipliers 0.75 / 1.25 / 2.0,
/// OpinionFor MuchTooHigh 0 / TooHigh 20 / AboutRight 50 / TooLow 100).</summary>
public readonly record struct EntryFeeSettings(
	float ExcitementToCostDivisor, float MinimumEntryFee,
	float CheapMultiplier, float AverageMultiplier, float ExpensiveMultiplier,
	float OpinionMuchTooHigh, float OpinionTooHigh, float OpinionAboutRight, float OpinionTooLow )
{
	public static EntryFeeSettings Defaults => new( 4f, 20f, 0.75f, 1.25f, 2f, 0f, 20f, 50f, 100f );

	public static EntryFeeSettings Load( SettingsFile sam )
	{
		var d = Defaults;
		float G( string key, float fallback )
			=> float.TryParse( sam[$"PeepInfo.{key}"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : fallback;
		return new EntryFeeSettings(
			G( "ExcitementToCostDivisor", d.ExcitementToCostDivisor ), G( "MinimumEntryFee", d.MinimumEntryFee ),
			G( "CheapPriceMultiplier", d.CheapMultiplier ), G( "AveragePriceMultiplier", d.AverageMultiplier ),
			G( "ExpensivePriceMultiplier", d.ExpensiveMultiplier ),
			G( "OpinionForMuchTooHigh", d.OpinionMuchTooHigh ), G( "OpinionForTooHigh", d.OpinionTooHigh ),
			G( "OpinionForAboutRight", d.OpinionAboutRight ), G( "OpinionForTooLow", d.OpinionTooLow ) );
	}
}

/// <summary>
/// How a visitor feels about the admission fee on arrival (T-074 follow-up). The <c>.sam</c> comments define the
/// price bands: a park's ideal fee is its ride excitement scaled by <c>ExcitementToCostDivisor</c>; up to
/// <c>MinimumEntryFee</c> "everybody always pays up happily"; <c>Cheap × ideal</c> tops "cheap", <c>Average × ideal</c>
/// tops "OK", <c>Expensive × ideal</c> is the bottom of "very expensive". The four <c>OpinionFor*</c> values (0 / 20 /
/// 50 / 100) are read as the visitor's <b>starting happiness</b> — "about right" (50) is exactly the arrival mood the
/// game used before, and a visitor who finds the fee much too high (0) is at once unhappy enough to leave. That
/// reading is an interpretation until the original is reversed. Pure (unit-tested).
/// </summary>
public static class EntryFeeOpinion
{
	public static EntryFeeSettings Settings { get; set; } = EntryFeeSettings.Defaults;

	/// <summary>The fee a park "deserves": its total ride excitement ÷ <c>ExcitementToCostDivisor</c>.</summary>
	public static float IdealFee( EntryFeeSettings s, float parkExcitement ) =>
		s.ExcitementToCostDivisor > 0f ? parkExcitement / s.ExcitementToCostDivisor : 0f;

	/// <summary>The arrival happiness for an admission <paramref name="fee"/> in a park of total ride excitement
	/// <paramref name="parkExcitement"/>.</summary>
	public static float ArrivalHappiness( EntryFeeSettings s, float fee, float parkExcitement )
	{
		if ( fee <= s.MinimumEntryFee ) // the opening fee (InitialAdmissionFee 20) sits exactly on the minimum
			return s.OpinionTooLow;
		float ideal = IdealFee( s, parkExcitement );
		if ( fee <= s.CheapMultiplier * ideal )
			return s.OpinionTooLow;
		if ( fee <= s.AverageMultiplier * ideal )
			return s.OpinionAboutRight;
		if ( fee < s.ExpensiveMultiplier * ideal )
			return s.OpinionTooHigh;
		return s.OpinionMuchTooHigh;
	}

	/// <summary><see cref="ArrivalHappiness(EntryFeeSettings, float, float)"/> for the live park: the current fee and
	/// the summed excitement of its rides (sideshows excluded).</summary>
	public static float ArrivalHappinessNow()
	{
		float fee = ParkFinances.Current?.EntryFee ?? 0f;
		float excitement = Entity.All.OfType<Ride>().Where( r => !r.IsSideshow ).Sum( r => (float)r.Excitement );
		return ArrivalHappiness( Settings, fee, excitement );
	}
}
