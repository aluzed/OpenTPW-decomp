using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace OpenTPW.Tests;

[TestClass]
public class NearestVertexIndexTests
{
	private static int BruteForce( IReadOnlyList<(float X, float Y)> pts, float x, float y )
	{
		int best = -1;
		float bestD = float.MaxValue;
		for ( int i = 0; i < pts.Count; i++ )
		{
			float dx = pts[i].X - x, dy = pts[i].Y - y, d = dx * dx + dy * dy;
			if ( d < bestD ) { bestD = d; best = i; }
		}
		return best;
	}

	[TestMethod]
	public void MatchesALinearScanInsideAndOutsideTheGrid()
	{
		var rng = new Random( 1234 );
		var pts = new List<(float, float)>();
		for ( int i = 0; i < 3000; i++ )
			pts.Add( ((float)(rng.NextDouble() * 1000 - 100), (float)(rng.NextDouble() * 800 - 50)) );
		// a dense cluster + duplicates to exercise ties
		for ( int i = 0; i < 200; i++ )
			pts.Add( (500f + i % 5, 400f + i % 3) );

		var index = new NearestVertexIndex( pts, cellSize: 16f );
		for ( int q = 0; q < 2000; q++ )
		{
			float x = (float)(rng.NextDouble() * 1600 - 400), y = (float)(rng.NextDouble() * 1400 - 300);
			Assert.AreEqual( BruteForce( pts, x, y ), index.Nearest( x, y ), $"query ({x},{y})" );
		}
	}

	[TestMethod]
	public void EmptyAndSinglePointSets()
	{
		Assert.AreEqual( -1, new NearestVertexIndex( new List<(float, float)>() ).Nearest( 1, 2 ) );
		Assert.AreEqual( 0, new NearestVertexIndex( new List<(float, float)> { (5, 5) } ).Nearest( -1000, 1000 ) );
	}
}
