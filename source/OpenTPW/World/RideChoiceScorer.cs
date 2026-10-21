using System.Globalization;

namespace OpenTPW;

/// <summary>The authored ride-choice decision weights (T-060), from <c>PeepInfo.DecisionVar*</c> in
/// <c>levels/Standard.sam</c>: how much distance / queue length / excitement pull a peep's ride choice, plus
/// the "new ride" bonus (<c>DecisionVariable2</c> multiplier while a ride is younger than
/// <c>DecisionVariable1</c> days). The need weights (thirst/hunger/…) steer toward shops, not rides — see
/// <see cref="NeedChoiceScorer"/>.</summary>
public readonly record struct DecisionWeights( float Distance, float Queue, float Excitement, float NewRideMultiplier, int NewRideDays )
{
	/// <summary>The shipped jungle defaults (dist 1, queue 1, excitement 1, new×5 for 7 days).</summary>
	public static DecisionWeights Defaults => new( 1f, 1f, 1f, 5f, 7 );

	/// <summary>Read the weights from a level settings file (falling back to <see cref="Defaults"/> per key).</summary>
	public static DecisionWeights Load( SettingsFile sam )
	{
		float G( string key, float fallback )
			=> float.TryParse( sam[$"PeepInfo.{key}"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : fallback;
		return new DecisionWeights(
			Distance: G( "DecisionVarDistWeight", 1f ),
			Queue: G( "DecisionVarQueueWeight", 1f ),
			Excitement: G( "DecisionVarExcitementWeight", 1f ),
			NewRideMultiplier: G( "DecisionVariable2", 5f ),
			NewRideDays: (int)G( "DecisionVariable1", 7f ) );
	}
}

/// <summary>The authored need weights (T-060), from <c>PeepInfo.DecisionVar{Thirst,Hunger,Toilet}Weight</c> in
/// <c>levels/Standard.sam</c> (2 / 2 / 2 shipped): how strongly each pressing need pulls a peep toward a stall,
/// traded against the walk (<see cref="DecisionWeights.Distance"/>).</summary>
public readonly record struct NeedWeights( float Thirst, float Hunger, float Toilet )
{
	public static NeedWeights Defaults => new( 2f, 2f, 2f );

	public static NeedWeights Load( SettingsFile sam )
	{
		float G( string key, float fallback )
			=> float.TryParse( sam[$"PeepInfo.{key}"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : fallback;
		return new NeedWeights( G( "DecisionVarThirstWeight", 2f ), G( "DecisionVarHungerWeight", 2f ), G( "DecisionVarToiletWeight", 2f ) );
	}

	public float For( ShopKind kind ) => kind switch { ShopKind.Drink => Thirst, ShopKind.Toilet => Toilet, _ => Hunger };
}

/// <summary>
/// Which stall a peep with pressing needs heads for (T-060). Each stall serving a need whose urgency (level ÷
/// threshold) has reached 1 scores <c>needWeight · urgency − distanceWeight · distance / RefDistance</c>, and the
/// best wins — so a peep that is both thirsty and hungry weighs the two against where the stalls are, instead of
/// always walking to the nearest stall of its single most urgent need. Pure (unit-tested).
/// </summary>
public static class NeedChoiceScorer
{
	public static NeedWeights Weights { get; set; } = NeedWeights.Defaults;

	private const float RefDistance = 200f; // same scale as RideChoiceScorer

	public static float Score( float urgency, float distance, float needWeight, float distanceWeight ) =>
		needWeight * urgency - distanceWeight * (distance / RefDistance);

	/// <summary>Index of the best (kind, urgency, distance) candidate, or -1 when no need has reached its threshold.</summary>
	public static int Choose( IReadOnlyList<(ShopKind Kind, float Urgency, float Distance)> candidates, NeedWeights needs, float distanceWeight )
	{
		int best = -1;
		float bestScore = float.MinValue;
		for ( int i = 0; i < candidates.Count; i++ )
		{
			var (kind, urgency, distance) = candidates[i];
			if ( urgency < 1f )
				continue;
			float score = Score( urgency, distance, needs.For( kind ), distanceWeight );
			if ( score > bestScore )
			{
				best = i;
				bestScore = score;
			}
		}
		return best;
	}
}

/// <summary>One ride a peep is weighing: its excitement (0–100), the world distance to it, how many peeps are
/// already queued, whether it's still "new", and whether it's an indoor (sheltered) attraction.</summary>
public readonly record struct RideOption( float Excitement, float Distance, int QueueLength, bool IsNew, bool IsIndoors = false );

/// <summary>
/// The weighted-utility ride scorer (T-060): the original picks a ride by a score over distance / queue /
/// excitement (and a new-ride bonus), with the weights authored in the level <c>.sam</c>. This replaces the
/// previous excitement/rating-only heuristic. Pure (unit-tested); the factors are normalised to comparable
/// 0–1 ranges so the unit weights (≈1) balance, then a peep picks a ride <b>weighted by score</b> (keeping
/// crowd variety) rather than always the single best.
/// </summary>
public static class RideChoiceScorer
{
	/// <summary>The active level's decision weights (loaded from <c>Standard.sam</c>); defaults until set.</summary>
	public static DecisionWeights Weights { get; set; } = DecisionWeights.Defaults;

	private const float RefDistance = 200f; // world units that count as a "full" distance penalty
	private const float RefQueue = 10f;     // queued peeps that count as a "full" queue penalty

	/// <summary>The desirability of <paramref name="o"/> under <paramref name="w"/> (higher = more appealing;
	/// can be negative when a ride is far / heavily queued). <paramref name="indoorBonus"/> (≥0) is added for an
	/// indoor attraction — the caller raises it in bad weather so peeps seek shelter (T-056).
	/// <paramref name="preferredExcitement"/> (0–100) is the peep's own taste (<c>PeepTypes[*].PreferredExcitement</c>,
	/// T-060): when ≥0 the excitement term rewards how <i>close</i> the ride is to that taste (a timid peep is
	/// drawn to gentle rides, a thrill-seeker to intense ones) instead of always favouring the most intense;
	/// pass -1 (the default) to keep the plain "more excitement is better" behaviour.</summary>
	public static float Score( RideOption o, DecisionWeights w, float indoorBonus = 0f, float preferredExcitement = -1f )
	{
		// "Appeal" of the ride's intensity to this peep: a match score (1 at the peep's preferred level, falling
		// off with distance from it) when a taste is given, else the raw normalised excitement.
		float appeal = preferredExcitement >= 0f
			? Math.Clamp( 1f - MathF.Abs( o.Excitement - preferredExcitement ) / 100f, 0f, 1f )
			: o.Excitement / 100f;
		float score = w.Excitement * appeal
			- w.Distance * (o.Distance / RefDistance)
			- w.Queue * (o.QueueLength / RefQueue);
		if ( o.IsNew )
			score += w.NewRideMultiplier * appeal; // a new ride draws extra interest (scaled by how appealing it is)
		if ( o.IsIndoors && indoorBonus > 0f )
			score += indoorBonus; // shelter appeal in rain/snow
		return score;
	}

	/// <summary>Pick an option index weighted by clamped-positive score (so a more appealing ride is chosen
	/// more often, but not exclusively). <paramref name="roll01"/> is a [0,1) sample. Falls back to the
	/// highest score when every option scores ≤ 0, and to the last index when the list is empty of weight.</summary>
	public static int ChooseWeighted( IReadOnlyList<float> scores, float roll01 )
	{
		if ( scores.Count == 0 )
			return -1;

		float total = 0f;
		foreach ( var s in scores )
			total += MathF.Max( 0f, s );

		if ( total <= 0f )
		{
			// All unappealing — take the least-bad.
			int best = 0;
			for ( int i = 1; i < scores.Count; i++ )
				if ( scores[i] > scores[best] ) best = i;
			return best;
		}

		float r = Math.Clamp( roll01, 0f, 0.999999f ) * total;
		for ( int i = 0; i < scores.Count; i++ )
		{
			r -= MathF.Max( 0f, scores[i] );
			if ( r < 0f )
				return i;
		}
		return scores.Count - 1;
	}
}

/// <summary>
/// The peep "types" and their authored ride taste (T-060): each <c>PeepTypes[i]</c> in <c>levels/Standard.sam</c>
/// carries a <c>PreferredExcitement</c> (0–100) — a timid peep prefers gentle rides, a thrill-seeker intense
/// ones. A spawning peep is assigned a random type, and its preference feeds <see cref="RideChoiceScorer.Score"/>.
/// <para>The <c>.sam</c> stores these in the engine's columnar layout: one key path lists several field names
/// (<c>PeepTypes[i].PreferredExcitement.StartingCash.BoredomThreshold</c>) followed by the columns
/// (<c>80  300  40</c>), and <see cref="SAMParser"/> keeps the first column as the value of the whole key.
/// So PreferredExcitement is read as the value of the key that <i>starts with</i> <c>PeepTypes[i].PreferredExcitement</c>.</para>
/// </summary>
public static class PeepTypes
{
	// The shipped jungle values (PeepTypes[0..7].PreferredExcitement), used until a level is loaded / if the
	// key is absent — so the scorer behaves sensibly without the .sam.
	private static readonly float[] DefaultPreferences = { 80f, 65f, 50f, 35f, 65f, 80f, 45f, 80f };

	/// <summary>Per-type preferred excitement (0–100), indexed by peep type; defaults until <see cref="Load"/>.</summary>
	public static IReadOnlyList<float> Preferences { get; set; } = DefaultPreferences;

	/// <summary>How many peep types are defined (≥1).</summary>
	public static int Count => Preferences.Count > 0 ? Preferences.Count : 1;

	/// <summary>The preferred excitement for type <paramref name="type"/> (wrapped into range).</summary>
	public static float PreferredFor( int type )
	{
		var p = Preferences.Count > 0 ? Preferences : DefaultPreferences;
		return p[((type % p.Count) + p.Count) % p.Count];
	}

	/// <summary>Read <c>PeepTypes[i].PreferredExcitement</c> for each defined type from a level settings file,
	/// stopping at the first gap. Falls back to <see cref="DefaultPreferences"/> when none are present.</summary>
	public static List<float> Load( SettingsFile sam )
	{
		var result = new List<float>();
		for ( int i = 0; ; i++ )
		{
			string prefix = $"PeepTypes[{i}].PreferredExcitement";
			var pair = sam.Entries.FirstOrDefault( e => e.Key.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) );
			if ( pair.Key == null || !float.TryParse( pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) )
				break;
			result.Add( Math.Clamp( v, 0f, 100f ) );
		}
		return result.Count > 0 ? result : DefaultPreferences.ToList();
	}
}
