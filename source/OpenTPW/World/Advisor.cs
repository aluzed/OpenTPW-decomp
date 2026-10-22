using System.IO;
using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The Theme Park World advisor — the "Bug Head" character (<c>global/advisor.wad</c> → <c>Advisor.MD2</c>)
/// that gives the player tips. T-046: renders the real model and lip-syncs it. The model carries the five
/// viseme mouths as named sub-meshes — <c>Mouth - Normal/Aah/Eee/Ooh/Sss</c> (RE'd from the engine's
/// mouth-mesh selector <c>FUN_0044b2e0</c>, see <see cref="MouthShapeExtensions.MeshPartName"/>); we show
/// the one matching the current viseme and hide the other four. The viseme is driven from a real speech
/// clip + its companion <c>.LIP</c> (the timing wired in T-020). The advisor is anchored upright in front
/// of the camera so it reads as a corner overlay. Enabled by <c>OPENTPW_ADVISOR_DEMO=1</c>.
/// He gestures as he speaks, and now and then between tips: the model's object tree (<see cref="ModelHierarchy"/>:
/// his neck and shoulders are nodes above the meshes) is posed from one of his <c>m*.MD2</c> gestures
/// (<see cref="GestureFile"/>), which also blink his eyes and bring out the ticket or the map.
/// </summary>
public sealed class Advisor : Entity
{
	public static Advisor? Current { get; private set; }

	// The original shows the advisor whole — head, eyes, antennae, the ball body, both gloves and the theme's hat
	// (docs/10-ui-reference.md). Parts it only shows for a gesture stay hidden: the shut eyes (blink), the golden
	// ticket and map he holds up, the "mutant" eye, the spatula.
	private static readonly string[] HiddenParts =
	{
		"shuteye", "ticket", "map", "mutant", "spatula", " hat", "hardhat", "btie",
	};

	private static bool IsHidden( string part )
		=> Array.Exists( HiddenParts, h => part.Contains( h, StringComparison.OrdinalIgnoreCase ) );

	private sealed class Part
	{
		public ModelEntity Entity = null!;
		public Model Model = null!;
		public Vector3 LocalOffset;
		public Quaternion LocalRotation;
		public Vector3 LocalScale;
		public bool AlwaysShown;
		public MouthShape? Viseme; // set for the five "Mouth - *" parts
		public int Mesh;           // its number in the model's object tree
		// For a gesture's vertex morph (the gloves' fingers): the rest positions, and each mesh position's vertices.
		public Vector3[] Rest = Array.Empty<Vector3>();
		public int[][] VerticesOf = Array.Empty<int[]>();
		public bool Morphed;
	}

	// The model's object tree and his gestures; the one playing (null: at rest) and when it started.
	private ModelHierarchy? tree;
	private readonly List<GestureFile> gestures = new();
	// The props a gesture brings out (the golden ticket, the map): such gestures go with the advice about them only.
	private readonly Dictionary<GestureFile, string> gestureProp = new();
	private GestureFile? gesture;
	private float gestureStart, nextIdleGesture = 6f;
	private const float IdleGestureMin = 8f, IdleGestureMax = 16f, IdleGestureLongest = 2.5f;

	private readonly List<Part> parts = new();
	private LipSyncFile? lip;
	private string clipName = "";
	private float startTime = -1f;

	// The message system (T-046): the real Advisor.sam pacing/group rules decide WHEN and WHICH tip plays.
	// A say-once welcome (tutorial group) opens; thereafter the advisor speaks whatever the live park state
	// justifies (AdvisorAdvice maps the state → scored candidates), so tips fire on real events.
	private AdvisorConfig config = null!;
	private AdvisorMessages messages = null!;
	private string activeMessage = "";

	// On-screen anchoring, measured on the original (docs/10-ui-reference.md): the head's centre sits ~195 px left of
	// the right edge and 79 % down a 720-px screen, ~20 % of the screen high; he faces the player squarely.
	private const float Distance = 26f;
	private const float HeadFromRight = 195f;  // base-screen pixels from the right edge to the head's centre
	private const float HeadUp = 150f;         // base-screen pixels from the bottom
	private const float HeadPixels = 100f;     // the head's height on screen
	private float headUnits = 22f;             // the Bug Head mesh's height in model units (measured on load)
	private const float LeanBack = 0.26f;             // radians (15°)

	/// <summary>The viseme currently shown (for the HUD label); Closed when not speaking.</summary>
	public MouthShape CurrentShape { get; private set; } = MouthShape.Closed;
	public string ClipName => clipName;
	/// <summary>The id of the advice currently being spoken (for the HUD tip text); empty when idle.</summary>
	public string ActiveMessage => activeMessage;
	public float Elapsed => startTime < 0f ? 0f : Time.Now - startTime;
	/// <summary>How long the clip speaks: its audio (samples over rate) — the lip-sync's last keyframe can come
	/// early (a one-keyframe take ends at 0 s, and he spoke over himself).</summary>
	public float ClipLength => MathF.Max( (float)(lip?.Duration.TotalSeconds ?? 0), audioSeconds );
	private float audioSeconds;

	public Advisor()
	{
		Current = this;
		Name = "Advisor";
		BuildParts();
		LoadGlobalSpeech();

		config = AdvisorConfig.Load();
		messages = new AdvisorMessages( config );
		Log.Info( $"[advisor] message config: {config.MessageGroups.Count} group(s), "
			+ $"minGap {config.MinTimeAnyMessage:0}s, minScore {config.MinScoreForConsideration:0}" );
		// First tip fires on the next update via the scheduler (no unconditional StartSpeech).
	}

	private void BuildParts()
	{
		ModelFile model;
		try
		{
			// Each world dresses him for the theme (Advisor_JUN: the pith helmet…) — fall back to the plain one.
			var theme = Level.Current?.Name ?? "";
			string? themed = theme.Length >= 3 && Level.Current?.InPark == true ? $"Advisor_{theme[..3].ToUpperInvariant()}.MD2" : null; // the lobby's is the plain one
			var themeWad = Path.Join( GameDir.GamePath, "data", "levels", theme, "advisor.wad" );
			WadArchive wad;
			string name;
			if ( themed != null && File.Exists( themeWad ) && new WadArchive( themeWad ) is var levelWad
				&& levelWad.GetFile( themed ) is { } themedFile )
			{
				(wad, name) = (levelWad, themed);
				themeTextures = $"levels/{theme}/advisor/textures";
			}
			else
				(wad, name) = (new WadArchive( Path.Join( GameDir.GamePath, "data", "global", "advisor.wad" ) ), "Advisor.MD2");
			var bytes = wad.GetFile( name ).GetData();
			model = new ModelFile( new MemoryStream( bytes ) );
			tree = ModelHierarchy.Read( bytes );
			// His gestures sit beside him: Advisor_JUNm1..20.MD2, Advisorm1..15.MD2.
			var stem = Path.GetFileNameWithoutExtension( name );
			for ( int n = 1; n <= 30; n++ )
				if ( wad.GetFile( $"{stem}m{n}.MD2" ) is { } file && new GestureFile( file.GetData() ) is { Tracks.Count: > 0 } g )
					gestures.Add( g );
			foreach ( var g in gestures )
				foreach ( var t in g.Tracks )
					if ( t.Target < model.Meshes.Count && (t.Visibility.Any( v => v.Visible ) || t.Scale.Count > 0 || t.Rotation.Count > 0)
						&& model.Meshes[t.Target].Name.Replace( "\0", "" ).Trim() is var part
						&& (part.Contains( "ticket", StringComparison.OrdinalIgnoreCase ) || part.Contains( "map", StringComparison.OrdinalIgnoreCase )) )
						gestureProp[g] = part.Contains( "map", StringComparison.OrdinalIgnoreCase ) ? "map" : "ticket";
			Log.Info( $"[advisor] {gestures.Count} gesture(s) ({gestureProp.Count} with a prop), tree of {tree?.ObjectCount ?? 0} object(s)" );
			Log.Info( $"[advisor] {themed ?? "Advisor.MD2"} loaded: {model.Meshes.Count} mesh(es): {string.Join( ", ", model.Meshes.Select( m => m.Name.Trim( '\0', ' ' ) ) )}" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[advisor] model load failed: {e.Message}" );
			return;
		}

		for ( int index = 0; index < model.Meshes.Count; index++ )
		{
			var mesh = model.Meshes[index];
			var name = (mesh.Name ?? "").Replace( "\0", "" ).Trim();
			var viseme = VisemeForMesh( name );
			var isBase = viseme == null;
			if ( name.Equals( "bug head", StringComparison.OrdinalIgnoreCase ) && mesh.Vertices.Length > 0 )
			{
				// The models differ in scale (the lobby's plain one vs a theme's): size him by his head.
				Matrix4x4.Decompose( mesh.TransformMatrix, out var hs, out _, out _ );
				float lo = mesh.Vertices.Min( v => v.Position.Z ), hi = mesh.Vertices.Max( v => v.Position.Z );
				headUnits = MathF.Max( 1f, (hi - lo) * hs.Z );
			}
			// The parts only a gesture shows are kept, hidden, when he can gesture.
			bool hiddenAtRest = IsHidden( name );
			if ( hiddenAtRest && (tree == null || gestures.Count == 0) )
				continue;

			var built = BuildMesh( mesh );
			if ( built == null )
				continue;

			Matrix4x4.Decompose( mesh.TransformMatrix, out var scl, out var rot, out var pos );
			parts.Add( new Part
			{
				Entity = new ModelEntity { Model = isBase ? built : null },
				Model = built,
				// Match Ride.BuildMeshEntities' Y<->Z swap so local placement is consistent with the verts.
				LocalOffset = new Vector3( pos.X, pos.Z, pos.Y ),
				LocalRotation = new Quaternion( rot.X, rot.Z, rot.Y, -rot.W ),
				LocalScale = new Vector3( scl.X, scl.Z, scl.Y ),
				AlwaysShown = isBase && !hiddenAtRest,
				Viseme = viseme,
				Mesh = index,
				Rest = built.Vertices.Select( v => v.Position ).ToArray(),
				VerticesOf = Enumerable.Range( 0, mesh.VertexOrder.Length == 0 ? 0 : mesh.VertexOrder.Max() + 1 )
					.Select( position => Enumerable.Range( 0, mesh.VertexOrder.Length ).Where( j => mesh.VertexOrder[j] == position ).ToArray() ).ToArray(),
			} );
		}
		Log.Info( $"[advisor] built {parts.Count} part(s) (5 visemes + body), head {headUnits:0.#} units" );
	}

	private static MouthShape? VisemeForMesh( string name )
	{
		foreach ( MouthShape s in new[] { MouthShape.Normal, MouthShape.Aah, MouthShape.Eee, MouthShape.Ooh, MouthShape.Sss } )
			if ( string.Equals( name, s.MeshPartName(), StringComparison.OrdinalIgnoreCase ) )
				return s;
		return null;
	}

	// The theme's own advisor textures (the hat), searched before the shared global ones.
	private static string? themeTextures;

	private static Texture AdvisorTexture( string name )
	{
		foreach ( var dir in new[] { themeTextures, "global/advisor/textures" } )
		{
			if ( dir == null )
				continue;
			try { return new Texture( $"{dir}/{name}.wct", TextureFlags.Repeat ); }
			catch { }
		}
		return Texture.Missing;
	}

	// Builds a renderable Model from one MD2 mesh (the Ride.BuildMeshEntities texture/vertex pattern).
	private static Model? BuildMesh( ModelFile.Mesh mesh )
	{
		try
		{
			// Double-sided: the gloves are mirrored copies, whose faces would otherwise be culled.
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader", MaterialFlags.DoubleSided );
			var textures = new List<Texture>();
			for ( int i = 0; i < 16; ++i )
			{
				if ( mesh.Materials.Length <= i ) { textures.Add( Texture.Missing ); continue; }
				try { textures.Add( AdvisorTexture( mesh.Materials[i].Name.Trim( '\0', ' ' ) ) ); }
				catch { textures.Add( Texture.Missing ); }
			}
			material.Set( "Color", [.. textures] );

			var vertices = new List<Vertex>( mesh.Vertices.Length );
			for ( int i = 0; i < mesh.Vertices.Length; ++i )
			{
				vertices.Add( new Vertex
				{
					Position = new Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
					Normal = mesh.Normals[i],
					TexCoords = mesh.TexCoords[i],
					TexIndex = (int)mesh.Vertices[i].TextureIndex,
					MatFlags = mesh.Materials.Length == 0 ? 0 : mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags,
				} );
			}
			return new Model( [.. vertices], mesh.Indices, material );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[advisor] mesh '{mesh.Name}' build failed: {e.Message}" );
			return null;
		}
	}

	// Speak the message with id `messageId`: one of the game's recorded takes of its TAG_SYSTEM text
	// (AdvisorSpeech — global speech + lips.wad); the welcome is the level's own clip sp_001. Advice the game never
	// voiced is shown (hint strip, message tag) with a short silent gesture — it used to replay the 28 s welcome.
	private bool Speak( string messageId )
	{
		try
		{
			if ( AdvisorTips.TextIndexOf( messageId ) is { } text && AdvisorSpeech.ClipFor( text ) is { } number
				&& SpeakGlobal( messageId, $"sp_{number:000}" ) )
				return true;
			if ( messageId != "WelcomeTutorial" )
				return Show( messageId );

			var theme = Level.Current?.Name ?? "jungle"; // the welcome is per-theme (T-062)
			// The level's speech (the language overlay's voice when there is one — GameDir.DataFile).
			var sdtPath = GameDir.DataFile( "levels", theme, "Speech", "speechHD.SDT" );
			var speechDir = Path.GetDirectoryName( sdtPath )!;
			if ( !File.Exists( sdtPath ) )
				return false;

			var archive = new SdtArchive( sdtPath );
			var clip = archive.soundFiles.FirstOrDefault( f => f.Name.StartsWith( "sp_001", StringComparison.OrdinalIgnoreCase ) );
			if ( clip == null )
				return false;

			var lipPath = Path.Join( speechDir, "lips", $"{Path.GetFileNameWithoutExtension( clip.Name )}.LIP" );
			if ( !File.Exists( lipPath ) )
				return false;

			return Say( messageId, clip, new LipSyncFile( File.OpenRead( lipPath ) ) );
		}
		catch ( Exception e ) { Log.Warning( $"[advisor] speech failed: {e.Message}" ); return false; }
	}

	// The global speech bank (24 MB): read once, in the background while the welcome plays — reading it on the
	// first advice stalled that frame for seconds, which the clip timer then counted as already spoken.
	private static Task<SdtArchive?>? globalSpeech;

	private static void LoadGlobalSpeech() => globalSpeech ??= Task.Run( () =>
	{
		var sdtPath = GameDir.DataFile( "global", "Speech", "speechHD.SDT" );
		return File.Exists( sdtPath ) ? new SdtArchive( sdtPath ) : null;
	} );

	private bool SpeakGlobal( string messageId, string clipName )
	{
		LoadGlobalSpeech();
		if ( globalSpeech!.Result is not { } bank )
			return false;
		var clip = bank.soundFiles.FirstOrDefault( f => Path.GetFileNameWithoutExtension( f.Name ).Equals( clipName, StringComparison.OrdinalIgnoreCase ) );
		if ( clip == null )
			return false;
		using var stream = FileSystem.OpenRead( $"global/Speech/lips/{clipName}.LIP" );
		return Say( messageId, clip, new LipSyncFile( stream ) );
	}

	// Advice without a recording: its text, a gesture, and the time to read it.
	private const float SilentSeconds = 5f;

	// Starting a clip (decoding it) can stall that frame for seconds, which the frame clock then counts as already
	// spoken — the next advice cut in at once: the clip's start is set again on the next frame, when it really plays.
	private bool anchorPending;

	private bool Show( string messageId )
	{
		lip = null;
		clipName = "(text)";
		activeMessage = messageId;
		Post( messageId );
		startTime = Time.Now;
		audioSeconds = SilentSeconds;
		StartGesture( SilentSeconds + 1f );
		Log.Info( $"[advisor] message '{messageId}' → shown (no recording), {SilentSeconds:0}s" );
		return true;
	}

	// The message tag down the right edge for a spoken / shown message.
	private static void Post( string messageId )
	{
		if ( Level.Current?.InPark == true )
			MessageTags.Post( messageId.StartsWith( "InTheRed" ) || messageId.StartsWith( "Staff" ) && messageId.Contains( "Strik" ) || messageId == "Bankrupted"
				? MessageTags.Kind.Alert : messageId.StartsWith( "Research" ) ? MessageTags.Kind.News : MessageTags.Kind.Advice,
				"Advisor", AdvisorTips.TextFor( messageId ) );
	}

	private bool Say( string messageId, MP2File clip, LipSyncFile lipSync )
	{
		lip = lipSync;
		clipName = clip.Name;
		activeMessage = messageId;
		Post( messageId );
		Audio.PlaySpeech( $"advisor_{clip.Name}", clip.SoundData );
		startTime = Time.Now;
		anchorPending = true;
		audioSeconds = clip.SampleRate > 0 && clip.Samples > 0 ? (float)clip.Samples / clip.SampleRate : 0f;
		StartGesture( ClipLength + 1f, messageId.Contains( "GoldenTicket" ) ? "ticket" : null );
		Log.Info( $"[advisor] message '{messageId}' → speaking '{clipName}', {lip.Keyframes.Count} keyframes, {ClipLength:0.0}s" );
		return true;
	}

	// When the advisor is idle, ask the scheduler whether a tip should fire now. The Advisor.sam pacing/group
	// rules (min-gap, same-message gap, say-once, min-score) decide which — if any — message speaks.
	private void ConsiderSpeaking()
	{
		// In the lobby he just stands there: the advice is about a park.
		if ( Level.Current?.InPark != true && Environment.GetEnvironmentVariable( "OPENTPW_ADVISOR_DEMO" ) != "1" )
			return;
		bool speaking = startTime >= 0f && Time.Now - startTime <= ClipLength;
		if ( speaking )
			return;
		// After the welcome, wait for the speech bank (reading it inside an advice would stall that frame for seconds).
		if ( startTime >= 0f && globalSpeech is { IsCompleted: false } )
			return;
		// The park snapshot walks every entity several times: weigh the advice twice a second, not every frame.
		if ( Time.Now < nextConsider )
			return;
		nextConsider = Time.Now + ConsiderEvery;

		// A say-once welcome opens; then real park-state advice (each scored from the .sam params).
		messages.Submit( "WelcomeTutorial", config.Group( AdvisorAdvice.GroupTutorial ), 100f );
		foreach ( var (id, group, score) in AdvisorAdvice.Evaluate( BuildSnapshot(), config ) )
			messages.Submit( id, config.Group( group ), score );

		var pick = messages.Consider( Time.Now );
		if ( pick != null )
			Speak( pick );
	}

	// Gather the park figures the advice rules key off (T-046). The thirst/hunger thresholds come from the
	// .sam, so a level that retunes them retunes who counts as thirsty/hungry without code changes.
	// Previous milestone tallies, so BuildSnapshot can edge-trigger a one-shot advisor reaction the tick the
	// golden ticket is won / a challenge is won or lost (T-054/T-055), rather than re-firing while the state holds.
	private bool prevTicketAwarded;
	private int prevChallengesWon, prevChallengesLost;
	private readonly float[] complaintTally = new float[5], bargainTally = new float[5];
	private float nextConsider, lastTally = -1f;
	private const float ConsiderEvery = 0.5f;
	private const float PriceVerdictMemory = 30f; // seconds for a price verdict to fade (e-folding)

	private ParkSnapshot BuildSnapshot()
	{
		var fin = ParkFinances.Current;
		float thirstThresh = config.Param( "VisitorsThirsty", "ThirstierThan" ) ?? 99f;
		float hungerThresh = config.Param( "VisitorsHungry", "HungrierThan" ) ?? 99f;

		// Stall-price verdicts: a running tally that fades over ~30 s, so a pricing problem builds up to advice.
		var (newComplaints, newBargains) = Peep.TakePriceVerdicts();
		float elapsed = lastTally < 0f ? 0f : Time.Now - lastTally;
		lastTally = Time.Now;
		float keep = MathF.Exp( -elapsed / PriceVerdictMemory );
		var complaints = new int[newComplaints.Length];
		var bargains = new int[newBargains.Length];
		for ( int g = 0; g < complaints.Length; g++ )
		{
			complaintTally[g] = complaintTally[g] * keep + newComplaints[g];
			bargainTally[g] = bargainTally[g] * keep + newBargains[g];
			complaints[g] = (int)complaintTally[g];
			bargains[g] = (int)bargainTally[g];
		}
		bool ticketAwarded = GoldenTicketGoals.Current?.Awarded ?? false;
		int won = ChallengeManager.Current?.Won ?? 0;
		int lost = ChallengeManager.Current?.Lost ?? 0;
		bool ticketJustWon = ticketAwarded && !prevTicketAwarded;
		bool challengeJustWon = won > prevChallengesWon;
		bool challengeJustLost = lost > prevChallengesLost;
		prevTicketAwarded = ticketAwarded;
		prevChallengesWon = won;
		prevChallengesLost = lost;

		return new ParkSnapshot(
			Money: fin?.Money ?? 0f,
			MonthsInRed: fin?.MonthsInRed ?? 0,
			ThirstyVisitors: Peep.CountThirstierThan( thirstThresh ),
			HungryVisitors: Peep.CountHungrierThan( hungerThresh ),
			AverageHappiness: Peep.AverageHappiness,
			ResearchAvailable: Entity.All.OfType<Ride>().Any( r => r.NextResearched ),
			GoldenTicketWon: ticketJustWon,
			ChallengeWon: challengeJustWon,
			ChallengeLost: challengeJustLost,
			TicketSatisfaction: EntryFeeOpinion.ArrivalHappinessNow(),
			FeeAboveMinimum: (fin?.EntryFee ?? 0f) > EntryFeeOpinion.Settings.MinimumEntryFee,
			PriceComplaints: complaints,
			PriceBargains: bargains,
			Facilities: Facilities() );
	}

	// The shop and sideshow settings visitors complain about (the windows' sliders pushed too far).
	private const float PoorQuality = 30f, TooMuch = 80f, TooLittle = 15f;
	private const int CheapPrize = 10, SlimChance = 85; // a prize under a third of the usual 30; under 15 % to win
	private static IEnumerable<Shop> Selling() => Shop.Stalls.Where( s => !s.IsFree && !s.IsClosed );
	private static IEnumerable<Shop> Dosing( int ingredient ) => Selling().Where( s => s.Stats?.Ingredient == ingredient );

	// What the park has built and hired, for the staffing / building advice.
	private ParkFacilities Facilities()
	{
		var staff = Entity.All.OfType<Staff>().ToList();
		int Hired( StaffRole r ) => staff.Count( s => s.Role == r );
		int Unzoned( StaffRole r ) => staff.All( s => s.Role != r || !s.HasPatrolZone ) ? Hired( r ) : 0; // none zoned at all
		var rides = Entity.All.OfType<Ride>().Where( r => !r.IsFeature ).ToList();
		var recent = ParkFinances.Current?.RecentWagesAndIncome( (int)(config.Param( "WagesHigh", "AverageOverMonths" ) ?? 3f) ) ?? (0f, 0f);
		return new ParkFacilities(
			Mechanics: Hired( StaffRole.Mechanic ), Handymen: Hired( StaffRole.Handyman ),
			Guards: Hired( StaffRole.Guard ), Entertainers: Hired( StaffRole.Entertainer ),
			Rides: rides.Count( r => !r.IsSideshow ),
			WornRides: rides.Count( r => !r.IsBroken && r.Reliability < 0.25f ), // StaffHireMechanics1.PoorerStateThan 25
			BrokenRides: rides.Count( r => r.IsBroken ),
			Shops: Shop.Stalls.Count( s => !s.IsFree ),
			Sideshows: rides.Count( r => r.IsSideshow ),
			Toilets: Shop.Stalls.Count( s => s.Kind == ShopKind.Toilet ),
			Litter: Litter.Active.Count,
			Visitors: Entity.All.Count( e => e is Peep ),
			NeedingToilet: Peep.CountNeedingToilet(),
			Misbehaving: Peep.CountUnhappy(),
			Days: GameClock.Current?.TotalDays ?? 0,
			WaitingAtGate: Level.WaitingAtGate,
			Researchers: Hired( StaffRole.Researcher ),
			RecentWages: recent.Wages, RecentIncome: recent.Income,
			AnyUpgradeResearched: rides.Any( r => r.ResearchedLevel > 0 || r.UpgradeLevel > 0 ),
			Money: ParkFinances.Current?.Money ?? 0f,
			Debt: ParkFinances.Current?.Debt ?? 0f,
			WorstToiletFilth: Shop.Stalls.Where( s => s.Kind == ShopKind.Toilet ).Select( s => s.Filth ).DefaultIfEmpty( 0f ).Max(),
			AverageGrades: StaffStrikes.Roles.Select( r => staff.Where( s => s.Role == r ).Select( s => (float)s.Grade ).DefaultIfEmpty( float.NaN ).Average() ).ToArray(),
			StaffNotices: PendingStaffNotices(),
			Features: Ride.Features.Count, Cameras: Ride.Features.Count( r => r.ProvidesSecurity ),
			ToiletQueue: Shop.AverageToiletQueue(),
			FullQueues: rides.Count( r => r.Queue is { IsFull: true } ),
			PoorFood: Selling().Count( s => s.Kind is ShopKind.Food or ShopKind.Drink && s.Quality < PoorQuality ),
			PoorGoods: Selling().Count( s => s.Kind == ShopKind.Item && s.Quality < PoorQuality ),
			Fatty: Dosing( ShopBuilding.Fat ).Count( s => s.IngredientAmount > TooMuch ),
			Salty: Dosing( ShopBuilding.Salt ).Count( s => s.IngredientAmount > TooMuch ),
			Icy: Dosing( ShopBuilding.Ice ).Count( s => s.IngredientAmount > TooMuch ),
			TooSweet: Dosing( ShopBuilding.Sugar ).Count( s => s.IngredientAmount > TooMuch ),
			NotSweet: Dosing( ShopBuilding.Sugar ).Count( s => s.IngredientAmount < TooLittle ),
			CheapPrizes: rides.Count( r => r.IsSideshow && r.SideshowCostOfGoods < CheapPrize ),
			SlimChances: rides.Count( r => r.IsSideshow && r.SideshowChanceOfLoosing > SlimChance ),
			UnzonedMechanics: Unzoned( StaffRole.Mechanic ), UnzonedHandymen: Unzoned( StaffRole.Handyman ),
			UnzonedGuards: Unzoned( StaffRole.Guard ), UnzonedEntertainers: Unzoned( StaffRole.Entertainer ),
			ShopKindsAvailable: Kinds( BuildCategory.Shops, available: true ), ShopKindsBuilt: Kinds( BuildCategory.Shops, available: false ),
			SideshowKindsAvailable: Kinds( BuildCategory.Sideshows, available: true ), SideshowKindsBuilt: Kinds( BuildCategory.Sideshows, available: false ),
			RideKindsAvailable: RideKinds( available: true ), RideKindsBuilt: RideKinds( available: false ),
			RidesToResearch: BuildMode.Current?.Catalog.Count( c => c.Category == BuildCategory.Rides && !(ItemResearch.Current?.IsUnlocked( c.Name ) ?? true) ) ?? 0 );
	}

	// The researched kinds of ride in the catalog (available) or those of them the park has built.
	// The kinds of stall or sideshow on offer (researched), or how many of them the park has built.
	private static int Kinds( BuildCategory category, bool available )
	{
		var kinds = BuildMode.Current?.Catalog.Where( c => c.Category == category && (ItemResearch.Current?.IsUnlocked( c.Name ) ?? true) ).ToList();
		if ( kinds == null )
			return -1;
		if ( available )
			return kinds.Count;
		var built = Entity.All.OfType<Ride>().Select( r => System.IO.Path.GetFileName( r.Archive ) )
			.Concat( Shop.Stalls.Select( s => s.CatalogKey ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		return kinds.Count( c => built.Contains( c.Name ) || (c.RidePath != null && built.Contains( System.IO.Path.GetFileName( c.RidePath ) )) );
	}

	private static int RideKinds( bool available )
	{
		var rides = BuildMode.Current?.Catalog.Where( c => c.Category == BuildCategory.Rides && (ItemResearch.Current?.IsUnlocked( c.Name ) ?? true) ).ToList();
		if ( rides == null )
			return -1;
		if ( available )
			return rides.Count;
		var built = Entity.All.OfType<Ride>().Select( r => r.Archive ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		return rides.Count( c => c.RidePath != null && built.Contains( c.RidePath ) );
	}

	// One-off announcements raised by the game (a new ride researched…), waiting for the next snapshot.
	private static readonly List<string> announcements = new();

	/// <summary>Have the advisor announce <paramref name="messageId"/> (an Advisor.sam message) about
	/// <paramref name="name"/>, with the game's own sentence (TAG_SYSTEM 114 "Your scientists have researched the …",
	/// 118 "You've researched a new feature: …").</summary>
	public static void Announce( string messageId, string name )
	{
		int? tag = AdvisorTips.TextIndexOf( messageId );
		var opening = tag is { } t && AdvisorTips.GameMessageLines( t ) is { Length: > 0 } lines ? lines[0] : "You've researched";
		AdvisorTips.Raised[messageId] = opening.EndsWith( ':' ) || opening.EndsWith( "the" ) ? $"{opening} {name}." : $"{opening}: {name}.";
		lock ( announcements )
			announcements.Add( messageId );
	}

	/// <summary>Have the advisor say <paramref name="messageId"/> once, as it stands (the park opening or closing).</summary>
	public static void Notice( string messageId )
	{
		lock ( announcements )
			announcements.Add( messageId );
	}

	// Strike announcements wait up to StaffNoticeLife seconds for the advisor to have a free moment.
	private readonly Dictionary<string, float> staffNotices = new();
	private const float StaffNoticeLife = 30f;

	private IReadOnlyCollection<string> PendingStaffNotices()
	{
		if ( StaffStrikes.Current is { } strikes )
			foreach ( var id in strikes.TakeNotices() )
				staffNotices[id] = Time.Now + StaffNoticeLife;
		lock ( announcements )
		{
			foreach ( var id in announcements )
				staffNotices[id] = Time.Now + StaffNoticeLife;
			announcements.Clear();
		}
		foreach ( var id in staffNotices.Where( kv => kv.Value < Time.Now || activeMessage == kv.Key ).Select( kv => kv.Key ).ToList() )
			staffNotices.Remove( id );
		return staffNotices.Keys.ToList();
	}

	protected override void OnUpdate()
	{
		if ( parts.Count == 0 )
			return;

		if ( anchorPending )
		{
			anchorPending = false;
			startTime = Time.Now;
			if ( gesture != null )
				gestureStart = Time.Now;
		}

		// Let the message scheduler decide if/which tip fires (governed by the real Advisor.sam rules), then
		// advance the active clip's lip-sync.
		ConsiderSpeaking();
		CurrentShape = MouthShape.Closed;
		if ( lip != null && startTime >= 0f )
		{
			var elapsed = Time.Now - startTime;
			if ( elapsed <= ClipLength )
				CurrentShape = lip.ShapeAt( TimeSpan.FromSeconds( elapsed ) );
		}

		// Anchor the head at its measured screen spot, a fixed distance in front of the camera, and face the player
		// squarely — not just turned about world Z, which left him tilted by the park camera's pitch.
		if ( !ScreenSpace.TryAnchor( 1280f - HeadFromRight, HeadUp, Distance, out var anchor, out float pixel ) )
			return;
		var groupPos = (Vector3)anchor;
		float groupScale = HeadPixels * pixel / headUnits;

		// After the loader's Y/Z swap the model stands along +Y and looks along +Z (the eyes): map +Y to the
		// screen's up and +Z back at the camera.
		var (right, up, back) = ScreenSpace.Axes();
		var basis = new Matrix4x4(
			right.X, right.Y, right.Z, 0f,
			up.X, up.Y, up.Z, 0f,
			back.X, back.Y, back.Z, 0f,
			0f, 0f, 0f, 1f );
		// Leaning back a little, as in the original, so the face shows under the hat's brim.
		var groupRot = Quaternion.CreateFromRotationMatrix( basis )
			* Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitX, -LeanBack );

		// The gesture playing, if any (one now and then between tips, when he isn't speaking).
		if ( gesture != null && Time.Now - gestureStart > gesture.Seconds )
			gesture = null;
		bool speakingNow = startTime >= 0f && Time.Now - startTime <= ClipLength;
		if ( gesture == null && (!speakingNow || Environment.GetEnvironmentVariable( "OPENTPW_ADVISOR_GESTURE" ) != null) && Time.Now >= nextIdleGesture )
		{
			StartGesture( IdleGestureLongest );
			nextIdleGesture = Environment.GetEnvironmentVariable( "OPENTPW_ADVISOR_GESTURE" ) != null ? Time.Now
				: Time.Now + IdleGestureMin + (float)Random.Shared.NextDouble() * (IdleGestureMax - IdleGestureMin);
		}
		float frame = gesture == null ? 0f : (Time.Now - gestureStart) * GestureFile.FramesPerSecond;
		var pose = Pose( frame );

		// He steps out of view while the player rides a ride (the scenery is close and would cut through him).
		bool hidden = RideItCameraMode.Target != null || CamcorderCameraMode.Active;
		foreach ( var part in parts )
		{
			// Only the active viseme's mouth is visible; the gesture shows or hides the rest (a blink, the ticket).
			if ( hidden )
				part.Entity.Model = null;
			else if ( part.Viseme is { } v )
				part.Entity.Model = v == (CurrentShape == MouthShape.Closed ? MouthShape.Normal : CurrentShape) ? part.Model : null; // at rest he smiles
			else
				part.Entity.Model = (TrackOf( part.Mesh )?.VisibleAt( frame ) ?? part.AlwaysShown) ? part.Model : null;

			Morph( part, frame );
			var (localOffset, localRotation, localScale) = pose?[part.Mesh] ?? (part.LocalOffset, part.LocalRotation, part.LocalScale);
			var offset = System.Numerics.Vector3.Transform( (localOffset * groupScale).GetSystemVector3(), groupRot );
			part.Entity.Position = groupPos + (Vector3)offset;
			part.Entity.Rotation = groupRot * localRotation;
			part.Entity.Scale = localScale * groupScale;
		}
	}

	// Start one of his gestures, at random among those no longer than `longest` seconds (else the shortest): one that
	// brings out `prop` when it's given (the golden ticket's), else one with no prop.
	private void StartGesture( float longest, string? prop = null )
	{
		if ( gestures.Count == 0 || tree == null )
			return;
		// Dev aid: OPENTPW_ADVISOR_GESTURE=n plays gesture n (1-based) over and over.
		if ( int.TryParse( Environment.GetEnvironmentVariable( "OPENTPW_ADVISOR_GESTURE" ), out int forced ) && forced >= 1 && forced <= gestures.Count )
		{
			gesture = gestures[forced - 1];
			gestureStart = Time.Now;
			return;
		}
		var kind = gestures.Where( g => gestureProp.GetValueOrDefault( g ) == prop ).ToList();
		if ( kind.Count == 0 )
			kind = gestures.Where( g => !gestureProp.ContainsKey( g ) ).ToList();
		if ( kind.Count == 0 )
			return;
		var fitting = kind.Where( g => g.Seconds <= longest ).ToList();
		gesture = fitting.Count > 0 ? fitting[Random.Shared.Next( fitting.Count )] : kind.MinBy( g => g.Seconds );
		gestureStart = Time.Now;
	}

	// A gesture's vertex morph on a part (the gloves' finger poses): its rest shape with the morphed positions written
	// over it (the loader's Y/Z swap applied); back to rest once the gesture is over.
	private void Morph( Part part, float frame )
	{
		var morphs = TrackOf( part.Mesh )?.Morph;
		if ( (morphs == null || morphs.Count == 0) && !part.Morphed )
			return;
		var vertices = part.Model.Vertices;
		for ( int i = 0; i < vertices.Length && i < part.Rest.Length; i++ )
			vertices[i].Position = part.Rest[i];
		part.Morphed = false;
		foreach ( var m in morphs ?? new() )
			for ( int i = 0; i < m.VertexSlots.Length; i++ )
			{
				if ( m.VertexSlots[i] >= part.VerticesOf.Length )
					continue;
				var p = m.Sample( i, frame );
				foreach ( int j in part.VerticesOf[m.VertexSlots[i]] )
					vertices[j].Position = new Vector3( p.X, p.Z, p.Y );
				part.Morphed = true;
			}
		part.Model.UploadVertices();
	}

	/// <summary>Bring out a prop with its gesture ("map" when the map window opens), unless he is busy gesturing.</summary>
	public static void ShowProp( string prop )
	{
		if ( Current is { } advisor && advisor.gesture == null )
			advisor.StartGesture( float.MaxValue, prop );
	}

	private GestureFile.Track? TrackOf( int index ) => gesture?.Tracks.FirstOrDefault( t => t.Target == index );

	// Every mesh's placement for the gesture at `frame`, in the parts' terms (the loader's Y/Z swap): each object's
	// local matrix — the gesture's rotation and scale over its bind translation, or its bind pose — times its
	// parents' up the tree, the root's own turn left out (the Y/Z swap stands in for it); at rest, the bind pose.
	// Null without a tree: then each mesh keeps its own placement.
	private (Vector3 Offset, Quaternion Rotation, Vector3 Scale)[]? Pose( float frame )
	{
		if ( tree == null )
			return null;
		var world = new Matrix4x4?[tree.ObjectCount];
		Matrix4x4 World( int i )
		{
			if ( world[i] is { } done )
				return done;
			int parent = tree.Parent[i];
			var m = parent < 0 ? Matrix4x4.Identity : LocalAt( i, frame ) * World( parent );
			world[i] = m;
			return m;
		}
		var pose = new (Vector3, Quaternion, Vector3)[tree.MeshCount];
		for ( int i = 0; i < tree.MeshCount; i++ )
		{
			Matrix4x4.Decompose( World( i ), out var scl, out var rot, out var pos );
			pose[i] = (new Vector3( pos.X, pos.Z, pos.Y ), new Quaternion( rot.X, rot.Z, rot.Y, -rot.W ), new Vector3( scl.X, scl.Z, scl.Y ));
		}
		return pose;
	}

	private Matrix4x4 LocalAt( int i, float frame )
	{
		var bind = tree!.Local[i];
		if ( TrackOf( i ) is not { } track || (track.Rotation.Count == 0 && track.Scale.Count == 0) )
			return bind;
		Matrix4x4.Decompose( bind, out var scl, out var rot, out var pos );
		if ( track.Rotation.Count > 0 )
			rot = GestureFile.Sample( track.Rotation, frame );
		if ( track.Scale.Count > 0 )
			scl = GestureFile.Sample( track.Scale, frame );
		return Matrix4x4.CreateScale( scl ) * Matrix4x4.CreateFromQuaternion( rot ) * Matrix4x4.CreateTranslation( pos );
	}

	protected override void OnDelete()
	{
		foreach ( var part in parts )
			Entity.All.Remove( part.Entity );
		parts.Clear();
		if ( Current == this )
			Current = null;
	}
}
