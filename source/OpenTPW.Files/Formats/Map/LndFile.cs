using System;
using System.Collections.Generic;
using System.IO;

namespace OpenTPW;

/// <summary>
/// Reader for a level's landscape ground layer (<c>terrain.wad/base.lnd</c>) — the texture-splatted grass/dirt
/// floor of the park. The terrain's <c>base.MD2</c> only holds the scenery (river, lake, entrance, hills, fence),
/// so without this file the park floor is empty. Layout decoded from samples; identical on all four themes and
/// consumed byte-exactly (see docs/tickets/T-071).
///
/// <code>
///   0x00 : 11 bytes   header: version 3, then 5 (centre, width) ramp pairs for the blend (see ParkGround.Compose)
///   image             blend map: 384×344, 4 bytes/pixel, ARGB
///   × 3 detail levels (LOD 0..2):
///     u32 tileSize    (64, 32, 16)
///     u8  index       (0, 1, 2)
///     image × 6       ground textures, ARGB (alpha unused, 0): dirt ×2, grass ×2, foliage ×2
///     image × 7       single-channel masks (1 byte/pixel): M0–M2 the dirt/grass/foliage variant masks,
///                     M3–M4 the grass cover masks, M5–M6 the foliage cover masks
///
///   image = u32 width, u32 height, u32 bytesPerPixel, width×height×bytesPerPixel pixel bytes (row-major)
/// </code>
/// The blend map covers the park interior at 4 pixels per tile (row 0 = the far, max-Y edge): alpha is 0 where
/// the scenery meshes take over (river, lake, entrance) and 255 where ground is drawn; R is the variation between
/// each family's two textures, G the grass cover and B the foliage cover over the dirt.
/// </summary>
public sealed class LndFile : BaseFormat
{
	public const int HeaderSize = 11;
	public const int LevelCount = 3;
	public const int TexturesPerLevel = 6;
	public const int MasksPerLevel = 7;

	/// <summary>The 11 header bytes: version 3, then the blend ramps' (centre, width) pairs.</summary>
	public byte[] Header { get; private set; } = Array.Empty<byte>();

	/// <summary>Per-pixel ground mask (A), variation (R), grass cover (G), foliage cover (B).</summary>
	public LndImage BlendMap { get; private set; } = null!;

	/// <summary>The detail levels, most detailed first.</summary>
	public IReadOnlyList<LndLevel> Levels => levels;
	private readonly List<LndLevel> levels = new();

	public LndFile( string path ) => ReadFromFile( path );
	public LndFile( Stream stream ) => ReadFromStream( stream );

	protected override void ReadFromStream( Stream stream )
	{
		using var ms = new MemoryStream();
		stream.CopyTo( ms );
		var d = ms.ToArray();
		int p = 0;

		if ( d.Length < HeaderSize )
			throw new InvalidDataException( "LND: file smaller than the header." );
		Header = d[..HeaderSize];
		p = HeaderSize;

		BlendMap = ReadImage( d, ref p );
		if ( BlendMap.BytesPerPixel != 4 )
			throw new InvalidDataException( $"LND: blend map has {BlendMap.BytesPerPixel} bytes/pixel, expected 4." );

		for ( int l = 0; l < LevelCount; l++ )
		{
			if ( p + 5 > d.Length )
				throw new InvalidDataException( $"LND: truncated before detail level {l}." );
			var level = new LndLevel( (int)BitConverter.ToUInt32( d, p ), d[p + 4] );
			p += 5;

			for ( int i = 0; i < TexturesPerLevel; i++ )
				level.Textures.Add( ReadImage( d, ref p ) );
			for ( int i = 0; i < MasksPerLevel; i++ )
				level.Masks.Add( ReadImage( d, ref p ) );
			levels.Add( level );
		}

		if ( p != d.Length )
			throw new InvalidDataException( $"LND: {d.Length - p} trailing byte(s) after the last detail level." );
	}

	private static LndImage ReadImage( byte[] d, ref int p )
	{
		if ( p + 12 > d.Length )
			throw new InvalidDataException( $"LND: truncated image header at 0x{p:X}." );
		int w = (int)BitConverter.ToUInt32( d, p );
		int h = (int)BitConverter.ToUInt32( d, p + 4 );
		int bpp = (int)BitConverter.ToUInt32( d, p + 8 );
		if ( w <= 0 || h <= 0 || w > 4096 || h > 4096 || bpp is not (1 or 4) )
			throw new InvalidDataException( $"LND: implausible image {w}×{h}×{bpp} at 0x{p:X}." );
		p += 12;

		int size = w * h * bpp;
		if ( p + size > d.Length )
			throw new InvalidDataException( $"LND: image {w}×{h}×{bpp} at 0x{p - 12:X} runs past the end of the file." );
		var pixels = d[p..(p + size)];
		p += size;
		return new LndImage( w, h, bpp, pixels );
	}
}

/// <summary>One detail level of a <see cref="LndFile"/>: 6 ground textures + 7 masks.</summary>
public sealed class LndLevel
{
	public LndLevel( int tileSize, int index )
	{
		TileSize = tileSize;
		Index = index;
	}

	/// <summary>64, 32 or 16 — halves with each level, like the texture sizes.</summary>
	public int TileSize { get; }
	public int Index { get; }

	/// <summary>Dirt ×2, grass ×2, foliage ×2 (ARGB, 4 bytes/pixel).</summary>
	public List<LndImage> Textures { get; } = new();

	/// <summary>Single-channel masks: variants M0–M2, grass cover M3–M4, foliage cover M5–M6.</summary>
	public List<LndImage> Masks { get; } = new();
}

/// <summary>A raw <see cref="LndFile"/> image: row-major pixels, ARGB when 4 bytes/pixel.</summary>
public sealed record LndImage( int Width, int Height, int BytesPerPixel, byte[] Pixels )
{
	/// <summary>Byte <paramref name="channel"/> (0 = A, 1 = R, 2 = G, 3 = B for ARGB) of pixel (x, y).</summary>
	public byte At( int x, int y, int channel = 0 ) => Pixels[(y * Width + x) * BytesPerPixel + channel];
}
