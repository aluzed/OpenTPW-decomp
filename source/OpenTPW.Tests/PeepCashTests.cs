using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class PeepCashTests
{
	private static SettingsFile Sam( string text ) => new( new MemoryStream( Encoding.ASCII.GetBytes( text ) ) );

	[TestMethod]
	public void ColumnarKeysKeepTheirLaterColumns()
	{
		var sam = Sam( "PeepTypes[0].PreferredExcitement.StartingCash.BoredomThreshold\t80\t 300\t40\nPeepInfo.MinimumEntryFee\t20\t\tMinimum price below which\n" );
		Assert.AreEqual( "80", sam.Entries[0].Value );
		Assert.AreEqual( "300\t40", sam.Entries[0].Rest );
		Assert.AreEqual( "Minimum price below which", sam.Entries[1].Rest, "a trailing comment lands in Rest too" );
	}

	[TestMethod]
	public void StartingCashIsTheSecondColumnWithTheAuthoredVariance()
	{
		var sam = Sam( "PeepTypes[0].PreferredExcitement.StartingCash.BoredomThreshold\t80\t300\t40\n" +
			"PeepTypes[1].PreferredExcitement.StartingCash.BoredomThreshold\t65\t500\t30\n" );
		var saved = (PeepTypes.StartingCash, PeepTypes.StartingCashVarPc);
		try
		{
			PeepTypes.StartingCash = PeepTypes.LoadCash( sam );
			PeepTypes.StartingCashVarPc = 15f;
			CollectionAssert.AreEqual( new[] { 300f, 500f }, PeepTypes.StartingCash as System.Collections.ICollection ?? new System.Collections.Generic.List<float>( PeepTypes.StartingCash ) );
			Assert.AreEqual( 255f, PeepTypes.CashFor( 0, 0.0 ), 1e-3f, "−15%" );
			Assert.AreEqual( 500f, PeepTypes.CashFor( 1, 0.5 ), 1e-3f, "no variance at the middle" );
			Assert.AreEqual( 575f, PeepTypes.CashFor( 1, 1.0 ), 1e-3f, "+15%" );
		}
		finally { (PeepTypes.StartingCash, PeepTypes.StartingCashVarPc) = saved; }
	}
}
