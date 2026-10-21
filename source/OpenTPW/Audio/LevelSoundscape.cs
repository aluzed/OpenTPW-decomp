using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// The level's music + ambience (T-051 follow-up). Every park world ships its own <c>Music/MusicHD.sdt</c> (short music
/// segments in numbered series, e.g. jungle <c>Level1-1</c>…<c>Level1-9</c>) and <c>Sound/AmbientHD.sdt</c> (one-shot
/// creature/scenery sounds: frogs and screeches, bats and owls, clown horns, alien chatter), but the game always
/// played the global front-end track and looped the first global ambient clip (a flapping flag) in every park.
/// <para>In a park the first music series plays as one continuous looped track — MPEG audio is a plain frame stream,
/// so the segments concatenate — and a random ambient one-shot sounds every few seconds. The front-end keeps the
/// global music and ambient bed.</para>
/// </summary>
public static class LevelSoundscape
{
	private const float AmbientGain = 0.35f;            // one-shots sit under the music / ride sounds
	private const float MinGap = 4f, MaxGap = 11f;      // seconds between two ambient one-shots
	private const int MaxSegments = 9;                  // cap the concatenated track (memory + decode time)

	private static List<MP2File> ambience = new();
	private static float nextAmbient;

	/// <summary>Start the soundscape for park <paramref name="level"/>, or the front-end's when null.</summary>
	public static void Start( string? level )
	{
		ambience = new();
		if ( level == null )
		{
			StartFrontEnd();
			return;
		}

		try
		{
			var music = new SdtArchive( Path.Join( GameDir.GamePath, "data", "levels", level, "Music", "MusicHD.sdt" ) );
			var series = FirstSeries( music.soundFiles.Select( s => s.Name ) );
			var track = music.soundFiles.Where( s => series.Contains( s.Name ) ).OrderBy( s => s.Name, StringComparer.OrdinalIgnoreCase )
				.Take( MaxSegments ).SelectMany( s => s.SoundData ).ToArray();
			if ( track.Length > 0 )
				Audio.PlayMusic( track, loop: true );
			Log.Info( $"[audio] {level}: music series of {Math.Min( series.Count, MaxSegments )} segment(s)" );
		}
		catch ( Exception e ) { Log.Warning( $"[audio] {level} music unavailable: {e.Message}" ); }

		try
		{
			var amb = new SdtArchive( Path.Join( GameDir.GamePath, "data", "levels", level, "Sound", "AmbientHD.sdt" ) );
			ambience = amb.soundFiles.Where( s => s.SoundData.Length > 1024 ).ToList(); // skip placeholders ("blank")
			Audio.StopAmbient(); // the park's ambience is one-shots, not the front-end's looping bed
		}
		catch ( Exception e ) { Log.Warning( $"[audio] {level} ambience unavailable: {e.Message}" ); }

		nextAmbient = Time.Now + MinGap;
	}

	/// <summary>Play the next ambient one-shot when it's due (call once per frame).</summary>
	public static void Update()
	{
		if ( ambience.Count == 0 || Time.Now < nextAmbient )
			return;
		var clip = ambience[Random.Shared.Next( ambience.Count )];
		Audio.PlaySfx( $"ambience/{clip.Name}", clip.SoundData, AmbientGain * Audio.SfxVolume );
		nextAmbient = Time.Now + MinGap + (float)Random.Shared.NextDouble() * (MaxGap - MinGap);
	}

	/// <summary>
	/// The segments of the lowest-numbered music series: names grouped by their <c>level&lt;n&gt;</c> prefix
	/// (case-insensitive; "Level1-3", "level1a" and "Level1-aa" are all series 1). Pure (unit-tested).
	/// </summary>
	public static HashSet<string> FirstSeries( IEnumerable<string> names )
	{
		var bySeries = names
			.Select( n => (Name: n, Match: Regex.Match( n, @"^level(\d+)", RegexOptions.IgnoreCase )) )
			.Where( x => x.Match.Success )
			.GroupBy( x => int.Parse( x.Match.Groups[1].Value ) )
			.OrderBy( g => g.Key )
			.FirstOrDefault();
		return bySeries == null ? new() : bySeries.Select( x => x.Name ).ToHashSet();
	}

	// The front-end: the global calm track + the global ambient bed (the previous behaviour, T-031/T-051).
	private static void StartFrontEnd()
	{
		try
		{
			var music = new SdtArchive( Path.Join( GameDir.GamePath, "data", "global", "sound", "MusicHD.sdt" ) );
			var track = music.soundFiles.FirstOrDefault( x => x.Name.StartsWith( "level4c", StringComparison.OrdinalIgnoreCase ) )
				?? music.soundFiles.FirstOrDefault();
			if ( track != null )
				Audio.PlayMusic( track.SoundData, loop: true );
		}
		catch ( Exception e ) { Log.Warning( $"Background music unavailable: {e.Message}" ); }

		try
		{
			var ambient = new SdtArchive( Path.Join( GameDir.GamePath, "data", "global", "sound", "AmbientHD.sdt" ) );
			if ( ambient.soundFiles.FirstOrDefault() is { } amb )
				Audio.PlayAmbient( amb.SoundData, loop: true );
		}
		catch ( Exception e ) { Log.Warning( $"Ambient audio unavailable: {e.Message}" ); }
	}
}
