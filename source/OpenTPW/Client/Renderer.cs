using Veldrid;
using Veldrid.StartupUtilities;

namespace OpenTPW;

public partial class Renderer
{
	// Monotonic frame clock (cheaper and jump-free vs DateTime.Now). See T-028.
	private readonly System.Diagnostics.Stopwatch _frameClock = System.Diagnostics.Stopwatch.StartNew();
	private double _lastFrameSeconds;
	public CommandList CommandList = null!;

	// Toggleable FPS overlay (F3). Smoothed over ~0.5s. The original game had an "FPS: %4g" counter
	// too (see docs/07-ghidra-render.md).
	public static bool ShowFps = false; // off by default; the debug toggle (backtick) drives it (T-065)
	private double _fpsAccum;
	private int _fpsFrames;
	private float _fps;
	private bool _fpsKeyWas;
	private bool _volDownWas;
	private bool _volUpWas;
	private bool _f2Was;
	private bool _freeCam;
	private Font? _debugFont;

	public Window Window;

	public Action? PreUpdate;
	public Action? OnUpdate;
	public Action? PostUpdate;

	public Action? OnRender;

	/// <summary>Colour the main framebuffer is cleared to each frame (e.g. for the loading screen).</summary>
	public RgbaFloat ClearColor = RgbaFloat.Black;

	private bool _presentReady;

	// Set by RequestQuit (possibly from a signal-handler thread); the render thread acts on it.
	private volatile bool _quitRequested;

	public Renderer()
	{
		Window = new( Settings.Default.GameWindowSize.X, Settings.Default.GameWindowSize.Y, "Theme Park World", true );
		Window.OnResized = OnWindowResized;
		Window.Visible = true;

		CreateGraphicsDevice();
		// Swap the buffers so that the screen isn't a mangled mess
		Device.SwapBuffers();
		CreateMultisampledFramebuffer();

		CommandList = Device.ResourceFactory.CreateCommandList();
		_lastFrameSeconds = _frameClock.Elapsed.TotalSeconds;
	}

	private void CreateMultisampledFramebuffer()
	{
		var colorTextureInfo = TextureDescription.Texture2D(
			(uint)(Screen.Size.X),
			(uint)(Screen.Size.Y),
			1,
			1,
			PixelFormat.B8_G8_R8_A8_UNorm,
			TextureUsage.RenderTarget,
			TextureSampleCount.Count4
		);

		var colorTexture = Device.ResourceFactory.CreateTexture( colorTextureInfo );

		var depthTextureInfo = TextureDescription.Texture2D(
			(uint)(Screen.Size.X),
			(uint)(Screen.Size.Y),
			1,
			1,
			PixelFormat.D32_Float_S8_UInt,
			TextureUsage.DepthStencil,
			TextureSampleCount.Count4
		);

		var depthTexture = Device.ResourceFactory.CreateTexture( depthTextureInfo );

		colorTextureInfo.SampleCount = TextureSampleCount.Count1;
		colorTextureInfo.Usage = TextureUsage.Sampled;

		ResolveColorTexture = Device.ResourceFactory.CreateTexture( colorTextureInfo );

		var framebufferAttachmentInfo = new FramebufferAttachmentDescription( colorTexture, 0 );
		var depthAttachmentInfo = new FramebufferAttachmentDescription( depthTexture, 0 );
		var framebufferDescription = new FramebufferDescription()
		{
			ColorTargets = [framebufferAttachmentInfo],
			DepthTarget = depthAttachmentInfo
		};

		MultisampledFramebuffer = Device.ResourceFactory.CreateFramebuffer( framebufferDescription );
	}

	public Framebuffer MultisampledFramebuffer = null!;
	public Veldrid.Texture ResolveColorTexture = null!;

	private Pipeline _blitPipeline = null!;
	private ResourceSet _blitResourceSet = null!;
	private ResourceLayout _blitResourceLayout = null!;

	// Sets up the blit pipeline that copies the resolved scene to the swapchain. Split out of
	// Run() so a loading frame can be presented before the (synchronous) level load. Idempotent.
	private void SetupPresent()
	{
		if ( _presentReady )
			return;

		var layoutDescription = new ResourceLayoutDescription(
			new ResourceLayoutElementDescription( "g_tInput", ResourceKind.TextureReadOnly, ShaderStages.Fragment ),
			new ResourceLayoutElementDescription( "g_sSampler", ResourceKind.Sampler, ShaderStages.Fragment )
		);

		_blitResourceLayout = Device.ResourceFactory.CreateResourceLayout( layoutDescription );

		// Create shader
		var shader = new Shader( "content/shaders/blit.shader" );
		var blitShader = shader.ShaderProgram;

		var pipelineDescription = new GraphicsPipelineDescription(
			BlendStateDescription.SingleAlphaBlend,
			DepthStencilStateDescription.Disabled,
			RasterizerStateDescription.CullNone,
			PrimitiveTopology.TriangleList,
			new ShaderSetDescription(
				Array.Empty<VertexLayoutDescription>(),
				shader.ShaderProgram
			),
			[_blitResourceLayout],
			Device.MainSwapchain.Framebuffer.OutputDescription
		);

		_blitPipeline = Device.ResourceFactory.CreateGraphicsPipeline( pipelineDescription );

		_blitResourceSet = Device.ResourceFactory.CreateResourceSet( new ResourceSetDescription(
			_blitResourceLayout,
			ResolveColorTexture,
			Device.LinearSampler
		) );

		OnWindowResized( Window.Size );

		// Small bitmap font for the FPS overlay, loaded here (before any command list is open).
		try { _debugFont = new Font( GameLanguage.File( "GAME12.bf4" ) ); }
		catch ( Exception e ) { Log.Warning( $"FPS overlay font unavailable: {e.Message}" ); }

		_presentReady = true;
	}

	/// <summary>
	/// Asks the game to exit at the start of the next frame, through the same window-close path as
	/// the window manager's close button (T-068/T-069). Safe to call from any thread.
	/// </summary>
	public void RequestQuit() => _quitRequested = true;

	public void Run()
	{
		SetupPresent();

		while ( Window.SdlWindow.Exists )
		{
			Update();
		}
	}

	/// <summary>
	/// Presents a single frame using <paramref name="draw"/> as the scene (e.g. a loading
	/// screen), so the window shows something during a long synchronous load instead of black.
	/// </summary>
	public void RenderLoadingScreen( Action draw )
	{
		SetupPresent();
		Window.SdlWindow.PumpEvents();

		var previous = OnRender;
		OnRender = draw;
		PreRender();
		PostRender();
		OnRender = previous;
	}

	private void PreRender()
	{
		// Recompile any hot-reloaded shaders. Draining a queue avoids scanning every loaded asset
		// (Asset.All.OfType<Shader>()) each frame — see T-028. Bounded to the count present at entry
		// so a watcher firing on another thread can never feed this loop indefinitely within a frame.
		for ( int remaining = Shader.DirtyShaders.Count;
			  remaining > 0 && Shader.DirtyShaders.TryDequeue( out var shader );
			  remaining-- )
			shader.Recompile();

		CommandList.Begin();
	}

	private void PostRender()
	{
		CommandList.SetFramebuffer( MultisampledFramebuffer ); // Use MSAA framebuffer
		CommandList.SetViewport( 0, new Viewport( 0, 0, MultisampledFramebuffer.Width, MultisampledFramebuffer.Height, 0, 1 ) );
		CommandList.SetFullViewports();
		CommandList.SetFullScissorRects();
		CommandList.ClearDepthStencil( 1 );
		CommandList.ClearColorTarget( 0, ClearColor );

		// Render level to MSAA buffer
		CommandList.PushDebugGroup( "Main Render" );
		OnRender?.Invoke();
		DrawOverlay();         // FPS overlay on top of the scene
		Graphics.FlushBatch(); // emit any accumulated UI batch before the resolve (T-027)
		CommandList.PopDebugGroup();

		// Resolve MSAA to non-MSAA texture
		CommandList.ResolveTexture( MultisampledFramebuffer.ColorTargets[0].Target, ResolveColorTexture );

		// Blit to screen
		CommandList.SetFramebuffer( Device.MainSwapchain.Framebuffer );
		CommandList.SetViewport( 0, new Viewport( 0, 0, Device.MainSwapchain.Framebuffer.Width, Device.MainSwapchain.Framebuffer.Height, 0, 1 ) );

		CommandList.SetPipeline( _blitPipeline );
		CommandList.SetGraphicsResourceSet( 0, _blitResourceSet );
		CommandList.Draw( 3, 1, 0, 0 );

		CommandList.End();

		Device.SubmitCommands( CommandList );

		try
		{
			Device.SwapBuffers();
		}
		catch ( VeldridException ) when ( Window.SdlWindow.Exists )
		{
			// Swapchain went out of date (resize/minimize). Recreate it and skip this frame's present.
			OnWindowResized( Window.Size );
		}
		// If the window is gone (closing), the acquire/present fails harmlessly — Run() will exit.
	}

	// Draws the FPS overlay (top-left) when enabled. Uses the shared UI batch so it merges/flushes
	// with the rest of the UI. Lazily loads a small bitmap font; any failure just disables it.
	private void DrawOverlay()
	{
		// Gate on _fps > 0 so it doesn't flash "FPS 0" on the loading screen (Update, which measures
		// fps, doesn't run during the load — only RenderLoadingScreen does).
		if ( !ShowFps || _debugFont == null || _fps <= 0f )
			return;

		Graphics.DrawText( _debugFont, $"FPS {_fps:F0}", 16f, Screen.Size.Y - 16f, TextAlign.Left, 2f );
	}

	private void Update()
	{
		var nowSeconds = _frameClock.Elapsed.TotalSeconds;
		float deltaTime = (float)(nowSeconds - _lastFrameSeconds);
		_lastFrameSeconds = nowSeconds;

		// Close on the render thread, before pumping, so the pump below tears the window down exactly
		// as a user close would — never in the middle of a frame.
		if ( _quitRequested )
			Window.SdlWindow.Close();

		InputSnapshot inputSnapshot = Window.SdlWindow.PumpEvents();

		// The pump may have processed a window-close; don't render into a surface that's gone (it
		// throws "could not acquire next image" in SwapBuffers). Run()'s while-condition then exits.
		if ( !Window.SdlWindow.Exists )
			return;

		Time.Update( deltaTime );
		Input.UpdateFrom( inputSnapshot );

		// FPS overlay: measure (smoothed) and toggle on F3 rising-edge.
		_fpsAccum += deltaTime;
		_fpsFrames++;
		if ( _fpsAccum >= 0.5 )
		{
			_fps = (float)(_fpsFrames / _fpsAccum);
			_fpsAccum = 0;
			_fpsFrames = 0;
		}
		var f3 = Input.Keyboard.KeysDown.Contains( Key.F3 );
		if ( f3 && !_fpsKeyWas )
			ShowFps = !ShowFps;
		_fpsKeyWas = f3;

		// Music volume: '-' down, '+' up in 0.1 steps (edge-detected).
		var volDown = Input.Keyboard.KeysDown.Contains( Key.Minus );
		var volUp = Input.Keyboard.KeysDown.Contains( Key.Plus );
		if ( volDown && !_volDownWas )
		{
			Audio.MusicVolume -= 0.1f;
			Log.Info( $"Music volume: {Audio.MusicVolume:F1}" );
			GameSettings.Current.MusicVolume = Audio.MusicVolume;
			GameSettings.Current.Save();
		}
		if ( volUp && !_volUpWas )
		{
			Audio.MusicVolume += 0.1f;
			Log.Info( $"Music volume: {Audio.MusicVolume:F1}" );
			GameSettings.Current.MusicVolume = Audio.MusicVolume;
			GameSettings.Current.Save();
		}
		_volDownWas = volDown;
		_volUpWas = volUp;

		// F2 toggles the free-fly debug camera (explore the scene) vs the fixed lobby orbit.
		var f2 = Input.Keyboard.KeysDown.Contains( Key.F2 );
		if ( f2 && !_f2Was )
		{
			_freeCam = !_freeCam;
			if ( _freeCam )
				Camera.SetCameraMode<FreeCameraMode>();
			else
				Camera.SetCameraMode<LobbyCameraMode>();
			Log.Info( $"Camera: {( _freeCam ? "free (WASD move, hold right-mouse to look, wheel speed, Space/Ctrl up/down)" : "lobby orbit" )}" );
		}
		_f2Was = f2;

		PreRender();
		PreUpdate?.Invoke();

		OnUpdate?.Invoke();

		PostRender();
		PostUpdate?.Invoke();

		ProcessDeletionQueue();
	}

	private void CreateGraphicsDevice()
	{
		var options = new GraphicsDeviceOptions()
		{
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
			SwapchainDepthFormat = null,
			SwapchainSrgbFormat = false,
			SyncToVerticalBlank = true,
			HasMainSwapchain = true
		};

		var swapchainSource = VeldridStartup.GetSwapchainSource( Window.SdlWindow );
		Device = GraphicsDevice.CreateVulkan( swapchainDescription: new SwapchainDescription( swapchainSource, (uint)(Window.Size.X), (uint)(Window.Size.Y), options.SwapchainDepthFormat, options.SyncToVerticalBlank, options.SwapchainSrgbFormat ), options: options );
	}

	public void OnWindowResized( Point2 newSize )
	{
		var dpiScale = 1.0f;
		Device.MainSwapchain.Resize( (uint)(newSize.X * dpiScale), (uint)(newSize.Y * dpiScale) );

		// Cleanup old MSAA resources
		MultisampledFramebuffer?.Dispose();
		ResolveColorTexture?.Dispose();

		// Recreate MSAA resources
		CreateMultisampledFramebuffer();

		// Recreate blit resources since they depend on the framebuffer
		_blitResourceSet?.Dispose();
		_blitResourceSet = Device.ResourceFactory.CreateResourceSet( new ResourceSetDescription(
			_blitResourceLayout,
			ResolveColorTexture,
			Device.LinearSampler
		) );
	}

	public void ImmediateSubmit( Action<CommandList> action )
	{
		var commandList = Device.ResourceFactory.CreateCommandList();
		commandList.Begin();

		action( commandList );

		commandList.End();
		Device.SubmitCommands( commandList );

		ScheduleDelete( commandList.Dispose );
	}
}
