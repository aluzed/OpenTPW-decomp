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
		IsOpen = true;
	}

	protected override Rectangle Frame => At( 110f, 24f, 730f, 438f );

	private static readonly Rectangle List = At( 140f, 128f, 620f, 290f );
	private static readonly Rectangle Ok = At( 788f, 384f, 42f, 42f );
	private const float RowTop = 172f, RowHeight = 16f;
	private const int VisibleRows = 15;
	private static readonly (Tab Tab, string Icon, string Title, int Hint)[] Tabs =
	{
		(Tab.Items, "b_allthings", "All Items", 75), (Tab.Staff, "b_allstaff", "All Staff", 74), (Tab.Visitors, "b_kids", "All Visitors", 100),
	};

	private static Rectangle TabRect( int i ) => At( 150f + i * 56f, 74f, 48f, 48f );
	private static Rectangle RowRect( int row ) => At( 146f, RowTop + row * RowHeight, 608f, RowHeight );

	private readonly record struct Entry( Entity Entity, string Name, string State, string Value );

	private List<Entry> Entries() => tab switch
	{
		Tab.Items => Entity.All.OfType<Ride>().Select( r => new Entry( r, r.Name,
				r.IsBroken ? "Broken down" : r.IsClosed ? "Closed" : $"{r.Riders} riding", $"$ {r.BuildCost * Ride.SellRefundFraction:0}" ) )
			.Concat( Shop.Stalls.Select( s => new Entry( s, s.Name, s.IsClosed ? "Closed" : s.IsFree ? "Free" : $"$ {s.Price:0}",
				$"$ {s.BuildCost * Ride.SellRefundFraction:0}" ) ) ).ToList(),
		Tab.Staff => Entity.All.OfType<Staff>().Select( s => new Entry( s, $"{s.PersonalName} ({StaffNames.For( s.Role )})",
			$"grade {s.Grade + 1}, {(s.HasPatrolZone ? "zone" : "free roam")}", $"$ {Staff.MonthlyWage( s.Role, s.Grade ):0}" ) ).ToList(),
		_ => Entity.All.OfType<Peep>().Select( p => new Entry( p, p.PersonalName, CrowdThoughts.Text( p.Thought ), $"{p.Happiness:0} %" ) ).ToList(),
	};

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

		DrawInset( List );
		Fill( At( 146f, 134f, 608f, 30f ), Header );
		var (n, st, v) = tab switch
		{
			Tab.Items => ("Name", "State", "Scrap value"),
			Tab.Staff => ("Name", "Grade", "Monthly wage"),
			_ => ("Name", "Thinking", "Happiness"),
		};
		Text( Small, n, 156f, 142f, 11f, HeaderInk );
		Text( Small, st, 420f, 142f, 11f, HeaderInk );
		Text( Small, v, 740f, 142f, 11f, HeaderInk, TextAlign.Right );

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

		if ( RoundButton( Ok, "b_okay", hint: 1 ) )
			IsOpen = false;
	}
}
