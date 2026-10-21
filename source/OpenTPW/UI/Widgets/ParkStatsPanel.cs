using OpenTPW.UI;

namespace OpenTPW;

/// <summary>
/// The in-park HUD (T-065): the player-facing essentials (money, visitors, admission, challenge/goals, loan,
/// park rating + contextual build/manage info) always shown, plus a <b>debug overlay</b> (income breakdown,
/// counts, crowd-mood thoughts, save-slot hints, FPS) that is OFF by default and toggled with the backtick key
/// (<see cref="GameSettings.ShowDebug"/>). Renders nothing until a park economy exists, so it's harmless in the
/// lobby.
/// </summary>
internal sealed class ParkStatsPanel : HudPanel
{
	private static Texture? bg, track, ratGood, ratMid, ratBad;
	private static Texture Bg => bg ??= Solid( 16, 16, 26, 170 );
	private static Texture Track => track ??= Solid( 50, 50, 62, 220 );
	private static Texture RatGood => ratGood ??= Solid( 80, 185, 90, 240 );
	private static Texture RatMid => ratMid ??= Solid( 210, 180, 60, 240 );
	private static Texture RatBad => ratBad ??= Solid( 200, 70, 60, 240 );

	private const float Scale = 1.5f;
	private const float Left = 30f;   // top-left margin (UI space is Y-up, origin bottom-left)
	private const float Top = 655f; // sits below the FPS counter in the top-left corner
	private const float LineStep = 28f;

	private bool debugKeyWas;

	protected override void OnUpdate()
	{
		// Backtick toggles the debug overlay (income breakdown / counts / thoughts / FPS) on/off — off by
		// default so the HUD reads as a real interface (T-065). Persisted; also drives the renderer FPS overlay.
		bool down = Input.Keyboard.KeysDown.Contains( Veldrid.Key.Grave );
		if ( down && !debugKeyWas )
		{
			var s = GameSettings.Current;
			s.ShowDebug = !s.ShowDebug;
			Renderer.ShowFps = s.ShowDebug;
			s.Save();
		}
		debugKeyWas = down;
	}

	protected override void OnRender()
	{
		if ( ParkFinances.Current is not { } fin )
			return;

		int visitors = Entity.All.Count( e => e is Peep );
		int staff = Entity.All.Count( e => e is Staff );
		bool dbg = GameSettings.Current.ShowDebug;

		// The player HUD (always shown): the essentials — money, visitors, admission control.
		var lines = new List<string>
		{
			// The labels are the game's own UITEXT.str entries, so they follow the language (GameLanguage).
			$"{Localization.Get( UIStrings.BankBalance, "Bank balance" )} {fin.Money:0}",
			Level.GatesOpen
				? $"{Localization.Get( UIStrings.PeopleInPark, "People in park" )} {visitors}"
				: $"{Localization.Get( UIStrings.PeopleInPark, "People in park" )} {visitors}  -  {Localization.Get( UIStrings.ParkClosed, "PARK CLOSED" )}",
			$"{Localization.Get( UIStrings.PriceOfEntry, "Price of entry" )} {fin.EntryFee:0}  ([ ])",
		};

		// The debug overlay (backtick toggle): the income breakdown, counts and crowd-mood diagnostics that
		// used to clutter the HUD by default (T-065).
		if ( dbg )
		{
			lines.Add( $"TICKETS {fin.RideRevenue:0}  GATE {fin.EntryRevenue:0}  FOOD {fin.FoodRevenue:0}" );
			lines.Add( $"SIDESHOW {fin.SideshowRevenue:0} (played {fin.SideshowsPlayed}, won {fin.SideshowsWon})" );
			lines.Add( $"UPKEEP {fin.UpkeepPaid:0}  WAGES {fin.WagesPaid:0}  STAFF {staff}  LITTER {Litter.Active.Count}" );
			if ( Peep.ThoughtTotal > 0 )
			{
				var parts = new List<string>();
				for ( int i = 0; i < Peep.ThoughtTally.Count; i++ )
					if ( Peep.ThoughtTally[i] > 0 )
						parts.Add( $"{RideThoughtText.Tag( (RideThought)i )} {Peep.ThoughtTally[i]}" );
				lines.Add( "THOUGHTS " + string.Join( " · ", parts ) );
			}
		}
		if ( GameClock.Current is { } clock )
			lines.Insert( 0, $"YEAR {clock.Year}  MTH {clock.Month}  DAY {clock.Day}  {DayNightTint.Phase( DayNightCycle.Phase01 )}" ); // in-game date + visual day phase (T-053/T-056)

			// Weather + season (T-056): current sky state derived from the level's authored weather model.
			if ( WeatherSim.Current is { } weather )
			{
				string[] seasonName = { "SPRING", "SUMMER", "AUTUMN", "WINTER" };
				string sky = weather.State.Kind switch
				{
					WeatherKind.Rain => weather.State.Lightning ? "STORM" : "RAIN",
					WeatherKind.Snow => weather.State.Lightning ? "BLIZZARD" : "SNOW",
					_ => "CLEAR",
				};
				lines.Insert( 1, $"{seasonName[weather.Season % 4]}  {sky} (q{weather.Quality})" );
			}

		// Challenge status (T-054): offered (Y/N), active (progress + days), or the last result.
		if ( ChallengeManager.Current is { } cm )
		{
			if ( cm.State == ChallengeManager.Phase.Offered && cm.Active is { } off )
				lines.Add( $"CHALLENGE: {ChallengeMetrics.Describe( off )}  (Y accept / N decline)" );
			else if ( cm.State == ChallengeManager.Phase.Active && cm.Active is { } act )
				lines.Add( $"CHALLENGE: {ChallengeMetrics.Describe( act )}  [{cm.Progress:0}/{act.TargetVal}, {cm.DaysLeft}d left]" );
			else if ( cm.LastResult is { } r )
				lines.Add( r.Won ? $"CHALLENGE WON! +${r.Challenge.Prize:0}" : "CHALLENGE FAILED" );
		}

		// Golden-ticket goals (T-055): the level win condition — goals met, or the win banner.
		if ( GoldenTicketGoals.Current is { } gt && gt.Total > 0 )
			lines.Add( gt.Awarded ? "GOLDEN TICKET WON!" : $"TICKET GOALS {gt.Met}/{gt.Total}" );
		if ( fin.Debt > 0 )
			lines.Add( $"DEBT {fin.Debt:0}  (L loans, K repay)" );
		else
			lines.Add( "LOANS: L  (no debt)" );
		if ( fin.Bankrupt )
			lines.Add( "*** BANKRUPT ***" );

		// Build palette (T-041): number keys pick an item to place, left-click places it.
		if ( BuildMode.Current is { } build )
		{
			if ( build.LayingTrack )
				lines.Add( build.TrackClosed
					? $"TRACK: {build.TrackSegments} segs - LOOP CLOSED (B back, T done)"
					: $"TRACK: {build.TrackSegments} segs (click lay, B back, T done)" );
			else if ( build.SelectedRide is { Shape.HasTrack: true } )
				lines.Add( "T: lay coaster track" );

			if ( dbg )
				lines.Add( $"SAVE SLOT {build.SaveSlot} {(SaveGame.SlotExists( build.SaveSlot ) ? "[used]" : "[empty]")}  (F5 save, F9 load, F6 slot, F8 menu)" );

			if ( build.SelectedStaff is { } staffSel )
				lines.Add( staffSel.HasPatrolZone
					? $"STAFF {StaffNames.For( staffSel.Role )}  zone r{staffSel.Zone!.Value.Radius:0} (FIRE/ZONE buttons)"
					: $"STAFF {StaffNames.For( staffSel.Role )}  free roam (FIRE/SET ZONE buttons)" );

			// Selected shop: its sale price (a toilet is free), adjustable with , . like a ride.
			if ( build.SelectedShop is { } shopSel )
				lines.Add( shopSel.Kind == ShopKind.Toilet
					? $"SHOP {shopSel.Name} (free)"
					: $"SHOP {shopSel.Name}  price {shopSel.Price:0} (,.)" );

			if ( build.SelectedRide is { } sel )
			{
				lines.Add( $"RIDE {sel.Name} L{sel.UpgradeLevel} cap {sel.Capacity}  price {sel.TicketPrice:0} (,.)" );
				if ( sel.IsResearching )
					lines.Add( $"  researching... {sel.ResearchFraction * 100f:0}%" );
				else if ( sel.NextResearched )
					lines.Add( $"  U: upgrade ${sel.NextUpgradeCost:0} -> cap {sel.Upgrades[sel.UpgradeLevel + 1].Capacity}" );
				else if ( sel.HasNextLevel )
					lines.Add( $"  R: research next ${sel.NextResearchCost:0}" );
				else
					lines.Add( "  (max upgrade level)" );
			}

			// The catalog itself is the clickable BuildPanel (right side); here just a hint + the
			// selected item, so the two panels don't duplicate the whole list.
			if ( build.Selected >= 0 && build.Selected < build.Catalog.Count )
			{
				var placing = build.Catalog[build.Selected];
				lines.Add( $"PLACING: {placing.Name} (click a tile, Esc cancel)" );
				if ( placing.RidePath != null )
					lines.Add( $"  ROTATE (R): {build.Rotation * 90} deg" );
			}
			else
				lines.Add( "BUILD: pick an item on the right -->" );
		}

		// What the crowd thinks, in the game's own words (THOUGHTS.str) — the two most common thoughts.
		var crowd = CrowdThoughts.Summary( Peep.CrowdThoughtTally() );
		if ( crowd.Length > 0 )
			lines.Add( $"{Localization.Get( UIStrings.Top3Thoughts, "Thoughts" )}: {crowd}" );

		// Park rating = average visitor happiness (T-039); shown as a coloured gauge under the stats.
		float rating = Peep.AverageHappiness;
		if ( rating >= 0f )
			lines.Add( $"{Localization.Get( UIStrings.ParkRating, "Park rating" )} {rating:0}%" );

		// Translucent backing so the text reads over the bright park; covers the lines + the rating bar.
		int n = lines.Count;
		float blockTop = Top + 8f;
		float lastLineTop = Top - (n - 1) * LineStep;
		float barH = 12f;
		float barTop = lastLineTop - 20f; // just below the PARK RATING text line
		float backBottom = barTop - barH - 8f;
		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( new Rectangle( Left - 12f, backBottom, 392f, blockTop - backBottom ), mat );

		for ( int i = 0; i < lines.Count; i++ )
			Graphics.DrawText( Font, lines[i], Left, Top - i * LineStep, TextAlign.Left, Scale );

		if ( rating >= 0f )
		{
			var fill = rating >= 66f ? RatGood : rating >= 33f ? RatMid : RatBad;
			DrawBar( new Rectangle( Left, barTop - barH, 364f, barH ), rating / 100f, fill, Track );
		}
	}
}
