using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class MaterialSlotsTests
{
	private static Vertex V( int texIndex, float x = 0 ) => new() { Position = new Vector3( x, 0, 0 ), TexIndex = texIndex };

	[TestMethod]
	public void AMeshWithinSixteenMaterialsIsUnchanged()
	{
		var vertices = new[] { V( 0 ), V( 15 ), V( 3 ) };
		var indices = new uint[] { 0, 1, 2 };
		var groups = MaterialSlots.Split( vertices, indices );
		Assert.AreEqual( 1, groups.Count );
		Assert.AreSame( vertices, groups[0].Vertices );
		Assert.AreSame( indices, groups[0].Indices );
	}

	[TestMethod]
	public void MaterialsBeyondSixteenGoToRebasedGroups()
	{
		// Triangle A uses material 2, triangle B material 20 (→ group 1, slot 4); they share vertex 2.
		var vertices = new[] { V( 2, 0 ), V( 2, 1 ), V( 2, 2 ), V( 20, 3 ), V( 20, 4 ) };
		var indices = new uint[] { 0, 1, 2, 3, 4, 2 };
		var groups = MaterialSlots.Split( vertices, indices );

		Assert.AreEqual( 2, groups.Count );
		Assert.AreEqual( 0, groups[0].Group );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, groups[0].Indices );

		var g1 = groups[1];
		Assert.AreEqual( 1, g1.Group );
		Assert.AreEqual( 3, g1.Vertices.Length, "the shared vertex is duplicated into the group" );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, g1.Indices );
		CollectionAssert.AreEqual( new[] { 4, 4 }, g1.Vertices.Take( 2 ).Select( v => v.TexIndex ).ToArray() );
		Assert.AreEqual( 3f, g1.Vertices[0].Position.X );
		Assert.AreEqual( 2f, g1.Vertices[2].Position.X );
	}
}
