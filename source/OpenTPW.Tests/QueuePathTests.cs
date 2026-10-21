using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class QueuePathTests
{
	private static readonly (int X, int Y) Door = (5, 5);

	[TestMethod]
	public void StraightRunEndsInTheOpenEndFacingAway()
	{
		var path = new[] { (5, 6), (5, 7), (5, 8) };
		Assert.AreEqual( (QueuePieces.Straight, (0, 1)), QueuePath.PieceAt( path, Door, 0 ) );
		Assert.AreEqual( (QueuePieces.Straight, (0, 1)), QueuePath.PieceAt( path, Door, 1 ) );
		Assert.AreEqual( (QueuePieces.End, (0, -1)), QueuePath.PieceAt( path, Door, 2 ) );
	}

	[TestMethod]
	public void BendsFaceTheirEdgesToTheWayInAndOut()
	{
		// Heading +Y, then turning to -X is a left (counter-clockwise) turn; to +X a right one.
		var left = new[] { (5, 6), (4, 6) };
		var right = new[] { (5, 6), (6, 6) };
		Assert.AreEqual( (QueuePieces.Bend, (0, -1)), QueuePath.PieceAt( left, Door, 0 ) );  // +Y back the way in
		Assert.AreEqual( (QueuePieces.Bend, (1, 0)), QueuePath.PieceAt( right, Door, 0 ) );  // +Y the way out
		Assert.AreEqual( (QueuePieces.End, (1, 0)), QueuePath.PieceAt( left, Door, 1 ) );
	}
}
