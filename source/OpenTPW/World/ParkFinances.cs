namespace OpenTPW;

/// <summary>
/// The park's bank balance and the money flows that move it: the gate entry fee, shop sales and sideshow games
/// bring money in (rides are free in TPW — <see cref="TakeRideTicket"/> just counts a ride at price 0), ride
/// upkeep and staff wages drain it. <see cref="Ride.UpkeepPerSecond"/> is a sensible derived default for now. Cumulative flow totals are
/// kept for diagnostics and a future finances HUD.
/// </summary>
public sealed class ParkFinances
{
	/// <summary>The active park's finances (set by the level on load).</summary>
	public static ParkFinances? Current { get; set; }

	public float Money { get; private set; }

	/// <summary>The park's gate admission fee (player-settable, T-042).</summary>
	public float EntryFee { get; set; }

	public float RideRevenue { get; private set; }
	public float EntryRevenue { get; private set; }
	public float FoodRevenue { get; private set; }
	public float UpkeepPaid { get; private set; }
	public float WagesPaid { get; private set; }
	public float BuildSpent { get; private set; }

	// ── Finance history (T-049): one sample per in-game "month", for the finance graph ─────────────
	/// <summary>One period's finances: the closing balance, the income / expense that flowed that month, and the
	/// cumulative visitor count at the close (drives the golden-ticket "recent visitors" window, T-055).</summary>
	public readonly record struct FinanceSample( float Balance, float Income, float Expense, int Visitors = 0, float Wages = 0f );

	/// <summary>How many monthly samples the rolling history keeps (oldest dropped past this).</summary>
	public const int MaxHistory = 48;

	private readonly List<FinanceSample> history = new();
	private float lastIncomeTotal;
	private float lastExpenseTotal;

	/// <summary>Rolling per-month finance history, oldest first (drives the finance graph).</summary>
	public IReadOnlyList<FinanceSample> History => history;

	/// <summary>Consecutive in-game months the park has closed in the red (Money &lt; 0); 0 when solvent.
	/// Drives the advisor's escalating "in the red" warnings (T-046).</summary>
	public int MonthsInRed { get; private set; }
	private float lastWagesTotal;

	/// <summary>Wages and income summed over the last <paramref name="months"/> closed months (advisor WagesHigh).</summary>
	public (float Wages, float Income) RecentWagesAndIncome( int months )
	{
		float w = 0f, inc = 0f;
		for ( int i = Math.Max( 0, history.Count - months ); i < history.Count; i++ )
		{
			w += history[i].Wages;
			inc += history[i].Income;
		}
		return (w, inc);
	}

	private float TotalIncome => RideRevenue + EntryRevenue + FoodRevenue + SideshowRevenue;
	private float TotalExpense => UpkeepPaid + WagesPaid + BuildSpent;

	// Capture this month's flows (cumulative totals minus the last snapshot) + the closing balance.
	private void RecordMonth()
	{
		float income = TotalIncome - lastIncomeTotal;
		float expense = TotalExpense - lastExpenseTotal;
		lastIncomeTotal = TotalIncome;
		lastExpenseTotal = TotalExpense;
		float wages = WagesPaid - lastWagesTotal;
		lastWagesTotal = WagesPaid;

		history.Add( new FinanceSample( Money, income, expense, VisitorsTotal, wages ) );
		if ( history.Count > MaxHistory )
			history.RemoveRange( 0, history.Count - MaxHistory );

		MonthsInRed = Money < 0f ? MonthsInRed + 1 : 0;
	}

	public ParkFinances( float starting, float entryFee )
	{
		Money = starting;
		EntryFee = entryFee;
	}

	/// <summary>Can the park currently afford a build/upgrade of <paramref name="cost"/>?</summary>
	public bool CanAfford( float cost ) => Money >= cost;

	/// <summary>Pay a one-off build/placement cost (caller checks <see cref="CanAfford"/> first).</summary>
	public void PayBuild( float amount )
	{
		Money -= amount;
		BuildSpent += amount;
	}

	/// <summary>Refund part of a build cost when a ride/shop is sold (credits the balance back).</summary>
	public void RefundBuild( float amount )
	{
		Money += amount;
		BuildSpent -= amount;
	}

	// ── Loans (T-042) ───────────────────────────────────────────────────────────────────────────
	/// <summary>A bank loan offer: principal in, repaid with simple APR interest over <see cref="Months"/> monthly
	/// instalments.</summary>
	public sealed class Loan
	{
		public string Name = "";
		public float Principal;
		public float Apr;            // annual percentage rate
		public int Months = 12;      // repayment period
		public float Outstanding;    // remaining to repay (principal + interest), 0 when not bought
		public float Monthly;        // per-month instalment
		public bool Bought;

		/// <summary>Principal + simple interest over the repayment period.</summary>
		public float TotalRepayable => Principal * (1f + Apr / 100f * Months / 12f);
	}

	private readonly List<Loan> loans = new()
	{
		new Loan { Name = "Small", Principal = 5000f, Apr = 10f },
		new Loan { Name = "Large", Principal = 15000f, Apr = 18f },
	};

	/// <summary>Replace the offers with the level's own (<see cref="LoadLoanOffers"/>); a no-op for an empty list.</summary>
	public void SetLoanOffers( IReadOnlyList<Loan> offers )
	{
		if ( offers.Count == 0 )
			return;
		loans.Clear();
		loans.AddRange( offers );
	}

	/// <summary>
	/// The level's loan offers: <c>LoanInfo[i].{LoanAmount, APRInPercent, RepaymentPeriodInMonths, Lendername}</c> from the
	/// global <c>levels/Standard.sam</c>, each key overridable by the level's own (jungle renames four lenders), with the
	/// lender's name from <c>LOANNAMES.str</c> (falls back to "Loan n"). Stops at the first offer without an amount. Pure.
	/// </summary>
	public static List<Loan> LoadLoanOffers( SettingsFile global, SettingsFile? level, IReadOnlyList<string> lenderNames )
	{
		var offers = new List<Loan>();
		for ( int i = 0; ; i++ )
		{
			string? Get( string field )
			{
				var key = $"LoanInfo[{i}].{field}";
				var v = level?[key];
				return string.IsNullOrEmpty( v ) ? global[key] : v;
			}
			if ( !float.TryParse( Get( "LoanAmount" ), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var amount ) )
				break;
			float.TryParse( Get( "APRInPercent" ), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var apr );
			int months = int.TryParse( Get( "RepaymentPeriodInMonths" ), out var m ) && m > 0 ? m : 12;
			int lender = int.TryParse( Get( "Lendername" ), out var n ) ? n : -1;
			offers.Add( new Loan
			{
				Name = lender >= 0 && lender < lenderNames.Count && !string.IsNullOrWhiteSpace( lenderNames[lender] ) ? lenderNames[lender].Trim() : $"Loan {i + 1}",
				Principal = amount,
				Apr = apr,
				Months = months,
			} );
		}
		return offers;
	}

	public IReadOnlyList<Loan> Loans => loans;
	public float Debt => loans.Where( l => l.Bought ).Sum( l => l.Outstanding );

	/// <summary>True once the park has been in the red for six months running — the game's rule (TAG_SYSTEM.str 123:
	/// "If you stay in the red for six months you will be made bankrupt"; 127: "…in the red for six months, so the banks
	/// have closed your park"). It was a −5 000 balance, which could bankrupt a park in its first month.</summary>
	public bool Bankrupt => MonthsInRed >= BankruptMonths;

	public const int BankruptMonths = 6;

	/// <summary>Dev (<c>OPENTPW_BANKRUPT_DEMO</c>): put the park straight into bankruptcy to exercise the end screen.</summary>
	internal void ForceBankrupt()
	{
		Money = MathF.Min( Money, -1f );
		MonthsInRed = BankruptMonths;
	}

	/// <summary>Take a loan: principal is credited now, repaid (principal + simple APR interest) over its period.</summary>
	public void TakeLoan( int index )
	{
		if ( index < 0 || index >= loans.Count )
			return;
		var l = loans[index];
		if ( l.Bought )
			return;
		l.Bought = true;
		l.Outstanding = l.TotalRepayable;
		l.Monthly = l.Outstanding / l.Months;
		Money += l.Principal;
	}

	/// <summary>Pay off a loan's remaining balance in full (if affordable).</summary>
	public void RepayLoan( int index )
	{
		if ( index < 0 || index >= loans.Count )
			return;
		var l = loans[index];
		if ( !l.Bought || Money < l.Outstanding )
			return;
		Money -= l.Outstanding;
		l.Outstanding = 0f;
		l.Bought = false;
	}

	/// <summary>Settle one in-game month (T-053: called on <see cref="GameClock.OnNewMonth"/>): debit each
	/// active loan's monthly instalment and snapshot the closing balance + this month's flows for the
	/// finance graph (T-049).</summary>
	public void SettleMonth()
	{
		foreach ( var l in loans )
		{
			if ( !l.Bought )
				continue;
			float pay = MathF.Min( l.Monthly, l.Outstanding );
			Money -= pay;
			l.Outstanding -= pay;
			if ( l.Outstanding <= 0.01f )
			{
				l.Outstanding = 0f;
				l.Bought = false;
			}
		}
		RecordMonth();
	}

	// Cumulative counts (T-054 challenge metrics): how many rides ridden, drinks/food sold, and visitors
	// admitted over the level — the things challenges count toward.
	public int RidesRidden { get; private set; }
	public int FoodSold { get; private set; }
	public int DrinkSold { get; private set; }
	public int VisitorsTotal { get; private set; }

	/// <summary>What the shops' goods cost the park (the cost-of-goods part of each sale).</summary>
	public float GoodsSpent { get; private set; }

	/// <summary>Shop profit: takings minus the cost of the goods sold (challenge type 12).</summary>
	public float ShopProfit => FoodRevenue - GoodsSpent;

	/// <summary>Track rides (coasters) built and ride research completed — challenge types 18 and 16.</summary>
	public int CoastersBuilt { get; private set; }
	public int ResearchCompleted { get; private set; }

	/// <summary>A track ride (coaster) was placed.</summary>
	public void NoteCoasterBuilt() => CoastersBuilt++;

	/// <summary>A ride finished researching its next level.</summary>
	public void NoteResearchCompleted() => ResearchCompleted++;

	/// <summary>A peep pays to board a ride.</summary>
	public void TakeRideTicket( float price )
	{
		Money += price;
		RideRevenue += price;
		RidesRidden++;
	}

	/// <summary>How many sideshows peeps have played + won, and the park's cumulative sideshow takings (T-058).</summary>
	public int SideshowsPlayed { get; private set; }
	public int SideshowsWon { get; private set; }
	public float SideshowRevenue { get; private set; }

	/// <summary>A peep plays a sideshow: the park banks the net takings (the pay-to-play price, minus a prize
	/// payout when the peep wins — see <see cref="SideshowEconomy"/>). Net can be negative on a winning play.</summary>
	public void TakeSideshowTakings( float net, bool won )
	{
		Money += net;
		SideshowRevenue += net;
		SideshowsPlayed++;
		if ( won )
			SideshowsWon++;
	}

	/// <summary>A fresh visitor pays the gate entry fee.</summary>
	public void TakeEntryFee()
	{
		Money += EntryFee;
		EntryRevenue += EntryFee;
		VisitorsTotal++;
	}

	/// <summary>A visitor buys from a concession (<paramref name="drink"/> distinguishes a drink stall from a
	/// food stall, for the challenge counters).</summary>
	public void TakeFoodSale( float price, bool drink = false, float costOfGoods = 0f, SaleCategory? category = null )
	{
		Money += price - costOfGoods;
		FoodRevenue += price;
		GoodsSpent += costOfGoods;
		var c = category ?? (drink ? SaleCategory.Drink : SaleCategory.Snack);
		sold[(int)c]++;
		if ( c is SaleCategory.Gift or SaleCategory.Costume or SaleCategory.Balloon )
			return; // impulse items: neither food nor drink
		if ( drink || c == SaleCategory.Drink ) DrinkSold++; else FoodSold++;
	}

	private readonly int[] sold = new int[Enum.GetValues<SaleCategory>().Length];

	/// <summary>How many of a product the park's stalls have sold (fries, gifts, balloons… — the challenge counters).</summary>
	public int Sold( SaleCategory category ) => sold[(int)category];

	/// <summary>Award a cash prize (a completed challenge, T-054).</summary>
	public void AwardPrize( float amount ) => Money += amount;

	/// <summary>Restore the balance + admission fee from a loaded save (T-059).</summary>
	public void RestoreMoney( float money, float entryFee )
	{
		Money = money;
		EntryFee = entryFee;
	}

	/// <summary>Net profit (income − expense) over the last <paramref name="months"/> recorded months — the
	/// golden-ticket "profit per year" goal sums the last 12 (T-055).</summary>
	public float RecentProfit( int months )
	{
		float sum = 0f;
		for ( int i = Math.Max( 0, history.Count - months ); i < history.Count; i++ )
			sum += history[i].Income - history[i].Expense;
		return sum;
	}

	/// <summary>Visitors admitted over the last <paramref name="months"/> recorded months — the golden-ticket
	/// "recent visitors" goal (T-055): the latest closed month's cumulative count minus the count as of the
	/// close just before the window (0 before any history). Counts closed months only, like <see cref="RecentProfit"/>.</summary>
	public int RecentVisitors( int months )
	{
		if ( history.Count == 0 || months <= 0 )
			return 0;
		int latest = history[^1].Visitors;
		int start = history.Count - months - 1; // the close just before the window
		int baseline = start >= 0 ? history[start].Visitors : 0;
		return latest - baseline;
	}

	/// <summary>Ongoing ride running cost.</summary>
	public void PayUpkeep( float amount )
	{
		Money -= amount;
		UpkeepPaid += amount;
	}

	/// <summary>Ongoing staff wages.</summary>
	public void PayWages( float amount )
	{
		Money -= amount;
		WagesPaid += amount;
	}
}
