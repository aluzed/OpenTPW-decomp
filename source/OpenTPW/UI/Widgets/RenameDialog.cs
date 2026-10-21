using Veldrid;

namespace OpenTPW;

/// <summary>
/// The original's rename box (UITEXT 194–200: "Rename Item", "Rename Staff Member", "Rename Park"), opened by clicking
/// a name in a window ("Name of ride - left-click to edit"): the <c>w_dialog</c> message with the name in a text box.
/// Typing replaces it, Backspace deletes, Enter or the tick keeps it, Esc leaves it as it was. While it is open the
/// park's keys stand down (<see cref="Input.TextCapture"/>).
/// </summary>
internal sealed class RenameDialog : GameWindow
{
	public const int MaxLength = 24;

	public static RenameDialog? Current { get; private set; }

	private string title = "", label = "", text = "";
	private Action<string>? apply;
	private readonly HashSet<Key> prevDown = new();

	public RenameDialog()
	{
		Current = this;
		if ( Environment.GetEnvironmentVariable( "OPENTPW_UI_OPEN" ) == "rename" ) // dev aid
			Open( Localization.Get( UIStrings.RenamePark, "Rename Park" ), Localization.Get( UIStrings.ParkName, "Park name" ), "My Park", _ => { } );
	}

	protected override void OnDelete()
	{
		if ( Current == this )
			Current = null;
		Input.TextCapture = false;
	}

	/// <summary>Ask for a new name: <paramref name="apply"/> gets it (trimmed, never empty) when the player keeps it.</summary>
	public static void Open( string title, string label, string initial, Action<string> apply )
	{
		if ( Current is not { } dialog )
			return;
		dialog.title = title;
		dialog.label = label;
		dialog.text = initial.Length > MaxLength ? initial[..MaxLength] : initial;
		dialog.apply = apply;
		dialog.prevDown.Clear();
		dialog.prevDown.UnionWith( Input.Keyboard.KeysDown ); // the click's keys don't count
		dialog.IsOpen = true;
	}

	public override void Close()
	{
		IsOpen = false;
		apply = null;
	}

	protected override Rectangle Frame => At( 270f, 170f, 420f, 247f );

	private Rectangle Box => At( 330f, 290f, 250f, 30f );

	protected override void OnUpdate()
	{
		Input.TextCapture = IsOpen;
		if ( !IsOpen )
			return;

		var down = Input.Keyboard.KeysDown;
		bool Hit( Key k ) => down.Contains( k ) && !prevDown.Contains( k );
		foreach ( char c in Input.TypedText )
			if ( text.Length < MaxLength )
				text += c;
		if ( Hit( Key.BackSpace ) && text.Length > 0 )
			text = text[..^1];
		if ( Hit( Key.Enter ) || Hit( Key.KeypadEnter ) )
			Keep();
		else if ( Hit( Key.Escape ) )
			Close();
		prevDown.Clear();
		prevDown.UnionWith( down );
	}

	private void Keep()
	{
		var name = text.Trim();
		if ( name.Length > 0 )
			apply?.Invoke( name );
		UiSounds.Play( UiSounds.Button );
		Close();
	}

	protected override void OnRender()
	{
		if ( !IsOpen )
			return;
		var ok = DrawDialog( Frame, title, new[] { label } );
		Fill( Box, SolidOf( 0, 0, 0 ) );
		Fill( Box with { Height = 2f }, SolidOf( 24, 54, 140 ) );
		bool caret = (int)(Time.Now * 2f) % 2 == 0;
		Text( Medium, text + (caret ? "_" : ""), 338f, 296f, 15f );
		if ( RoundButton( ok, "b_okay", hint: 1 ) )
			Keep();
	}
}
