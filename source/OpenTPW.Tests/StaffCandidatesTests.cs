using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class StaffCandidatesTests
{
	[TestMethod]
	public void EachJobHasApplicantsAndHiringTakesOneOff()
	{
		Log = new();
		var list = StaffCandidates.For( StaffRole.Mechanic );
		Assert.AreEqual( StaffCandidates.PerRole, list.Count );
		Assert.IsTrue( list.All( c => c.Role == StaffRole.Mechanic && !string.IsNullOrEmpty( c.Name ) ) );
		Assert.IsTrue( list.All( c => c.Grade >= 0 && c.Grade <= StaffGrades.MaxGrade ) );

		var first = list[0];
		StaffCandidates.Hired( first );
		Assert.AreEqual( StaffCandidates.PerRole - 1, StaffCandidates.For( StaffRole.Mechanic ).Count );
		Assert.AreEqual( Staff.MonthlyWage( first.Role, first.Grade ), first.MonthlyWage );
	}
}
