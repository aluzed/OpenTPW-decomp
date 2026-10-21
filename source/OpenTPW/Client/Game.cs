using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Handles the creation and management of various systems, including the game
/// window.
/// </summary>
internal static class Game
{
	public static void Run( string[] args )
	{
		Log = new();

		// Resolve the install path once (honors the OPENTPW_GAMEPATH override). See T-006.
		var gamePath = GameDir.GamePath;

		//
		// Check if the game data directory exists
		//
		if ( !Path.Exists( Path.Join( gamePath, "data" ) ) )
			throw new DirectoryNotFoundException(
				$"Theme Park World not found at '{gamePath}'. Set OPENTPW_GAMEPATH or the "
				+ "GamePath setting to a valid install." );

		// Register game data directory
		FileSystem = new BaseFileSystem( Path.Join( gamePath, "data" ) );

		// Language: English ships in data/ itself; another language is the disc's <Language>/data folder, extracted
		// next to it and read as an overlay (strings, fonts, advisor voice). Missing → stay in English.
		var language = Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE" ) is { Length: > 0 } envLanguage
			? envLanguage
			: GameSettings.Current.Language;
		if ( !language.Equals( "English", StringComparison.OrdinalIgnoreCase ) )
		{
			var overlay = GameDir.LanguageDataPath( language );
			if ( overlay != null )
			{
				FileSystem.Overlay = new BaseFileSystem( overlay );
				GameLanguage.Name = Path.GetFileName( Path.GetDirectoryName( overlay ) )!; // the folder's own spelling
				Log.Info( $"[lang] {GameLanguage.Name} ({overlay})" );
			}
			else
				Log.Warning( $"[lang] no '{language}/data' next to data/ — extract it from the disc (docs/03); using English" );
		}

		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		FileSystem.RegisterArchiveHandler<SdtArchive>( ".sdt" );

		//
		// Check if the save data directory exists (create if not)
		//
		var savePath = Path.Join( gamePath, "save" );
		if ( !Path.Exists( savePath ) )
			Directory.CreateDirectory( savePath );

		// Register save data directory
		SaveFileSystem = new BaseFileSystem( savePath );

		//
		// Custom OpenTPW cache directory (mainly for editor-related stuff)
		//
		CacheFileSystem = new BaseFileSystem( $"./.opentpw" );

		//
		// Init renderer
		//
		Render = new();

		//
		// Show a loading screen while the level loads synchronously (otherwise the window is
		// black and the WM marks it "not responding"). A title font draws "LOADING…" over a
		// sky-blue clear; if the font can't be loaded we still show the colour, never black.
		//
		Font? loadingFont = null;
		try { loadingFont = new Font( GameLanguage.File( "GAME12.bf4" ) ); }
		catch ( Exception e ) { Log.Warning( $"Loading-screen font unavailable: {e.Message}" ); }

		// 1x1 textures for the progress bar (the UI shader only samples a texture, no tint — T-030).
		var barTrack = new Texture( [0, 0, 60, 200], 1, 1 );
		var barFill = new Texture( [255, 255, 255, 255], 1, 1 );

		Render.ClearColor = new RgbaFloat( 0.35f, 0.72f, 0.92f, 1f );

		const float loadingScale = 4f;
		const float statusScale = 2f;
		void DrawLoading()
		{
			if ( loadingFont == null )
				return;

			// Centre "LOADING..." on the actual ink (the line box's descender space would push it
			// high). DrawText's originY is the line top; place it so the ink midpoint lands on the
			// screen centre.
			const string text = "LOADING...";
			var (inkTop, inkBottom) = loadingFont.Atlas.InkBounds( text );
			var centerY = Screen.Size.Y / 2f + (inkTop + inkBottom) / 2f * loadingScale;
			Graphics.DrawText( loadingFont, text, Screen.Size.X / 2f, centerY, TextAlign.Center, loadingScale );

			// Progress bar (T-030): a track + a fill proportional to LoadProgress.Progress, centred
			// horizontally above the status line.
			const float barWidth = 600f, barHeight = 22f;
			var barX = Screen.Size.X / 2f - barWidth / 2f;
			var barY = 120f;
			Material.UI.Set( "Color", barTrack );
			Graphics.Quad( new Rectangle( barX, barY, barWidth, barHeight ), Material.UI );
			if ( LoadProgress.Progress > 0f )
			{
				Material.UI.Set( "Color", barFill );
				Graphics.Quad( new Rectangle( barX, barY, barWidth * LoadProgress.Progress, barHeight ), Material.UI );
			}

			// Current load step, near the bottom of the screen.
			var status = LoadProgress.Status;
			if ( !string.IsNullOrEmpty( status ) )
			{
				var (sTop, sBottom) = loadingFont.Atlas.InkBounds( status );
				var statusY = 60f + (sTop + sBottom) / 2f * statusScale;
				Graphics.DrawText( loadingFont, status, Screen.Size.X / 2f, statusY, TextAlign.Center, statusScale );
			}
		}

		// While the level loads (synchronously, on this thread), each LoadProgress.Report checkpoint
		// pumps events and re-presents this screen — so the window updates with the current step and
		// stays responsive instead of freezing on a static frame. See T-030.
		LoadProgress.OnReport = () => Render.RenderLoadingScreen( DrawLoading );

		// Show the loading screen for a brief minimum so it's actually visible before the heavy work.
		var loadingStart = System.Diagnostics.Stopwatch.StartNew();
		do
		{
			Render.RenderLoadingScreen( DrawLoading );
		}
		while ( loadingStart.ElapsedMilliseconds < 1500 );

		//
		// Entry point (T-063): the default launch shows the front-end MENU (title + world select). Only the
		// test runtime jumps straight into a park — set OPENTPW_TESTPARK=<jungle|hallow|fantasy|space> (add
		// OPENTPW_AUTOPLACE for a pre-populated dev park). This keeps the shipped menu flow off the test path.
		//
		var testPark = Environment.GetEnvironmentVariable( "OPENTPW_TESTPARK" )
			?? Environment.GetEnvironmentVariable( "OPENTPW_LEVEL" ); // legacy alias, still test-only
		var level = string.IsNullOrWhiteSpace( testPark )
			? Level.CreateMenu()
			: new Level( LevelTheme.Resolve( testPark ) );

		LoadProgress.Done();
		Render.ClearColor = RgbaFloat.Black;

		// Music + ambience are started by the level (LevelSoundscape): the front-end's here, a park's own on entry.

		//
		// SIGTERM / SIGINT (T-069): the runtime's default handling exits the process while the render
		// loop still owns the Vulkan/SDL/OpenAL resources, which segfaults. Cancel that and shut down
		// through the normal window-close path instead. Keep the registrations alive for the loop.
		//
		using var sigTerm = PosixSignalRegistration.Create( PosixSignal.SIGTERM, OnQuitSignal );
		using var sigInt = PosixSignalRegistration.Create( PosixSignal.SIGINT, OnQuitSignal );

		//
		// Run game loop
		//
		Render.OnUpdate += level.Update;
		Render.OnUpdate += Audio.UpdateListener; // keep the 3D-audio listener on the camera (T-047)
		Render.OnRender += level.Render;
		Render.Run();
	}

	private static void OnQuitSignal( PosixSignalContext context )
	{
		context.Cancel = true;
		Log.Info( $"{context.Signal} received, quitting" );
		Render.RequestQuit();
	}
}
