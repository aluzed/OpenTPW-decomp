using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class StaffWageTests
{
	[TestMethod]
	public void WagesFollowTheAuthoredPayRatios()
	{
		float handyman = Staff.WagePerSecond( StaffRole.Handyman );
		Assert.AreEqual( 3f, Staff.WagePerSecond( StaffRole.Mechanic ) / handyman, 1e-4f, "mechanic 30 vs handyman 10" );
		Assert.AreEqual( 3.5f, Staff.WagePerSecond( StaffRole.Researcher ) / handyman, 1e-4f, "researcher 35" );
		// One of each role costs what five members of staff did before (1.5/s each).
		float all = handyman + Staff.WagePerSecond( StaffRole.Mechanic ) + Staff.WagePerSecond( StaffRole.Entertainer )
			+ Staff.WagePerSecond( StaffRole.Guard ) + Staff.WagePerSecond( StaffRole.Researcher );
		Assert.AreEqual( 7.5f, all, 1e-3f );
	}
}
