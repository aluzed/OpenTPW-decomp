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
/// </summary>
public sealed class Advisor : Entity
{
	public static Advisor? Current { get; private set; }

	// Base parts always shown (the bug face: head + eyes + antennae); everything else (the body — which hangs
	// off-axis and just clutters a corner overlay — hats, hands, spatula, shut-eyes, inactive mouths) is
	// hidden so the talking head reads cleanly.
	private static readonly string[] BaseParts =
	{
		"bug head", "right eye", "left eye", "right antennae", "left antennae",
	};

	private sealed class Part
	{
		public ModelEntity Entity = null!;
		public Model Model = null!;
		public Vector3 LocalOffset;
		public Quaternion LocalRotation;
		public Vector3 LocalScale;
		public bool AlwaysShown;
		public MouthShape? Viseme; // set for the five "Mouth - *" parts
	}

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

	// On-screen anchoring: the bug head sits as a small bottom-right corner overlay, a fixed distance in
	// front of the camera and offset right + down, scaled down to ~⅓ screen height (tuned on real jungle
	// assets — see T-046's visual pass). The body part is hidden (BaseParts) so the talking head reads clean.
	private const float Distance = 26f, RightOffset = 10.5f, UpOffset = -5.2f, GroupScale = 0.15f;

	/// <summary>The viseme currently shown (for the HUD label); Closed when not speaking.</summary>
	public MouthShape CurrentShape { get; private set; } = MouthShape.Closed;
	public string ClipName => clipName;
	/// <summary>The id of the advice currently being spoken (for the HUD tip text); empty when idle.</summary>
	public string ActiveMessage => activeMessage;
	public float Elapsed => startTime < 0f ? 0f : Time.Now - startTime;
	public float ClipLength => (float)(lip?.Duration.TotalSeconds ?? 0);

	public Advisor()
	{
		Current = this;
		Name = "Advisor";
		BuildParts();

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
			var wadPath = Path.Join( GameDir.GamePath, "data", "global", "advisor.wad" );
			var wad = new WadArchive( wadPath );
			model = new ModelFile( new MemoryStream( wad.GetFile( "Advisor.MD2" ).GetData() ) );
			Log.Info( $"[advisor] Advisor.MD2 loaded: {model.Meshes.Count} mesh(es)" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[advisor] model load failed: {e.Message}" );
			return;
		}

		foreach ( var mesh in model.Meshes )
		{
			var name = (mesh.Name ?? "").Replace( "\0", "" ).Trim();
			var viseme = VisemeForMesh( name );
			var isBase = Array.Exists( BaseParts, p => string.Equals( p, name, StringComparison.OrdinalIgnoreCase ) );
			if ( viseme == null && !isBase )
				continue; // skip hats/hands/spatula/shut-eyes

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
				AlwaysShown = isBase,
				Viseme = viseme,
			} );
		}
		Log.Info( $"[advisor] built {parts.Count} part(s) from Advisor.MD2 (5 visemes + bug face)" );
	}

	private static MouthShape? VisemeForMesh( string name )
	{
		foreach ( MouthShape s in new[] { MouthShape.Normal, MouthShape.Aah, MouthShape.Eee, MouthShape.Ooh, MouthShape.Sss } )
			if ( string.Equals( name, s.MeshPartName(), StringComparison.OrdinalIgnoreCase ) )
				return s;
		return null;
	}

	// Builds a renderable Model from one MD2 mesh (the Ride.BuildMeshEntities texture/vertex pattern).
	private static Model? BuildMesh( ModelFile.Mesh mesh )
	{
		try
		{
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			var textures = new List<Texture>();
			for ( int i = 0; i < 16; ++i )
			{
				if ( mesh.Materials.Length <= i ) { textures.Add( Texture.Missing ); continue; }
				try { textures.Add( new Texture( $"global/advisor/textures/{mesh.Materials[i].Name}.wct", TextureFlags.Repeat ) ); }
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

	// Speak the message with id `messageId`: load its lip-sync + clip and play it. The id→clip mapping is by
	// convention (Advisor/<id>); we don't ship per-message advisor clips, so it falls back to the demo clip
	// sp_001, which still drives a correct viseme track. Returns true when audio actually started.
	private bool Speak( string messageId )
	{
		try
		{
			var theme = Level.Current?.Name ?? "jungle"; // advisor speech is per-theme (T-062)
			// The level's speech (the language overlay's voice when there is one — GameDir.DataFile).
			var sdtPath = GameDir.DataFile( "levels", theme, "Speech", "speechHD.SDT" );
			var speechDir = Path.GetDirectoryName( sdtPath )!;
			if ( !File.Exists( sdtPath ) )
				return false;

			// Per-message clip if it exists, else the shared demo clip (always present in the jungle level).
			var archive = new SdtArchive( sdtPath );
			var clip = archive.soundFiles.FirstOrDefault( f => f.Name.Equals( messageId, StringComparison.OrdinalIgnoreCase ) )
				?? archive.soundFiles.FirstOrDefault( f => f.Name.StartsWith( "sp_001", StringComparison.OrdinalIgnoreCase ) );
			if ( clip == null )
				return false;

			var lipPath = Path.Join( speechDir, "lips", $"{Path.GetFileNameWithoutExtension( clip.Name )}.LIP" );
			if ( !File.Exists( lipPath ) )
				lipPath = Path.Join( speechDir, "lips", "sp_001.LIP" );
			if ( !File.Exists( lipPath ) )
				return false;

			lip = new LipSyncFile( File.OpenRead( lipPath ) );
			clipName = clip.Name;
			activeMessage = messageId;
			Audio.PlaySpeech( $"advisor_{clip.Name}", clip.SoundData );
			startTime = Time.Now;
			Log.Info( $"[advisor] message '{messageId}' → speaking '{clipName}', {lip.Keyframes.Count} keyframes, {ClipLength:0.0}s" );
			return true;
		}
		catch ( Exception e ) { Log.Warning( $"[advisor] speech failed: {e.Message}" ); return false; }
	}

	// When the advisor is idle, ask the scheduler whether a tip should fire now. The Advisor.sam pacing/group
	// rules (min-gap, same-message gap, say-once, min-score) decide which — if any — message speaks.
	private void ConsiderSpeaking()
	{
		bool speaking = startTime >= 0f && Time.Now - startTime <= ClipLength;
		if ( speaking )
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

	// What the park has built and hired, for the staffing / building advice.
	private ParkFacilities Facilities()
	{
		var staff = Entity.All.OfType<Staff>().ToList();
		int Hired( StaffRole r ) => staff.Count( s => s.Role == r );
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
			RideKindsAvailable: RideKinds( available: true ), RideKindsBuilt: RideKinds( available: false ),
			RidesToResearch: BuildMode.Current?.Catalog.Count( c => c.Category == BuildCategory.Rides && !(ItemResearch.Current?.IsUnlocked( c.Name ) ?? true) ) ?? 0 );
	}

	// The researched kinds of ride in the catalog (available) or those of them the park has built.
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
		int? tag = messageId switch { "ResearchRideResearched" => 114, "ResearchFeatureResearched" => 118, _ => null };
		var opening = tag is { } t && AdvisorTips.GameMessageLines( t ) is { Length: > 0 } lines ? lines[0] : "You've researched";
		AdvisorTips.Raised[messageId] = opening.EndsWith( ':' ) || opening.EndsWith( "the" ) ? $"{opening} {name}." : $"{opening}: {name}.";
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

		// Anchor the whole assembly upright, in front of and facing the camera.
		var cam = Camera.Position;
		var fwd = Camera.Rotation.Forward;
		var right = Camera.Rotation.Right;
		var up = Camera.Rotation.Up;
		var groupPos = cam + fwd * Distance + right * RightOffset + up * UpOffset;

		// Face the camera horizontally, staying upright (world Z up). +MathF.PI aligns the model's front.
		var toCam = cam - groupPos;
		var yaw = MathF.Atan2( toCam.Y, toCam.X ) + MathF.PI / 2f;
		var groupRot = Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );

		foreach ( var part in parts )
		{
			// Only the active viseme's mouth is visible.
			if ( part.Viseme is { } v )
				part.Entity.Model = v == CurrentShape ? part.Model : null;
			else if ( part.AlwaysShown )
				part.Entity.Model = part.Model;

			var offset = System.Numerics.Vector3.Transform( (part.LocalOffset * GroupScale).GetSystemVector3(), groupRot );
			part.Entity.Position = groupPos + (Vector3)offset;
			part.Entity.Rotation = groupRot * part.LocalRotation;
			part.Entity.Scale = part.LocalScale * GroupScale;
		}
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
