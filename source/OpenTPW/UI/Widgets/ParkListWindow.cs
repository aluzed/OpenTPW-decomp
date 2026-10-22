namespace OpenTPW;

/// <summary>
/// The original's lists (UIHELPTEXT: "list all the items in the park (A)", "all the staff (S)", "all the visitors
/// (V)"): the <c>w_park</c> frame with three tabs and a list — rides and stalls with their state, staff with their
/// grade and patrol, visitors with their thought and happiness. Clicking an entry opens its window and brings the
/// camera to it.
/// </summary>
internal sealed class ParkListWindow : GameWindow
{
	public static ParkListWindow? Current { get; private set; }

	public enum Tab { Items, Staff, Visitors }

	private Tab tab;
	private int scroll;

	public ParkListWindow()
	{
		Current = this;
		if ( Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) is "items" or "stafflist" or "visitors" ) // dev aid
		{
			IsOpen = true;
			tab = Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) switch { "stafflist" => Tab.Staff, "visitors" => Tab.Visitors, _ => Tab.Items };
		}
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	/// <summary>Open on <paramref name="which"/>, or close if it's already showing.</summary>
	public void Toggle( Tab which )
	{
		if ( IsOpen && tab == which )
		{
			IsOpen = false;
			return;
		}
		tab = which;
		scroll = 0;
		filter = -1;
		IsOpen = true;
	}

	protected override Rectangle Frame => At( 110f, 24f, 730f, 438f );

	private static readonly Rectangle List = At( 140f, 128f, 620f, 290f );
	private static readonly Rectangle Ok = At( 788f, 384f, 42f, 42f );
	private static readonly Rectangle Training = At( 788f, 128f, 42f, 42f ); // staff tab: the training budgets
	private static readonly Rectangle Locate = At( 788f, 180f, 42f, 42f );   // staff / visitors: show where they are
	private const float RowTop = 172f, RowHeight = 16f;
	private const int VisibleRows = 15;
	private static readonly (Tab Tab, string Icon, string Title, int Hint)[] Tabs =
	{
		(Tab.Items, "b_allthings", "All Items", 75), (Tab.Staff, "b_allstaff", "All Staff", 74), (Tab.Visitors, "b_kids", "All Visitors", 100),
	};

	private static Rectangle TabRect( int i ) => At( 150f + i * 56f, 74f, 48f, 48f );
	private static Rectangle RowRect( int row ) => At( 146f, RowTop + row * RowHeight, 608f, RowHeight );

	// A row: what it shows, and the value column as a number to sort by.
	private readonly record struct Entry( Entity Entity, string Name, string State, string Value, double Amount );

	// Filters beside the tabs (UIHELPTEXT 101–104, 114–118): one kind of item or of staff; a second click shows all.
	private int filter = -1;
	private static readonly (string Icon, int Hint)[] ItemFilters =
		{ ("b_srides", 101), ("b_sshops", 102), ("b_sshows", 103), ("b_sfeature", 104) };
	private static readonly (string Icon, int Hint, StaffRole Role)[] StaffFilters =
	{
		("b_shandy", 114, StaffRole.Handyman), ("b_smech", 115, StaffRole.Mechanic), ("b_senter", 116, StaffRole.Entertainer),
		("b_sguard", 117, StaffRole.Guard), ("b_sresrcher", 118, StaffRole.Researcher),
	};
	private int FilterCount => tab switch { Tab.Items => ItemFilters.Length, Tab.Staff => StaffFilters.Length, _ => 0 };
	private static Rectangle FilterRect( int i ) => At( 340f + i * 46f, 80f, 38f, 38f );

	private bool Passes( Entity e ) => filter < 0 || e switch
	{
		Ride r => filter == (r.IsFeature ? 3 : r.IsSideshow ? 2 : 0),
		Shop => filter == 1,
		Staff s => StaffFilters[filter].Role == s.Role,
		_ => true,
	};

	// Sorting: a click on a column's heading sorts by it; a second click turns the order round.
	private int sortColumn = -1;
	private bool descending;
	private static Rectangle ColumnRect( int c ) => c switch
	{
		0 => At( 146f, 134f, 268f, 30f ), 1 => At( 414f, 134f, 186f, 30f ), _ => At( 600f, 134f, 154f, 30f ),
	};
	// The game's hints for the headings (UIHELPTEXT "Left-click to sort the list by …"), per tab and column.
	private int SortHint( int c ) => tab switch
	{
		Tab.Items => c == 0 ? 105 : c == 1 ? 108 : -1,
		Tab.Staff => c == 0 ? 119 : c == 1 ? 122 : 121,
		_ => c == 0 ? 129 : c == 1 ? 133 : 134,
	};

	private List<Entry> Entries()
	{
		var entries = tab switch
		{
			Tab.Items => Entity.All.OfType<Ride>().Select( r => new Entry( r, r.Name,
					r.IsBroken ? "Broken down" : r.IsClosed ? "Closed" : $"{r.Riders} riding", $"$ {r.BuildCost * Ride.SellRefundFraction:0}",
					r.BuildCost * Ride.SellRefundFraction ) )
				.Concat( Shop.Stalls.Select( s => new Entry( s, s.Name, s.IsClosed ? "Closed" : s.IsFree ? "Free" : $"$ {s.Price:0}",
					$"$ {s.BuildCost * Ride.SellRefundFraction:0}", s.BuildCost * Ride.SellRefundFraction ) ) ).ToList(),
			Tab.Staff => Entity.All.OfType<Staff>().Select( s => new Entry( s, $"{s.PersonalName} ({StaffNames.For( s.Role )})",
				$"grade {s.Grade + 1}, {(s.HasPatrolZone ? "zone" : "free roam")}", $"$ {Staff.MonthlyWage( s.Role, s.Grade ):0}",
				Staff.MonthlyWage( s.Role, s.Grade ) ) ).ToList(),
			_ => Entity.All.OfType<Peep>().Select( p => new Entry( p, p.PersonalName, CrowdThoughts.Text( p.Thought ), $"{p.Happiness:0} %",
				p.Happiness ) ).ToList(),
		};
		entries = entries.Where( e => Passes( e.Entity ) ).ToList();
		IEnumerable<Entry> sorted = sortColumn switch
		{
			0 => entries.OrderBy( e => e.Name, StringComparer.CurrentCultureIgnoreCase ),
			1 => entries.OrderBy( e => e.State, StringComparer.CurrentCultureIgnoreCase ),
			2 => entries.OrderBy( e => e.Amount ),
			_ => entries,
		};
		if ( sortColumn >= 0 && descending )
			sorted = sorted.Reverse();
		return sorted.ToList();
	}

	protected override void OnUpdate()
	{
		if ( !IsOpen || BuildMode.Current is not { } build )
			return;
		var m = MouseBase();
		var entries = Entries();
		if ( Contains( List, m ) && Input.Mouse.Wheel != 0 )
			scroll = Math.Clamp( scroll - Math.Sign( Input.Mouse.Wheel ), 0, Math.Max( 0, entries.Count - VisibleRows ) );
		if ( !Input.MouseLeftPressed )
			return;
		for ( int t = 0; t < Tabs.Length; t++ )
			if ( Contains( TabRect( t ), m ) )
			{
				tab = Tabs[t].Tab;
				scroll = 0;
				sortColumn = -1;
				filter = -1;
				return;
			}
		for ( int f = 0; f < FilterCount; f++ )
			if ( Contains( FilterRect( f ), m ) )
			{
				filter = filter == f ? -1 : f;
				scroll = 0;
				return;
			}
		for ( int c = 0; c < 3; c++ )
			if ( Contains( ColumnRect( c ), m ) )
			{
				descending = sortColumn == c ? !descending : c == 2; // amounts: the largest first
				sortColumn = c;
				scroll = 0;
				UiSounds.Play( UiSounds.Button );
				return;
			}
		for ( int row = 0; row < VisibleRows && row + scroll < entries.Count; row++ )
		{
			if ( !Contains( RowRect( row ), m ) )
				continue;
			var e = entries[row + scroll].Entity;
			switch ( e )
			{
				case Ride r: build.SelectRide( r ); break;
				case Shop s: build.SelectShop( s ); break;
				case Staff st: build.SelectStaff( st ); break;
				case Peep p: build.SelectPeep( p ); break;
			}
			BuildCameraMode.Focus = e.Position.WithZ( BuildCameraMode.Focus.Z );
			UiSounds.Play( UiSounds.Button );
			IsOpen = false;
			return;
		}
	}

	protected override void OnRender()
	{
		if ( !IsOpen )
			return;
		int ti = Array.FindIndex( Tabs, t => t.Tab == tab );
		DrawFrame( "w_park" );
		DrawHeading( Tabs[ti].Title, 470f, 0f, 46f );
		for ( int t = 0; t < Tabs.Length; t++ )
			RoundButton( TabRect( t ), Tabs[t].Icon, Tabs[t].Tab == tab, hint: Tabs[t].Hint );
		for ( int f = 0; f < FilterCount; f++ )
		{
			var (icon, hint) = tab == Tab.Items ? ItemFilters[f] : (StaffFilters[f].Icon, StaffFilters[f].Hint);
			RoundButton( FilterRect( f ), icon, filter == f, hint: hint );
		}

		DrawInset( List );
		Fill( At( 146f, 134f, 608f, 30f ), Header );
		var (n, st, v) = tab switch
		{
			Tab.Items => ("Name", "State", "Scrap value"),
			Tab.Staff => ("Name", "Grade", "Monthly wage"),
			_ => ("Name", "Thinking", "Happiness"),
		};
		string Arrow( int c ) => sortColumn == c ? (descending ? " v" : " ^") : "";
		Text( Small, n + Arrow( 0 ), 156f, 142f, 11f, HeaderInk );
		Text( Small, st + Arrow( 1 ), 420f, 142f, 11f, HeaderInk );
		Text( Small, v + Arrow( 2 ), 740f, 142f, 11f, HeaderInk, TextAlign.Right );
		for ( int c = 0; c < 3; c++ )
			if ( Contains( ColumnRect( c ), MouseBase() ) && SortHint( c ) >= 0 )
				Hint( SortHint( c ) );

		var entries = Entries();
		var m = MouseBase();
		for ( int row = 0; row < VisibleRows && row + scroll < entries.Count; row++ )
		{
			var e = entries[row + scroll];
			float top = RowTop + row * RowHeight;
			if ( Contains( RowRect( row ), m ) )
				Fill( RowRect( row ), Selected );
			Text( Small, e.Name, 156f, top + 2f, 11f );
			Text( Small, e.State, 420f, top + 2f, 11f );
			Text( Small, e.Value, 740f, top + 2f, 11f, default, TextAlign.Right );
		}
		Text( Small, $"{entries.Count} in the park", 740f, 422f, 11f, default, TextAlign.Right );
		if ( tab == Tab.Staff && RoundButton( Locate, "b_locate", LocateMarkers.Showing == LocateMarkers.Target.Staff, hint: 128 ) )
			LocateMarkers.Toggle( LocateMarkers.Target.Staff );
		if ( tab == Tab.Visitors && RoundButton( Locate, "b_peeploc", LocateMarkers.Showing == LocateMarkers.Target.Visitors, hint: 139 ) )
			LocateMarkers.Toggle( LocateMarkers.Target.Visitors );
		if ( tab == Tab.Staff && RoundButton( Training, "b_staffcost", hint: 195 ) )
		{
			IsOpen = false;
			TrainingWindow.Current?.Toggle();
		}

		if ( RoundButton( Ok, "b_okay", hint: 1 ) )
			IsOpen = false;
	}
}
