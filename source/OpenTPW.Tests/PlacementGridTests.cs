using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class PlacementGridTests
{
	private static PlacementGrid Grid() => new( 10, 8, 16f, new Vector3( 0, 0, 0 ) );

	[TestMethod]
	public void TileToWorldIsTheTileCentre()
	{
		var g = Grid();
		// Single tile (0,0): centre is half a tile in.
		var c0 = g.TileToWorld( 0, 0 );
		Assert.AreEqual( 8f, c0.X, 1e-4f );
		Assert.AreEqual( 8f, c0.Y, 1e-4f );
		Assert.AreEqual( 0f, c0.Z, 1e-4f );

		// Tile (2,3): centre at (2.5, 3.5) tiles.
		var c = g.TileToWorld( 2, 3 );
		Assert.AreEqual( 40f, c.X, 1e-4f );
		Assert.AreEqual( 56f, c.Y, 1e-4f );

		// A 4×4 footprint at (0,0) centres at (2,2) tiles = (32,32).
		var f = g.TileToWorld( 0, 0, 4, 4 );
		Assert.AreEqual( 32f, f.X, 1e-4f );
		Assert.AreEqual( 32f, f.Y, 1e-4f );
	}

	[TestMethod]
	public void WorldToTileInvertsTileToWorld()
	{
		var g = Grid();
		for ( int ty = 0; ty < g.Height; ty++ )
			for ( int tx = 0; tx < g.Width; tx++ )
			{
				var (rx, ry) = g.WorldToTile( g.TileToWorld( tx, ty ) );
				Assert.AreEqual( tx, rx );
				Assert.AreEqual( ty, ry );
			}
	}

	[TestMethod]
	public void CenteredMapsGridCentreToWorldCentre()
	{
		var g = PlacementGrid.Centered( 10, 8, 16f, new Vector3( 100, 100, 0 ) );
		// The grid's centre corner (tile 5,4) sits at the requested world centre.
		var corner = g.Origin + new Vector3( 5 * 16f, 4 * 16f, 0 );
		Assert.AreEqual( 100f, corner.X, 1e-3f );
		Assert.AreEqual( 100f, corner.Y, 1e-3f );
	}

	[TestMethod]
	public void InBoundsRespectsFootprintAndEdges()
	{
		var g = Grid(); // 10×8
		Assert.IsTrue( g.InBounds( 0, 0, 10, 8 ) );   // exactly fills
		Assert.IsFalse( g.InBounds( 8, 0, 4, 1 ) );   // 8+4 > 10
		Assert.IsFalse( g.InBounds( 0, 6, 1, 4 ) );   // 6+4 > 8
		Assert.IsFalse( g.InBounds( -1, 0, 1, 1 ) );
	}

	[TestMethod]
	public void PlacementTracksOccupancy()
	{
		var g = Grid();
		Assert.IsTrue( g.TryPlace( 2, 2, 3, 3 ) );
		Assert.IsFalse( g.CanPlace( 4, 4, 2, 2 ), "overlaps the placed footprint" );
		Assert.IsTrue( g.CanPlace( 5, 2, 3, 3 ), "adjacent, no overlap" );
		Assert.IsFalse( g.TryPlace( 0, 0, 4, 4 ), "overlaps at (2,2)" );

		g.Clear( 2, 2, 3, 3 );
		Assert.IsTrue( g.CanPlace( 4, 4, 2, 2 ), "freed after Clear" );
	}

	[TestMethod]
	public void WaterBlocksWalkingAndPlacement()
	{
		var g = Grid();
		g.MarkWater( 4, 4 );

		Assert.IsTrue( g.IsWater( 4, 4 ) );
		Assert.IsFalse( g.IsWalkable( 4, 4 ), "peeps can't stand on water" );
		Assert.IsFalse( g.CanPlace( 4, 4, 1, 1 ), "can't build on water" );
		Assert.IsFalse( g.CanPlace( 3, 3, 2, 2 ), "a footprint overlapping water is rejected" );
		Assert.AreEqual( 1, g.WaterTileCount );
		Assert.IsTrue( g.IsWalkable( 5, 5 ), "dry ground is fine" );
	}

	[TestMethod]
	public void MarkWaterFromTerrainFlagsTilesAtOrBelowLevel()
	{
		var g = Grid();
		// Sampler: tiles with world X below 40 are "low" (height 0), the rest are high (height 100).
		int marked = g.MarkWaterFromTerrain( ( x, _ ) => x < 40f ? 0f : 100f, waterLevel: 1f );

		Assert.IsTrue( marked > 0 );
		Assert.AreEqual( marked, g.WaterTileCount );
		// Tile (0,0) centre is at X=8 (<40) → water; tile (5,0) centre at X=88 (>40) → dry.
		Assert.IsTrue( g.IsWater( 0, 0 ) );
		Assert.IsFalse( g.IsWater( 5, 0 ) );
	}

	// T-072 — scenery from the .lnd ground holes.
	private static PlacementGrid SceneryGrid( string[] rows, (int, int) entrance, out (int Water, int Entrance) counts )
	{
		// rows[0] is the top (max Y) row, like the ASCII art reads; 'S' = scenery.
		var g = new PlacementGrid( rows[0].Length, rows.Length, PlacementGrid.WorldTileSize, new Vector3( 0, 0, 0 ) );
		counts = g.MarkScenery( ( x, y ) => rows[rows.Length - 1 - y][x] == 'S', entrance );
		return g;
	}

	[TestMethod]
	public void SceneryConnectedToTheEntranceIsAWalkableForecourtTheRestIsWater()
	{
		var g = SceneryGrid( new[]
		{
			"..SS..",
			"..SS..",
			"......",
			"SSSS..",
		}, (1, 0), out var counts );

		Assert.AreEqual( (4, 4), counts );
		Assert.IsTrue( g.IsWater( 2, 3 ) && g.IsWater( 3, 2 ), "the pond is water" );
		Assert.IsFalse( g.IsWater( 0, 0 ) );
		Assert.IsTrue( g.IsWalkable( 0, 0 ), "the forecourt is walkable…" );
		Assert.IsFalse( g.CanPlace( 0, 0, 1, 1 ), "…but not buildable" );
		Assert.IsTrue( g.CanPlace( 4, 0, 1, 1 ), "plain ground stays free" );
	}

	[TestMethod]
	public void ForecourtIsFoundNearAGateTileThatIsGround()
	{
		// The gate notch itself has no mesh over it (so it's ground), the forecourt starts two tiles away.
		var g = SceneryGrid( new[] { "......", "......", "SSS..." }, (1, 2), out var counts );
		Assert.AreEqual( (0, 3), counts );
		Assert.IsTrue( g.IsWalkable( 0, 0 ) && !g.IsWater( 2, 0 ) );
	}

	[TestMethod]
	public void FindOpenAnchorPicksTheMiddleOfTheBiggestLawn()
	{
		var g = SceneryGrid( new[]
		{
			"S.........",
			"S.........",
			"S.........",
			"S.........",
			"S.........",
			"S.........",
			"SSSSSSSSSS",
		}, (-10, -10), out _ );
		var (x, y) = g.FindOpenAnchor();
		// Free area x 1..9, y 1..6 (the edge row/column count as blocked): the centre is ~ (5, 3).
		Assert.IsTrue( x >= 4 && x <= 6 && y >= 2 && y <= 4, $"anchor ({x},{y})" );
		Assert.IsTrue( g.CanPlace( x, y, 1, 1 ) );
	}

	[TestMethod]
	public void FromLevelSettingsUsesTenUnitTilesFromTheHeightfieldStart()
	{
		var sam = new SettingsFile( new System.IO.MemoryStream( System.Text.Encoding.ASCII.GetBytes(
			"MapInfo.HeightfieldXStart 2\nMapInfo.HeightfieldYStart 1\nMapInfo.HeightfieldWidth 95\nMapInfo.HeightfieldHeight 84\n" ) ) );
		var g = PlacementGrid.FromLevelSettings( sam );
		Assert.AreEqual( 95, g.Width );
		Assert.AreEqual( 84, g.Height );
		Assert.AreEqual( 10f, g.TileSize );
		Assert.AreEqual( 20f, g.Origin.X, 1e-4f );
		Assert.AreEqual( 10f, g.Origin.Y, 1e-4f );
		// Jungle's entrance tile (48,17) lands on the gate: x 480, y 170 from a (0,0) start.
		var gate = new PlacementGrid( 95, 84, PlacementGrid.WorldTileSize, new Vector3( 0, 0, 0 ) ).PointToWorld( 48, 17 );
		Assert.AreEqual( 480f, gate.X, 1e-4f );
		Assert.AreEqual( 170f, gate.Y, 1e-4f );
	}
}
