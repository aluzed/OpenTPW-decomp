using Veldrid;

namespace OpenTPW;

/// <summary>
/// Save/load slot menu (T-061), in the original's window (<c>w_park</c>): the <see cref="SaveGame.SlotCount"/> save
/// slots in a list, each with a one-line summary of the saved park (money, in-game date, ride/shop/visitor counts via
/// <see cref="SaveGame.Summary"/>) and round save / load buttons. Toggled with F8, closed with its tick. Builds on
/// the keyboard slots from T-059 (F5/F9/F6) by letting the player see what's in a slot before overwriting/loading it.
/// </summary>
internal sealed class SavePanel : GameWindow
{
	public static SavePanel? Current { get; private set; }

	public SavePanel()
	{
		Current = this;
		if ( Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "save" ) // dev aid
		{
			IsOpen = true;
			Refresh();
		}
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
	}

	private bool wasToggleDown;

	// Cached per-slot saves (null = empty/unreadable), so the menu doesn't re-read the files every frame; it's
	// refreshed when the menu opens and after a save writes a slot.
	private readonly SaveGame?[] slots = new SaveGame?[SaveGame.SlotCount];

	protected override Rectangle Frame => At( 110f, 24f, 730f, 438f );

	private static readonly Rectangle List = At( 140f, 82f, 620f, 330f );
	private static readonly Rectangle Ok = At( 788f, 384f, 42f, 42f );
	private const float FirstRow = 94f;
	private static float RowStep => 320f / SaveGame.SlotCount;

	private void Refresh()
	{
		for ( int i = 0; i < slots.Length; i++ )
			slots[i] = SaveGame.ReadFromFile( SaveGame.SlotPath( i + 1 ) );
	}

	protected override void OnUpdate()
	{
		// F8 toggles the menu (edge-detected on the raw key, like the other overlays).
		var toggle = Input.Keyboard.KeysDown.Contains( Key.F8 );
		if ( toggle && !wasToggleDown )
		{
			IsOpen = !IsOpen;
			if ( IsOpen )
				Refresh();
		}
		wasToggleDown = toggle;
	}

	protected override void OnRender()
	{
		if ( !IsOpen )
			return;

		DrawFrame( "w_park" );
		DrawHeading( "Save / Load", 470f, 0f, 46f );
		DrawInset( List );

		for ( int i = 0; i < SaveGame.SlotCount; i++ )
		{
			int slot = i + 1; // slots are 1-based on disk
			float top = FirstRow + i * RowStep;
			bool filled = slots[i] != null;
			if ( i > 0 )
				Fill( At( 146f, top - 6f, 608f, 1f ), SolidOf( 24, 54, 140 ) );
			Text( Small, $"Slot {slot}", 152f, top, 13f, TitleInk );
			Text( Small, slots[i]?.Summary() ?? "- empty -", 152f, top + 22f, 11f, filled ? default : new( 0.6f, 0.6f, 0.6f ) );

			Text( Small, "Save", 626f, top + 10f, 11f, default, TextAlign.Right );
			if ( RoundButton( At( 630f, top, 34f, 34f ), "b_okay" ) )
			{
				Level.CaptureSave().WriteToFile( SaveGame.SlotPath( slot ) );
				Refresh();
			}
			Text( Small, "Load", 708f, top + 10f, 11f, default, TextAlign.Right );
			if ( RoundButton( At( 712f, top, 34f, 34f ), "b_entpark", enabled: filled )
				&& SaveGame.ReadFromFile( SaveGame.SlotPath( slot ) ) is { } g )
				Level.ApplySave( g );
		}

		if ( RoundButton( Ok, "b_okay", hint: 1 ) )
			IsOpen = false;
	}
}
