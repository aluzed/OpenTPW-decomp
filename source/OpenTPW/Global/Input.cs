using Veldrid;
using Veldrid.Sdl2;

namespace OpenTPW;

public static partial class Input
{
	public static MouseInfo Mouse { get; internal set; } = new();
	public static KeyboardInfo Keyboard { get; internal set; } = new();

	public static float Forward { get; set; }
	public static float Right { get; set; }

	/// <summary>True for the single frame the left mouse button transitions from up to down.</summary>
	public static bool MouseLeftPressed { get; private set; }
	private static bool _lastMouseLeft;

	internal static List<InputButton> LastKeysDown { get; set; } = new();
	internal static List<InputButton> KeysDown { get; set; } = new();

	private static CursorTypes _cursorType;
	public static CursorTypes CursorType
	{
		get => _cursorType;
		set => _cursorType = value;
	}

	public static bool IsSystemCursorVisible
	{
		get => Sdl2Native.SDL_ShowCursor( -1 ) == 1;
		set => Sdl2Native.SDL_ShowCursor( value ? 1 : 0 );
	}

	public static bool Pressed( InputButton button )
	{
		return KeysDown.Contains( button ) && !LastKeysDown.Contains( button );
	}

	public static bool Down( InputButton button )
	{
		return KeysDown.Contains( button );
	}

	public static bool Released( InputButton button )
	{
		return !KeysDown.Contains( button ) && LastKeysDown.Contains( button );
	}

	public struct KeyboardInfo
	{
		public List<Key> KeysDown { get; set; }

		public KeyboardInfo()
		{
			KeysDown = new();
		}

		public KeyboardInfo( List<Key> keysDown )
		{
			KeysDown = keysDown;
		}
	}

	private static Dictionary<InputButton, Key[]> Bindings = new();

	static Input()
	{
		static T? GetAttributeOfType<T>( Enum enumVal ) where T : System.Attribute
		{
			var type = enumVal.GetType();
			var memInfo = type.GetMember( enumVal.ToString() );
			var attributes = memInfo[0].GetCustomAttributes( typeof( T ), false );
			return (attributes.Length > 0) ? (T)attributes[0] : null;
		}

		//
		// Build bindings table based on [DefaultKey] attribute values
		//
		foreach ( var key in Enum.GetValues<InputButton>() )
		{
			var attrib = GetAttributeOfType<DefaultKeyAttribute>( key );

			if ( attrib == null )
				continue;

			Bindings.Add( key, attrib.Keys );
		}
	}

	// Keys tapped (down + up) inside the previous frame: shown down for that one frame, released now.
	private static HashSet<Key> tappedLastFrame = new();

	/// <summary>
	/// The held keys after this frame's events, in order. A key pressed and released within the same frame (a quick
	/// tap, or a slow frame) still counts as down for this frame and is released on the next one — it used to be
	/// added and dropped in the same update, so the press was never seen.
	/// </summary>
	internal static List<Key> NextKeysDown( IEnumerable<Key> held, IEnumerable<KeyEvent> events )
	{
		var down = new HashSet<Key>( held );
		down.ExceptWith( tappedLastFrame );

		var pressed = new HashSet<Key>();
		var tapped = new HashSet<Key>();
		foreach ( var e in events )
		{
			if ( e.Down )
			{
				down.Add( e.Key );
				pressed.Add( e.Key );
				tapped.Remove( e.Key );
			}
			else if ( pressed.Contains( e.Key ) )
			{
				tapped.Add( e.Key ); // released in the frame it was pressed: keep it down until next frame
			}
			else
			{
				down.Remove( e.Key );
			}
		}

		tappedLastFrame = tapped;
		return down.ToList();
	}

	public static void UpdateFrom( InputSnapshot inputSnapshot )
	{
		IsSystemCursorVisible = true;

		var mousePos = new Vector2( inputSnapshot.MousePosition.X, inputSnapshot.MousePosition.Y );
		var mouseInfo = new MouseInfo
		{
			Delta = Mouse.Position - mousePos,
			Position = mousePos,
			Left = inputSnapshot.IsMouseDown( MouseButton.Left ),
			Right = inputSnapshot.IsMouseDown( MouseButton.Right ),
			Wheel = inputSnapshot.WheelDelta
		};

		Mouse = mouseInfo;

		// Also count a press from the frame's events: a quick click (down + up inside one frame, e.g. a
		// touchpad tap) leaves the button state "up" at both samples and would otherwise be lost.
		MouseLeftPressed = (mouseInfo.Left && !_lastMouseLeft)
			|| inputSnapshot.MouseEvents.Any( e => e.MouseButton == MouseButton.Left && e.Down );
		_lastMouseLeft = mouseInfo.Left;

		Right = 0;
		Forward = 0;

		Keyboard = new KeyboardInfo( NextKeysDown( Keyboard.KeysDown, inputSnapshot.KeyEvents ) );

		bool IsKeyPressed( Key k ) => Keyboard.KeysDown.Contains( k );

		if ( IsKeyPressed( Key.A ) )
			Right -= 1;
		if ( IsKeyPressed( Key.D ) )
			Right += 1;
		if ( IsKeyPressed( Key.W ) )
			Forward += 1;
		if ( IsKeyPressed( Key.S ) )
			Forward -= 1;

		LastKeysDown = [.. KeysDown];
		KeysDown.Clear();

		foreach ( var (button, vkeys) in Bindings )
		{
			bool isPressed = true;

			foreach ( var vkey in vkeys )
			{
				if ( !IsKeyPressed( vkey ) )
					isPressed = false;
			}

			if ( isPressed )
			{
				KeysDown.Add( button );
			}
		}
	}
}
