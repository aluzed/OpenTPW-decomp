namespace OpenTPW;

/// <summary>A visitor thought's icon in <c>ui.wad</c> (<c>ti_*</c>, the thought bubbles), by its index in
/// <c>THOUGHTS.str</c> (<see cref="CrowdThoughts"/>).</summary>
internal static class ThoughtIcons
{
	public static string For( int thought ) => thought switch
	{
		1 => "ti_hungry", 2 => "ti_thirsty", 3 => "ti_hungthir", 4 => "ti_toilet", 5 => "ti_bad", 6 => "ti_good",
		7 => "ti_litter", 8 => "ti_suhappy", 9 => "ti_happy", 11 => "ti_sad", 12 => "ti_bored", 13 => "ti_angry",
		14 => "ti_sick", 15 => "ti_scared", 16 => "ti_queuebad", 17 => "ti_confused", _ => "ti_normal",
	};
}
