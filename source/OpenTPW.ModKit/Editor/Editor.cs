using ImGuiNET;
using OpenTPW.ModKit;
using StbImageWriteSharp;
using System.Reflection;
using Veldrid;

namespace OpenTPW;
public partial class Editor
{
	public static Editor Instance { get; private set; } = null!;

	public ImGuiRenderer imguiRenderer { get; private set; }
	public GraphicsDevice graphicsDevice { get; private set; }

	private List<BaseTab> tabs = new();

	public static ImFontPtr MonospaceFont { get; private set; }
	public static ImFontPtr SansSerifFont { get; private set; }

	public Editor( ImGuiRenderer imguiRenderer, GraphicsDevice graphicsDevice )
	{
		Instance ??= this;

		this.imguiRenderer = imguiRenderer;
		this.graphicsDevice = graphicsDevice;

		InitIO();

		tabs.AddRange( new BaseTab[] {
			new DemoWindow(),
			new FileBrowser()
		} );

		tabs.ForEach( x => x.ImGuiRenderer = this.imguiRenderer );
	}

	private static void SetKeyMappings( ImGuiIOPtr io )
	{
		io.KeyMap[(int)ImGuiKey.Tab] = (int)Key.Tab;
		io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Key.Left;
		io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Key.Right;
		io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Key.Up;
		io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Key.Down;
		io.KeyMap[(int)ImGuiKey.PageUp] = (int)Key.PageUp;
		io.KeyMap[(int)ImGuiKey.PageDown] = (int)Key.PageDown;
		io.KeyMap[(int)ImGuiKey.Home] = (int)Key.Home;
		io.KeyMap[(int)ImGuiKey.End] = (int)Key.End;
		io.KeyMap[(int)ImGuiKey.Delete] = (int)Key.Delete;
		io.KeyMap[(int)ImGuiKey.Backspace] = (int)Key.BackSpace;
		io.KeyMap[(int)ImGuiKey.Enter] = (int)Key.Enter;
		io.KeyMap[(int)ImGuiKey.Escape] = (int)Key.Escape;
		io.KeyMap[(int)ImGuiKey.A] = (int)Key.A;
		io.KeyMap[(int)ImGuiKey.C] = (int)Key.C;
		io.KeyMap[(int)ImGuiKey.V] = (int)Key.V;
		io.KeyMap[(int)ImGuiKey.X] = (int)Key.X;
		io.KeyMap[(int)ImGuiKey.Y] = (int)Key.Y;
		io.KeyMap[(int)ImGuiKey.Z] = (int)Key.Z;
	}

	private void InitIO()
	{
		var io = ImGui.GetIO();

		io.ConfigFlags |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.IsSRGB;
		ImGui.LoadIniSettingsFromDisk( ImGui.GetIO().IniFilename ); // https://github.com/mellinoe/veldrid/issues/410

		// io.Fonts.Clear();
		// io.Fonts.AddFontFromFileTTF( "C:\\Windows\\Fonts\\segoeui.ttf", 16f );
		// io.Fonts.Build();
		// imguiRenderer.RecreateFontDeviceTexture( graphicsDevice );

		SetKeyMappings( io );
	}

	private void DrawMenuBar()
	{
		ImGui.BeginMainMenuBar();

		foreach ( var tab in tabs )
		{
			var editorMenuAttribute = tab.GetType().GetCustomAttribute<EditorMenuAttribute>();
			if ( editorMenuAttribute == null )
				continue;

			var splitPath = editorMenuAttribute.Path.Split( '/' );

			if ( ImGui.BeginMenu( splitPath[0] ) )
			{
				for ( int i = 1; i < splitPath.Length; i++ )
				{
					string? item = splitPath[i];
					bool active = ImGui.MenuItem( item );

					if ( i == splitPath.Length - 1 && active )
						tab.visible = !tab.visible;
				}

				ImGui.EndMenu();
			}
		}

		ImGui.EndMainMenuBar();
	}

	public void UpdateFrom( InputSnapshot inputSnapshot )
	{
		imguiRenderer.Update( 1 / 60f, inputSnapshot );

		DrawMenuBar();

		ImGui.DockSpaceOverViewport( ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode );

		tabs.ForEach( tab =>
		{
			if ( tab.visible )
				tab.Draw();
		} );
	}

	public async Task<string> CacheThumbnailAsync( string path )
	{
		if ( !path.EndsWith( ".wct" ) )
			return "";

		var thumbnailPath = FileSystem.GetRelativePath( path );
		thumbnailPath = Path.ChangeExtension( thumbnailPath, ".png" );
		thumbnailPath = "/thumbcache" + thumbnailPath;

		await Task.Run( () =>
		{
			var textureFile = new TextureFile( path );
			var t = textureFile.Data;

			// A 16x16 box-filtered thumbnail: each pixel averages its block of the source.
			const int size = 16;
			var thumbnail = new byte[size * size * 4];
			for ( int y = 0; y < size; y++ )
				for ( int x = 0; x < size; x++ )
				{
					int x0 = x * t.Width / size, x1 = Math.Max( x0 + 1, (x + 1) * t.Width / size );
					int y0 = y * t.Height / size, y1 = Math.Max( y0 + 1, (y + 1) * t.Height / size );
					for ( int c = 0; c < 4; c++ )
					{
						int sum = 0;
						for ( int sy = y0; sy < y1; sy++ )
							for ( int sx = x0; sx < x1; sx++ )
								sum += t.Data[(sy * t.Width + sx) * 4 + c];
						thumbnail[(y * size + x) * 4 + c] = (byte)(sum / ((x1 - x0) * (y1 - y0)));
					}
				}

			using var writeStream = CacheFileSystem.OpenWrite( thumbnailPath );
			new ImageWriter().WritePng( thumbnail, size, size, ColorComponents.RedGreenBlueAlpha, writeStream );

			Log.Info( $"Cached to {thumbnailPath}" );
		} );

		return thumbnailPath;
	}
}

