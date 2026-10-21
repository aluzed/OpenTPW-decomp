namespace OpenTPW;

/// <summary>
/// What a visitor is thinking right now, in the game's own words (<c>THOUGHTS.str</c>): the entries this models are
/// 1 Hungry, 2 Thirsty, 3 Hungry and thirsty, 4 Need the toilet, 7 Too messy, 8 Super happy!!, 9 Happy!, 10 Okay,
/// 11 Unhappy, 13 Angry, 14 Feeling sick, 16 Queue too long. A pressing need wins over the mood. Pure (unit-tested); the HUD shows the
/// crowd's most common thoughts so the player can see what to fix.
/// </summary>
public static class CrowdThoughts
{
	public const int Hungry = 1, Thirsty = 2, HungryAndThirsty = 3, NeedToilet = 4, TooMessy = 7,
		SuperHappy = 8, Happy = 9, Okay = 10, Unhappy = 11, Angry = 13, FeelingSick = 14, QueueTooLong = 16;

	/// <summary>The share of <c>PeepInfo.VomitCapacity</c> at which a visitor feels sick (and looks for a toilet).</summary>
	public const float SickFraction = 0.6f;

	/// <summary>The thought index for a visitor's state: needs as level ÷ threshold (≥ 1 = pressing), happiness 0–100,
	/// illness as a fraction of the vomit capacity.</summary>
	public static int For( float hunger, float thirst, float bladder, float happiness, bool amidLitter, bool longQueue, float illness = 0f )
	{
		if ( bladder >= 1f )
			return NeedToilet;
		if ( illness >= SickFraction )
			return FeelingSick;
		if ( hunger >= 1f && thirst >= 1f )
			return HungryAndThirsty;
		if ( thirst >= 1f )
			return Thirsty;
		if ( hunger >= 1f )
			return Hungry;
		if ( amidLitter )
			return TooMessy;
		if ( longQueue )
			return QueueTooLong;
		return happiness switch
		{
			>= 85f => SuperHappy,
			>= 65f => Happy,
			>= 40f => Okay,
			>= 15f => Unhappy,
			_ => Angry,
		};
	}

	private static string[]? texts;

	/// <summary>The thought's text from THOUGHTS.str (or a built-in English fallback).</summary>
	public static string Text( int thought )
	{
		if ( texts == null )
		{
			try { texts = new StringFile( GameLanguage.File( "THOUGHTS.str" ) ).Entries; }
			catch { texts = Array.Empty<string>(); }
		}
		if ( thought >= 0 && thought < texts.Length && !string.IsNullOrWhiteSpace( texts[thought] ) )
			return texts[thought].Trim();
		return thought switch
		{
			Hungry => "Hungry", Thirsty => "Thirsty", HungryAndThirsty => "Hungry and thirsty", NeedToilet => "Need the toilet",
			TooMessy => "Too messy", SuperHappy => "Super happy!!", Happy => "Happy!", Okay => "Okay", Unhappy => "Unhappy",
			Angry => "Angry", FeelingSick => "Feeling sick", QueueTooLong => "Queue too long", _ => "",
		};
	}

	/// <summary>The <paramref name="top"/> most common thoughts as "Text NN%" (most common first). Pure.</summary>
	public static string Summary( IReadOnlyDictionary<int, int> tally, int top = 2 )
	{
		int total = tally.Values.Sum();
		if ( total == 0 )
			return "";
		return string.Join( "  ", tally.OrderByDescending( kv => kv.Value ).ThenBy( kv => kv.Key ).Take( top )
			.Select( kv => $"{Text( kv.Key )} {100 * kv.Value / total}%" ) );
	}
}
