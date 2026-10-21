using OpenTPW.UI;

namespace OpenTPW;

internal sealed class LobbyLayout : Layout
{
	public override void OnInit()
	{
		Hud.AddChild( new Image( "/ui/textures/tpw_logo.wct" )
		{
			Uv0 = new Vector2( 0, 0.6f ),
			Uv1 = new Vector2( 1, 1 ),
			Position = new Vector2( 25 ),
			Size = new Vector2( 180f, 77.4f )
		} );

		var topRight = new Vector2( 1280, 720 );

		string[] saves = [];
		if ( SaveFileSystem.DirectoryExists( "users" ) )
			saves = SaveFileSystem.GetDirectories( "users" );

		var playerButtons = new PurpleButton[4];
		for ( int i = 0; i < 4; ++i )
		{
			var offset = new Vector2( 0, i * 100 );
			string? folder = saves.Length > i ? Path.GetFileName( saves[i] ) : null;
			var buttonText = folder is { Length: > 1 } ? folder[1..] : "#CreateNewPlayer";

			// A player's button selects that profile (its saves); an empty one creates "Player n" (T-063). The
			// selected one is marked with a star; clicking an island then enters a world as that player.
			int slot = i;
			var button = new PurpleButton( buttonText ) { position = topRight - offset };
			button.OnClick = () =>
			{
				var name = folder != null ? folder[1..] : PlayerProfile.Create( slot );
				if ( folder != null )
					PlayerProfile.Select( folder );
				folder ??= $"{slot}{name}";
				for ( int k = 0; k < playerButtons.Length; k++ )
					playerButtons[k].Text = playerButtons[k].Text.TrimStart( '*', ' ' );
				button.Text = $"* {name}";
			};
			playerButtons[i] = button;
			Hud.AddChild( button );
		}

		Hud.AddChild( new PurpleButton( "#QuitGame" )
		{
			position = topRight + new Vector2( 200, -425 ),
			OnClick = () => Render.RequestQuit()
		} );
	}
}
