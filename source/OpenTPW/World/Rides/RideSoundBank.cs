using OpenTPW.Files;

namespace OpenTPW;

/// <summary>
/// The ride sound registry: resolves a script sound id to its actual sample.
///
/// A ride's sound id (in <c>EventMap.rse</c>'s <c>VAR_EVT*</c>, and in <c>EVENT</c> operands) is a
/// <b>global index across the category's banks concatenated</b> in the order listed by the BANK catalog
/// (<c>cat_ridesBANK.map</c> → <c>Sound\Ride, sfUi, Staff, Kids, xKids</c> = <c>RideHD/sfUiHD/StaffHD/
/// KidsHD/xKidsHD.sdt</c>). RE'd and verified: e.g. id 14 → <c>RideHD:nl_creak_2</c>, 69 →
/// <c>RideHD:mortar_3</c>, 104 → <c>StaffHD:hamer002</c>, 200 → <c>KidsHD:fscE001</c> — all plausible
/// ride/peep/staff sounds (the catalog soundIds map cleanly through this concatenation). See T-037/T-016.
///
/// Banks are loaded lazily — only up to the bank containing a resolved id — so resolving a low
/// <c>RideHD</c> id never pays to load the 800-entry <c>KidsHD</c> bank.
/// </summary>
public sealed class RideSoundBank
{
	private readonly string[] bankFiles;        // .sdt stems, in catalog order (e.g. "RideHD")
	private readonly string directory;          // where the banks live: beside their catalog
	private readonly bool virtualFiles;         // read through the game VFS (a bank inside a ride's .wad)
	private readonly List<MP2File> tracks = new(); // concatenated samples loaded so far
	private int loadedBanks;

	private RideSoundBank( string[] bankFiles, string directory, bool virtualFiles = false )
	{
		this.bankFiles = bankFiles;
		this.directory = directory;
		this.virtualFiles = virtualFiles;
	}

	/// <summary>A ride's own sound bank, packed in its <c>.wad</c> (the jungle loudspeaker's <c>cat_rideBANK.map</c> +
	/// <c>BankHD.sdt</c>) — what EVENT / ADDOBJ type 10 plays. Null when the ride has none.</summary>
	public static RideSoundBank? FromArchive( string archive )
	{
		try
		{
			using var s = FileSystem.OpenRead( $"{archive}/cat_rideBANK.map" );
			if ( s == null )
				return null;
			var banks = new OpenTPW.MapFile( s ).Entries.Select( e => e.Split( '\\', '/' )[^1] + "HD" ).ToArray();
			return banks.Length > 0 ? new RideSoundBank( banks, archive, virtualFiles: true ) : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Builds the registry from a BANK catalog (e.g. <c>data/global/sound/cat_ridesBANK.map</c>): its
	/// entry names (<c>Sound\Ride</c>, …) become the bank <c>.sdt</c> files (<c>RideHD.sdt</c>, …) in
	/// order. Returns null if the catalog can't be read.
	/// </summary>
	public static RideSoundBank? FromBankCatalog( string catalogPath )
	{
		try
		{
			if ( !File.Exists( catalogPath ) )
				return null;
			using var s = File.OpenRead( catalogPath );
			var map = new OpenTPW.MapFile( s );
			// "Sound\Ride" → "RideHD"; the .sdt files append "HD" to the bank's leaf name.
			var banks = map.Entries
				.Select( e => e.Split( '\\', '/' )[^1] + "HD" )
				.ToArray();
			return banks.Length > 0 ? new RideSoundBank( banks, Path.GetDirectoryName( catalogPath ) ?? "" ) : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>The total samples loaded so far (grows as higher ids are resolved).</summary>
	public int LoadedCount => tracks.Count;

	/// <summary>Resolve a global sound id to its sample, loading banks lazily until it's covered.</summary>
	public MP2File? Resolve( int soundId )
	{
		if ( soundId < 0 )
			return null;

		while ( soundId >= tracks.Count && loadedBanks < bankFiles.Length )
		{
			var path = Path.Join( directory, bankFiles[loadedBanks] + ".sdt" );
			loadedBanks++;
			try
			{
				if ( virtualFiles )
				{
					using var s = FileSystem.OpenRead( path );
					if ( s != null )
						tracks.AddRange( new SdtArchive( s ).soundFiles );
				}
				else if ( File.Exists( path ) )
					tracks.AddRange( new SdtArchive( path ).soundFiles );
			}
			catch { /* skip a bad bank; higher ids just won't resolve */ }
		}

		return soundId < tracks.Count ? tracks[soundId] : null;
	}
}
