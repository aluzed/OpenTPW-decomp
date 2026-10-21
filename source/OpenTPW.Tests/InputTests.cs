using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
public class InputTests
{
	private static KeyEvent Ev( Key k, bool down ) => new( k, down, ModifierKeys.None );

	[TestMethod]
	public void AKeyTappedWithinOneFrameIsDownForThatFrameThenReleased()
	{
		Input.NextKeysDown( [], [] ); // reset the tap carry-over

		var frame1 = Input.NextKeysDown( [], [Ev( Key.F8, true ), Ev( Key.F8, false )] );
		CollectionAssert.Contains( frame1, Key.F8, "a same-frame tap must still be seen" );

		var frame2 = Input.NextKeysDown( frame1, [] );
		CollectionAssert.DoesNotContain( frame2, Key.F8, "…and released on the next frame" );
	}

	[TestMethod]
	public void HeldKeysStayDownUntilReleased()
	{
		Input.NextKeysDown( [], [] );

		var held = Input.NextKeysDown( [], [Ev( Key.W, true )] );
		held = Input.NextKeysDown( held, [] );
		CollectionAssert.Contains( held, Key.W );

		held = Input.NextKeysDown( held, [Ev( Key.W, false )] );
		CollectionAssert.DoesNotContain( held, Key.W );
	}

	[TestMethod]
	public void ReleaseThenPressInOneFrameStaysDown()
	{
		Input.NextKeysDown( [], [] );
		var held = Input.NextKeysDown( new List<Key> { Key.A }, [Ev( Key.A, false ), Ev( Key.A, true )] );
		held = Input.NextKeysDown( held, [] );
		CollectionAssert.Contains( held, Key.A );
	}
}
