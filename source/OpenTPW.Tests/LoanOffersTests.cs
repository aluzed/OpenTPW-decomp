using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class LoanOffersTests
{
	private static SettingsFile Sam( string text ) => new( new MemoryStream( Encoding.ASCII.GetBytes( text ) ) );

	[TestMethod]
	public void OffersComeFromTheSamWithLevelOverridesAndLenderNames()
	{
		var global = Sam(
			"LoanInfo[0].LoanAmount\t100000\nLoanInfo[0].APRInPercent\t20\nLoanInfo[0].RepaymentPeriodInMonths\t36\nLoanInfo[0].Lendername\t0\n" +
			"LoanInfo[1].LoanAmount\t50000\nLoanInfo[1].APRInPercent\t20\nLoanInfo[1].RepaymentPeriodInMonths\t36\nLoanInfo[1].Lendername\t1\n" );
		var jungle = Sam( "LoanInfo[1].Lendername\t3\n" );
		var names = new[] { "Cash Unlimited", "Shark Finance", "Colossal Bank", "Loans Limited" };

		var offers = ParkFinances.LoadLoanOffers( global, jungle, names );

		Assert.AreEqual( 2, offers.Count );
		Assert.AreEqual( "Cash Unlimited", offers[0].Name );
		Assert.AreEqual( "Loans Limited", offers[1].Name, "the level overrides the lender" );
		Assert.AreEqual( 100000f, offers[0].Principal );
		Assert.AreEqual( 36, offers[0].Months );
		// 20% a year over 3 years: 100000 × 1.6.
		Assert.AreEqual( 160000f, offers[0].TotalRepayable, 1e-1f );
	}

	[TestMethod]
	public void TakingALoanSpreadsItOverItsPeriod()
	{
		var fin = new ParkFinances( 0f, 20f );
		fin.SetLoanOffers( new[] { new ParkFinances.Loan { Name = "X", Principal = 12000f, Apr = 10f, Months = 24 } } );
		fin.TakeLoan( 0 );
		Assert.AreEqual( 12000f, fin.Money, 1e-2f );
		Assert.AreEqual( 14400f, fin.Loans[0].Outstanding, 1e-1f ); // 12000 × (1 + 0.10 × 2)
		Assert.AreEqual( 600f, fin.Loans[0].Monthly, 1e-2f );
	}
}
