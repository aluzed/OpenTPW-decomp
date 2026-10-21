using System.IO;
using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// A native OpenTPW save (T-059, Route A): a versioned JSON snapshot of the park — the balance + loans, the
/// in-game clock, the placed rides/shops (their catalog name, tile, rotation, ticket price, research/upgrade
/// level + mechanical condition), the hired staff (role + position + patrol zone), the player-built coaster
/// tracks, and the goal progression (the active/offered challenge + the golden-ticket win flag). Capturing +
/// applying live in <see cref="Level"/> (which owns the placement pipeline); this is the serializable shape +
/// fault-tolerant file I/O (mirrors <c>GameSettings</c>), with <see cref="SlotPath"/> save slots. Peeps are
/// transient (they respawn); the restored clock keeps the calendar/loan timing aligned. Versioning is
/// additive + back-compatible — older saves simply lack the newer blocks, which default to "none".
/// </summary>
public sealed class SaveGame
{
	public int Version { get; set; } = 3;
	public float Money { get; set; }
	public float EntryFee { get; set; }
	public float ClockSeconds { get; set; }
	public List<LoanState> Loans { get; set; } = new();
	public List<Placement> Placements { get; set; } = new();
	public List<StaffState> Staff { get; set; } = new();
	public List<TrackState> Tracks { get; set; } = new(); // player-built coaster tracks (one per coaster)
	public ChallengeState? Challenge { get; set; }    // active/offered challenge progress (null = none)
	public bool GoldenTicketAwarded { get; set; }     // the level-win flag (T-055), so a won park stays won
	public MetaInfo? Meta { get; set; }               // at-a-glance slot summary (T-061; null on pre-Meta saves)

	public sealed class LoanState
	{
		public string Name { get; set; } = "";
		public bool Bought { get; set; }
		public float Outstanding { get; set; }
		public float Monthly { get; set; }
	}

	public sealed class Placement
	{
		public string Kind { get; set; } = "";   // "ride" | "shop"
		public string Name { get; set; } = "";   // catalog item name (totem / drink / …)
		public int TileX { get; set; }
		public int TileY { get; set; }
		public int Rotation { get; set; }
		public float TicketPrice { get; set; }
		public float Filth { get; set; }         // a toilet's dirtiness (0 clean … 100)

		// Per-ride progression (T-059 v2): research/upgrade level + mechanical condition, so a reloaded park
		// keeps its researched capacity bumps and a ride's reliability/breakdown state. Defaults match a
		// freshly-built ride so a v1 save (which omits these keys) loads as "level 0, fully reliable".
		public int UpgradeLevel { get; set; }
		public int ResearchedLevel { get; set; }
		public bool Researching { get; set; }
		public float ResearchFraction { get; set; }    // 0..1 progress toward the next level
		public int ResearchQueuePos { get; set; } = -1; // park-wide research-queue order (-1 = not queued)
		public float Reliability { get; set; } = 1f;
		public bool Broken { get; set; }
	}

	/// <summary>A hired staff member (T-059 v2): role + wander centre + optional patrol zone. Staff aren't
	/// grid-reserved (they roam), so they're snapshot/respawned separately from the ride/shop placements.</summary>
	public sealed class StaffState
	{
		public string Role { get; set; } = "";    // StaffRole name (Entertainer/Handyman/Guard/Mechanic/Researcher)
		public float X { get; set; }
		public float Y { get; set; }
		public bool HasZone { get; set; }
		public float ZoneX { get; set; }
		public float ZoneY { get; set; }
		public float ZoneRadius { get; set; }
	}

	/// <summary>The active/offered challenge's progression (T-059, T-054). The challenge itself is identified by
	/// its <c>Index</c> into the level's <c>Challenges.sam</c> list; the manager re-derives the metric baseline
	/// on load, so only the progress + remaining days + win/loss tally need persisting.</summary>
	public sealed class ChallengeState
	{
		public string Phase { get; set; } = "Idle";   // ChallengeManager.Phase (Idle/Offered/Active)
		public int ActiveIndex { get; set; } = -1;     // Challenge.Index of the offered/active challenge (-1 = none)
		public int DaysLeft { get; set; }
		public float Progress { get; set; }
		public int Won { get; set; }
		public int Lost { get; set; }
	}

	/// <summary>A player-built coaster track (T-059): the laid tiles + heights + the closed-loop flag, tied to its
	/// coaster by that ride's grid tile (so it re-attaches after the placements rebuild). The station anchor tile
	/// is included at [0] but the constructor re-derives it; the rest is replayed via <c>CoasterTrack.Restore</c>.</summary>
	public sealed class TrackState
	{
		public int CoasterTileX { get; set; }
		public int CoasterTileY { get; set; }
		public bool Closed { get; set; }
		public List<TrackTile> Tiles { get; set; } = new();
	}

	/// <summary>One laid track tile: grid position + its height offset above the base track height.</summary>
	public sealed class TrackTile
	{
		public int X { get; set; }
		public int Y { get; set; }
		public float Rise { get; set; }
	}

	/// <summary>At-a-glance metadata for the save-slot menu (T-061): the park's money, in-game date, total
	/// visitors and ride/shop counts at save time — enough to tell slots apart without loading the park.</summary>
	public sealed class MetaInfo
	{
		public float Money { get; set; }
		public int Year { get; set; } = 1;
		public int Month { get; set; } = 1;
		public int Day { get; set; } = 1;
		public int Visitors { get; set; }
		public int Rides { get; set; }
		public int Shops { get; set; }
	}

	/// <summary>A one-line slot label for the save menu (T-061): money, in-game date and ride/shop/visitor
	/// counts from <see cref="Meta"/>; for a pre-Meta save it falls back to what the placements alone reveal.</summary>
	public string Summary()
	{
		if ( Meta is { } m )
			return $"${m.Money:0}  Y{m.Year} M{m.Month} D{m.Day}  {m.Rides}R {m.Shops}S  {m.Visitors}v";
		int rides = Placements.Count( p => p.Kind == "ride" );
		int shops = Placements.Count( p => p.Kind == "shop" );
		return $"${Money:0}  {rides}R {shops}S";
	}

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	/// <summary>How many save slots the UI offers (T-059).</summary>
	public const int SlotCount = 3;

	/// <summary>The file path for save slot <paramref name="slot"/> (1..<see cref="SlotCount"/>, clamped).</summary>
	public static string SlotPath( int slot ) =>
		Path.Combine( PlayerProfile.FolderPath ?? ".opentpw", $"save{Math.Clamp( slot, 1, SlotCount )}.json" ); // a selected player's own saves (T-063)

	/// <summary>True if slot <paramref name="slot"/> holds a save on disk (for the slot UI).</summary>
	public static bool SlotExists( int slot ) => File.Exists( SlotPath( slot ) );

	/// <summary>Default save-slot path under the OpenTPW cache directory (slot 1).</summary>
	public static string DefaultPath => SlotPath( 1 );

	public string ToJson() => JsonSerializer.Serialize( this, JsonOptions );

	public static SaveGame? FromJson( string json )
	{
		try { return JsonSerializer.Deserialize<SaveGame>( json ); }
		catch { return null; }
	}

	/// <summary>Write this save to <paramref name="path"/> (best-effort; logs + swallows IO errors).</summary>
	public void WriteToFile( string path )
	{
		try
		{
			var dir = Path.GetDirectoryName( path );
			if ( !string.IsNullOrEmpty( dir ) )
				Directory.CreateDirectory( dir );
			File.WriteAllText( path, ToJson() );
			Log.Info( $"[save] wrote {Placements.Count} placement(s) to {path}" );
		}
		catch ( Exception e ) { Log.Warning( $"[save] write failed: {e.Message}" ); }
	}

	/// <summary>Read a save from <paramref name="path"/>, or null if it's missing/unreadable.</summary>
	public static SaveGame? ReadFromFile( string path )
	{
		try { return File.Exists( path ) ? FromJson( File.ReadAllText( path ) ) : null; }
		catch ( Exception e ) { Log.Warning( $"[save] read failed: {e.Message}" ); return null; }
	}
}
