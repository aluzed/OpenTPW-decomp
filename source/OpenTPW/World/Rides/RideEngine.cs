using System.Numerics;
namespace OpenTPW;

/// <summary>
/// Concrete ride engine for one running <see cref="Ride"/>. Owns the registry of objects a ride's
/// RSE script spawns/animates. Object registry, sound (via <see cref="Audio"/>), object lifecycle,
/// and animation. Animation is now <b>channel-aware</b>: the owning Ride discovers the ride's real
/// animation channels from its WAD (see <see cref="SetAnimChannels"/> and docs/08-ghidra-animation.md)
/// so the engine animates only channels the ride actually ships and scales each by its keyframe
/// count. The visible motion is still a procedural placeholder bob — decoding the per-frame vertex
/// payload to morph the real keyframes is T-033. The ride body is registered as a self object (id
/// <see cref="SelfId"/>) and plays its looping motion (Main, else Idle) so the model is visibly alive.
/// See docs/tickets/T-032 / T-033 / T-007.
/// </summary>
public sealed class RideEngine : IRideEngine
{
	private const int SelfId = -1;     // the ride body's handle (script object ids are >= 0)
	private const float FrameSeconds = 0.1f; // placeholder playback rate (~10 fps) per keyframe
	private const float BobAmplitude = 12f;
	// Keyframe time units advanced per second. The original computes animTime = elapsed_ms * 0.03 * speed
	// (FUN_004735d0; _DAT_006fec0c = 0.03 ≈ 1/33.33, the 30 FPS frame time), i.e. keyframe times are in
	// 30 FPS frames — so the clock advances 30 units/second. (RE'd, see docs/08-ghidra-animation.md.)
	private const float KeyframeRate = 30f;

	private readonly Dictionary<int, RideObject> objects = new();
	private SdtArchive? rideSounds;
	private SdtArchive? peepSounds;      // KidsHD.sdt — peep voices (screams live here, not RideHD)
	private int[]? screamIndices;        // indices of the scream/yell samples in peepSounds

	// Rider scream (STARTSCREAM/STOPSCREAM/SINGLESCREAM/SCREAMLEVEL — used by e.g. monkey). A sustained
	// scream re-plays the scream sound every ScreamPeriod while active; level (0..100) scales volume.
	private const float ScreamPeriod = 1.8f;
	private bool screaming;
	private int screamCode;
	private float screamGain = 0.8f;        // 0..1, from the script's level
	private float lastScreamAt = float.NegativeInfinity;

	// The ride's real animation channels, discovered from the WAD: anim id -> keyframe count. Empty
	// until the owning Ride calls SetAnimChannels. A ScriptDefs.Animations value present here means
	// the ride actually ships that channel's keyframe files (see docs/08-ghidra-animation.md, T-033).
	private IReadOnlyDictionary<int, int> channelFrames = new Dictionary<int, int>();

	/// <summary>
	/// The single-letter animation channel code for a <see cref="ScriptDefs.Animations"/> value, as the
	/// original game derives it: the first letter of the animation name (Create→'c', Idle→'i',
	/// Main→'m', …). Ride keyframe files are named <c>&lt;base&gt;&lt;letter&gt;[&lt;frame#&gt;].md2</c>.
	/// </summary>
	public static char ChannelLetter( ScriptDefs.Animations anim )
	{
		const string prefix = "ANIM_";
		var name = anim.ToString();
		return name.StartsWith( prefix ) && name.Length > prefix.Length
			? char.ToLowerInvariant( name[prefix.Length] )
			: '\0';
	}

	/// <summary>
	/// Supplies the ride's real animation channels (anim id -> keyframe count), discovered by probing
	/// the WAD for <c>&lt;base&gt;&lt;letter&gt;[&lt;n&gt;].md2</c> files. Lets the engine animate only
	/// channels the ride actually has, and scale a channel's duration by its keyframe count.
	/// </summary>
	public void SetAnimChannels( IReadOnlyDictionary<int, int> channels )
	{
		channelFrames = channels;
		Log.Info( $"[ride] animation channels: " + ( channels.Count == 0
			? "none"
			: string.Join( ", ", channels.OrderBy( kv => kv.Key )
				.Select( kv => $"{(ScriptDefs.Animations)kv.Key}({ChannelLetter( (ScriptDefs.Animations)kv.Key )}×{kv.Value})" ) ) ) );
	}

	// Frame count for an animation channel (0 if the ride doesn't ship it), and the playback duration.
	// A channel with decoded keyframes runs for its real track length (Duration is in keyframe time
	// units; the clock advances at KeyframeRate per second), so a one-shot (Load/Start/End) plays in
	// full before the cycle advances. Channels with no decoded keyframes fall back to a per-frame
	// placeholder length.
	private int FrameCount( int anim ) => channelFrames.TryGetValue( anim, out var n ) ? n : 0;
	private float DurationOf( int anim ) =>
		channelAnims.TryGetValue( anim, out var kf ) && kf.Duration > 0
			? kf.Duration / KeyframeRate
			: Math.Max( FrameCount( anim ), 1 ) * FrameSeconds;

	// Registers the ride's loaded meshes as the body, and starts its looping motion so it visibly
	// moves. The looping channel is Main (the original's primary ride motion) when the ride ships it,
	// otherwise Idle — see docs/08-ghidra-animation.md.
	public void RegisterBody( IEnumerable<ModelEntity> parts )
	{
		var body = new RideObject { Id = SelfId, Type = -1 };
		int globalOffset = 0;
		foreach ( var e in parts )
		{
			body.Parts.Add( (e, e.Position, e.Rotation, e.Scale) );

			// Capture the rest-pose vertex positions + this part's global vertex start, so vertex-morph
			// (which addresses one combined buffer across all parts) can map its global slots to parts.
			var verts = e.Model?.Vertices ?? System.Array.Empty<Vertex>();
			var basePos = new Vector3[verts.Length];
			for ( int i = 0; i < verts.Length; i++ )
				basePos[i] = verts[i].Position;
			body.PartBaseVerts.Add( basePos );
			body.PartVertexOffset.Add( globalOffset );
			globalOffset += verts.Length;
		}

		objects[SelfId] = body;

		var loopAnim = FrameCount( (int)ScriptDefs.Animations.ANIM_Main ) > 0
			? ScriptDefs.Animations.ANIM_Main
			: ScriptDefs.Animations.ANIM_Idle;
		StartAnim( body, (int)loopAnim, loop: true );
	}

	public void SpawnObject( int type, int node, int code, int id )
	{
		// FUN_005573d0: types 1–2 are a positioned sound (the same global ride-sound id an EVENT plays; 0x8000 marks a
		// ride-local id, whose remap isn't reversed — skipped), types 3–10 a particle effect from one of the engine's
		// effect pools (FUN_0051bfc0 with DAT_00803a3c / 38 / 28 / 24…). Those pools aren't Tp2.plb — read as Tp2 the
		// shipped scripts' codes give demolition clouds and message tags on loop — so particle objects are registered
		// but not drawn until the pools are reversed (T-037). (The old "explosions" came from reading op 2, the node —
		// −1 for "the ride" — as the sound.)
		var obj = new RideObject { Id = id, Type = type, Node = node, Code = code };
		objects[id] = obj;
		if ( ClassifyEvent( type ) == EventKind.Sound && code >= 0 && (code & 0x8000) == 0 )
			PlayEventSound( type, code, node );
		Log.Trace( $"[ride] ADDOBJ type={type} node={node} code={code} id={id}" );
	}


	public void PlaySound( string name )
	{
		// A ".rse" name is a sound-event-map script (e.g. EventMap.rse) — a child script that sets up the
		// ride's EVENT→sound bindings. The EVENT effects engine that consumes those bindings is deferred
		// (T-032), so we recognise it without playing a clip (playing RideHD[offset] here was the old
		// wrong-sound bug). A plain sound name plays from the ride's banks by name.
		if ( name.EndsWith( ".rse", StringComparison.OrdinalIgnoreCase ) )
		{
			Log.Info( $"[ride] SPAWNSOUND sound-event map '{name}' (EVENT bindings deferred — T-032)" );
			return;
		}
		PlayNamedSound( name );
	}

	// Play a sound by file name from the ride's sound bank (RideHD). Best-effort: the catalog (T-016)
	// lists the category's banks; for now we resolve names against the ride bank.
	private void PlayNamedSound( string name )
	{
		try
		{
			var path = Path.Join( GameDir.GamePath, "data", "global", "sound", "RideHD.sdt" );
			if ( !File.Exists( path ) )
				return;
			rideSounds ??= new SdtArchive( path );
			var stem = Path.GetFileNameWithoutExtension( name );
			var track = rideSounds.soundFiles.FirstOrDefault( f =>
				f.Name.StartsWith( stem, StringComparison.OrdinalIgnoreCase ) );
			if ( track == null )
			{
				Log.Warning( $"[ride] SPAWNSOUND '{name}' not found in RideHD" );
				return;
			}
			Audio.PlaySfx3D( $"ride_{track.Name}", track.SoundData, LightPosition( SelfId ) ); // ride-positioned (T-047)
			Log.Info( $"[ride] SPAWNSOUND '{name}' -> {track.Name}" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ride] SPAWNSOUND '{name}' failed: {e.Message}" );
		}
	}

	public void KillObject( int id )
	{
		if ( !objects.Remove( id, out var obj ) )
			return;

		// Despawn its visual parts (the Entity list isn't otherwise pruned on delete).
		foreach ( var (entity, _, _, _) in obj.Parts )
			Entity.All.Remove( entity );
	}

	/// <summary>Tear down everything this engine spawned — the ride body + all script objects, plus the
	/// light and particle-effect proxies — when the ride is sold/demolished (T-041).</summary>
	public void Despawn()
	{
		foreach ( var obj in objects.Values )
			foreach ( var (entity, _, _, _) in obj.Parts )
				Entity.All.Remove( entity );
		objects.Clear();

		foreach ( var light in lights.Values )
			if ( light.Proxy != null )
				Entity.All.Remove( light.Proxy );
		lights.Clear();

		particles.Clear();

		foreach ( var (_, marker) in headProxies.Values )
			Entity.All.Remove( marker.Entity );
		headProxies.Clear();

		foreach ( var marker in walkProxies.Values )
			Entity.All.Remove( marker.Entity );
		walkProxies.Clear();
	}

	public void SetObjectParam( int id, int param, int value )
	{
		if ( objects.TryGetValue( id, out var obj ) )
			obj.Params[param] = value;
	}

	public void TriggerAnim( int id, int anim, bool loop )
	{
		var obj = Resolve( id );
		if ( obj == null )
			return;

		// Idempotent: re-triggering the same animation (e.g. each tick of a TRIGWAIT loop) must not
		// keep resetting its start time.
		if ( obj.AnimId == anim && obj.AnimLoop == loop )
			return;
		// The body builds itself once, when placed: a later Create from the script would show it half-built again.
		if ( obj.Id == SelfId && anim == (int)ScriptDefs.Animations.ANIM_Create )
		{
			if ( bodyBuilt )
				return;
			bodyBuilt = true;
		}

		StartAnim( obj, anim, loop );
		var frames = FrameCount( anim );
		Log.Trace( $"[ride] anim {(ScriptDefs.Animations)anim} ('{ChannelLetter( (ScriptDefs.Animations)anim )}', {frames} frame(s)) on {id} loop={loop}"
			+ ( frames == 0 ? " — ride has no art for this channel" : "" ) );
	}

	public void SetAnimSpeed( int id, float speed )
	{
		var obj = Resolve( id );
		if ( obj != null )
			obj.AnimSpeed = speed == 0 ? 1f : speed;
	}

	public void FlushAnims( int id )
	{
		if ( id < 0 )
		{
			foreach ( var obj in objects.Values )
				StopAnim( obj );
			return;
		}

		var o = Resolve( id );
		if ( o != null )
			StopAnim( o );
	}

	public int GetAnim( int id ) => Resolve( id )?.AnimId ?? 0;

	public bool IsAnimating( int id, int anim )
	{
		var obj = Resolve( id );
		// Looping anims (e.g. the idle/main loop) never "finish", so they never block a WAITANIM.
		return obj?.AnimId == anim && !obj.AnimLoop && Time.Now - obj.AnimStart < DurationOf( anim ) / obj.AnimSpeed;
	}

	public bool AnyAnimating()
	{
		foreach ( var obj in objects.Values )
			if ( obj.AnimId is int a && !obj.AnimLoop && Time.Now - obj.AnimStart < DurationOf( a ) / obj.AnimSpeed )
				return true;
		return false;
	}

	public void StartScream( int code, int level )
	{
		screaming = true;
		screamCode = code;
		if ( level >= 0 )
			screamGain = Math.Clamp( level / 100f, 0f, 1f );
		PlayScream( code, screamGain ); // begin immediately; Update sustains it
		Log.Info( $"[ride] STARTSCREAM code={code} level={level}" );
	}

	public void StopScream()
	{
		screaming = false;
		Log.Info( "[ride] STOPSCREAM" );
	}

	public void SingleScream( int code, int level )
	{
		float gain = level >= 0 ? Math.Clamp( level / 100f, 0f, 1f ) : screamGain;
		PlayScream( code, gain );
		Log.Info( $"[ride] SINGLESCREAM code={code} level={level}" );
	}

	public void SetScreamLevel( int level )
	{
		if ( level >= 0 )
			screamGain = Math.Clamp( level / 100f, 0f, 1f );
	}

	// Coaster control, multiplexed by subcommand (RE'd from coaster1.rse): 1 load rider · 2 can-load? ·
	// 3 peep-wants-off? · 4 set running/broken · 5 mode/closed · 6 capacity · 7 worn · 8 init. The query
	// subcommands' Zero-flag result is set in the opcode handler so the script's load/unload loops yield
	// rather than spin; real car loading + motion needs the coaster car/track engine (T-045) — deferred.
	// Silent: coaster1's loops call this thousands of times per second, so it must not log.
	public void Coast( int sub, int arg ) { }

	// Tour-ride / bumper-kart control, multiplexed by subcommand (RE'd from op_53 / op_54). Like COAST,
	// these drive an authored car-object subsystem (tour-route cars / bumper cars). We don't model that
	// subsystem, so — exactly as the original does when the subsystem isn't live (every helper bails on
	// the `0x4a454647` magic) — commands are no-ops here and the queries return 0 (set in the handler).
	// Our car rides still show visible motion via the occupancy-driven RideVehicle (T-032). Silent: these
	// fire in tight per-tick loops, so they must not log.
	public void Tour( int sub, int arg ) { }
	public void Bump( int sub, int arg ) { }

	private readonly Dictionary<int, float> lastEventAt = new();
	private const float EventDebounce = 0.5f; // ride loops re-fire EVENT each tick — don't restart too fast

	// Ride event dispatch. EVENT(type, target, code) — RE'd end to end (handler FUN_00552615 → dispatch
	// FUN_005573d0, shared with ADDOBJ). Every type is a positioned SOUND at node `target` (T-047, corrected
	// 2026-10-21): types 1 & 2 play a global ride-sound id (FUN_00521e60 / FUN_00521930), types 3-10 spawn from one
	// of the effect manager's pools (FUN_0051bfc0), and the pools are the sound categories FUN_0051eae0 /
	// FUN_0051ec50 register — 3 → the LEVEL's cat_rides (DAT_00803a3c), 4 → the level's cat_ambient (a38),
	// 5 → global rides, 6 → kids, 7 → staff, 8 → global ambient, 9 → ui, 10 → the ride's own custom bank (not
	// resolved yet, so silent). (Types 3-4 were read as Tp2.plb particles before: their codes — 73-75 the jungle
	// lion roars, 90 lava suck, 219/220 "out of range" — are entries of the level's 306-sample ride bank.) Sounds are
	// debounced so a per-tick ride loop doesn't restart the clip.
	/// <summary>The effect kind an EVENT / ADDOBJ type selects (FUN_005573d0): types 1-10 are positioned sounds,
	/// anything else is "RSSE: Unknown object type".</summary>
	public enum EventKind { Unknown, Sound }

	public static EventKind ClassifyEvent( int type ) => type is >= 1 and <= 10 ? EventKind.Sound : EventKind.Unknown;

	/// <summary>The sound category an EVENT / ADDOBJ type plays through: 1-2 & 5 → rides, 6 → kids, 7 → staff,
	/// 8 → ambient, 9 → ui, and the level's own banks for 3 (<c>level:rides</c>) and 4 (<c>level:ambient</c>). Null
	/// for the custom type 10 and non-sound types. Each maps to a <c>cat_*BANK.map</c>.</summary>
	public static string? EventSoundCategory( int type ) => type switch
	{
		1 or 2 or 5 => "rides",
		3 => "level:rides",
		4 => "level:ambient",
		6 => "kids",
		7 => "staff",
		8 => "ambient",
		9 => "ui",
		10 => "custom",
		_ => null
	};

	/// <summary>The ride's archive, for its own sound bank (EVENT / ADDOBJ type 10) — set by the ride.</summary>
	public string? Archive { get; set; }

	/// <summary>Resolves this ride's node ids to world positions (T-048/T-047) — set by <see cref="Ride"/>
	/// when the model loads. EVENT effects + REPAIREFFECT/SPARK spawn at the addressed node through it.</summary>
	public RideNodePositions? NodeField { get; set; }

	// World position of an addressed ride node (T-048): the node's resolved position (a live car/seat
	// position, or the footprint layout), else the ride body — so a script with no decoded node graph
	// behaves exactly as before (effect at the ride centre).
	private Vector3 NodePosition( int nodeId )
		=> NodeField != null && NodeField.TryResolve( nodeId, out var p ) ? p : LightPosition( SelfId );

	public void Event( int type, int p1, int p2 )
	{
		// p1 = target node id (where the effect/sound plays); p2 = code (a sound id or par_lib particle index).
		switch ( ClassifyEvent( type ) )
		{
			case EventKind.Sound:
				PlayEventSound( type, p2, p1 );
				break;
		}
	}

	private void PlayEventSound( int type, int code, int node )
	{
		var category = EventSoundCategory( type );
		if ( category == null )
			return;
		var track = (category == "custom" ? CustomBank() : CategoryBank( category ))?.Resolve( code );
		if ( track == null )
			return;
		if ( EventDebounced( $"snd:{category}:{code}" ) )
			return;
		// Play the category sound at the addressed node's world position (T-048 resolver) through the 3D
		// audio bus (T-047), so it pans + attenuates from the moving car/seat or footprint node.
		var pos = NodePosition( node );
		Audio.PlaySfx3D( $"ev_{track.Name}", track.SoundData, pos );
		Log.Trace( $"[ride] EVENT t{type} cat={category} code={code} node={node}@{pos} -> {track.Name}" );
	}

	// True if this event key fired within the debounce window (and refreshes it when it hasn't).
	private bool EventDebounced( string key )
	{
		var k = key.GetHashCode();
		if ( Time.Now - lastEventAt.GetValueOrDefault( k, float.NegativeInfinity ) < EventDebounce )
			return true;
		lastEventAt[k] = Time.Now;
		return false;
	}

	// Per-category sound registries (global sound ids → samples), one per EVENT sound category and built
	// once from that category's BANK catalog (cat_<category>BANK.map). Cached (incl. a null = "no catalog")
	// so a missing/headless install is only probed once. See EventSoundCategory + T-047.
	private static readonly Dictionary<string, RideSoundBank?> categoryBanks = new();
	// The ride's own bank (type 10), cached per archive like the categories.
	private RideSoundBank? CustomBank()
	{
		if ( Archive == null )
			return null;
		var key = $"custom:{Archive}";
		if ( !categoryBanks.TryGetValue( key, out var bank ) )
			categoryBanks[key] = bank = RideSoundBank.FromArchive( Archive );
		return bank;
	}

	private static RideSoundBank? CategoryBank( string category )
	{
		// A "level:" category lives with the park world's own sounds (levels/<theme>/Sound), the rest globally.
		bool level = category.StartsWith( "level:" );
		var theme = Level.Current?.Name ?? LevelTheme.Default;
		var key = level ? $"{theme}/{category}" : category;
		if ( !categoryBanks.TryGetValue( key, out var bank ) )
		{
			bank = level
				? RideSoundBank.FromBankCatalog( Path.Join( GameDir.GamePath, "data", "levels", theme, "Sound", $"cat_{category["level:".Length..]}BANK.map" ) )
				: RideSoundBank.FromBankCatalog( Path.Join( GameDir.GamePath, "data", "global", "sound", $"cat_{category}BANK.map" ) );
			categoryBanks[key] = bank;
		}
		return bank;
	}

	//
	// Particle effects (opcodes REPAIREFFECT/SPARK, RE'd from op_93/op_105 → the spawner FUN_0051bfc0): the ride's
	// bursts live in its own ParticleEmitter (the .PLB-driven proxy system, shared with build/demolish effects).
	//
	private readonly ParticleEmitter particles = new();

	// Particle effect at the ride centre (no addressed node) — the Repair() effect + any caller without a
	// node. Sits high over the ride body, as before.
	private const int EffectSparks = 1; // P_EFFECT_Sparks (par_lib.h order; the SPARK opcode's effect)

	/// <summary>Breakdown feedback (T-039): a burst of <c>P_EFFECT_Sparks</c> + an electrical zap, both at the
	/// ride body and positioned through the 3D bus (T-047), so a broken-down ride visibly + audibly sparks
	/// until a mechanic repairs it. Called periodically by the owning <see cref="Ride"/> while it's broken.</summary>
	public void PlayBreakdownEffect()
	{
		SpawnParticleEffect( EffectSparks );
		PlayRideHdSoundAt( "ZAP", LightPosition( SelfId ), gain: 0.8f );
	}

	// Play a RideHD clip whose name starts with `stem` at a world position through the 3D bus; best-effort
	// (no device / missing clip → silent). Shares the cached RideHD archive with PlayNamedSound.
	private void PlayRideHdSoundAt( string stem, Vector3 pos, float gain = -1f )
	{
		try
		{
			var path = Path.Join( GameDir.GamePath, "data", "global", "sound", "RideHD.sdt" );
			if ( !File.Exists( path ) )
				return;
			rideSounds ??= new SdtArchive( path );
			var track = rideSounds.soundFiles.FirstOrDefault( f => f.Name.StartsWith( stem, StringComparison.OrdinalIgnoreCase ) );
			if ( track != null )
				Audio.PlaySfx3D( $"ride_{track.Name}", track.SoundData, pos, gain );
		}
		catch ( Exception e ) { Log.Warning( $"[ride] sound '{stem}' failed: {e.Message}" ); }
	}

	public void SpawnParticleEffect( int effectCode )
		=> particles.Spawn( effectCode, LightPosition( SelfId ) + Vector3.Up * 8f );

	// Particle effect at a ride node's world position (REPAIREFFECT/SPARK + EVENT particles — T-048/T-047).
	// Resolves the node (a live car/seat position, or the footprint layout), lifting it slightly so the
	// proxy reads above the geometry; falls back to the ride centre when the node can't be placed.
	public void SpawnParticleEffect( int effectCode, int nodeId )
		=> particles.Spawn( effectCode, NodePosition( nodeId ) + Vector3.Up * 2f );

	//
	// Walk + head node visuals (T-048). The VM's WALKON/ADDHEAD families keep pure slot tables (RideVM.Walk
	// / RideVM.Heads); the *visual* placement was the gap. The engine mirrors those tables each frame from
	// the ride's node positions (RideNodePositions): a walk peep glides between its two walk nodes, a head
	// sits at its head node. The figures are **real peep sprites** (RideSpriteMarker, the same esprites.wad
	// kid art + directional walk cycle the crowd peeps use), driven by real node positions; a flat billboard
	// is the headless fallback. (Previously emissive cube stand-ins — the art swap is this pass.)
	//

	private readonly Dictionary<int, (int Value, RideSpriteMarker Marker)> headProxies = new(); // by VM head-slot
	private readonly Dictionary<int, RideSpriteMarker> walkProxies = new();                       // by VM walk-slot

	/// <summary>Mirror the VM head table to head-node positions: show a head marker for each occupied slot at
	/// its head node, hide vacated ones. <paramref name="slots"/> is <c>RideVM.HeadSlots</c> (0 = empty).</summary>
	public void SyncHeads( IReadOnlyList<int> slots )
	{
		// The head/walk figures are stand-in visualisations of the ride script's node tables (T-048) — they read
		// as scattered sprites, so only show them with the debug overlay on (T-066); real riders are the peeps.
		if ( !GameSettings.Current.ShowDebug )
		{
			foreach ( var (_, m) in headProxies.Values )
				Entity.All.Remove( m.Entity );
			headProxies.Clear();
			return;
		}
		try
		{
			for ( int slot = 0; slot < slots.Count; slot++ )
			{
				int value = slots[slot];
				bool shown = headProxies.TryGetValue( slot, out var cur );

				if ( value == 0 )
				{
					if ( shown )
					{
						Entity.All.Remove( cur.Marker.Entity );
						headProxies.Remove( slot );
					}
					continue;
				}

				// (Re)create the marker on first show or when the head value changes (the value picks which
				// kid sprite shows, so the head figure varies with the script's head graphic id).
				if ( !shown || cur.Value != value )
				{
					if ( shown )
						Entity.All.Remove( cur.Marker.Entity );

					cur.Marker = new RideSpriteMarker( Peep.KidSpriteDir, Peep.KidSpriteName( value ),
						height: 15f, 90, 200, 250 );
					headProxies[slot] = (value, cur.Marker);
				}

				// Track the head node every frame (it rides the animated body — real per-frame transform):
				// stand the figure at the node, facing its forward direction (the directional sprite frame).
				int nodeId = HeadNodeId( slot );
				var headFwd = nodeId >= 0 && NodeField != null && NodeField.TryResolveFacing( nodeId, out var fwd )
					? fwd : Vector3.Zero;
				cur.Marker.Update( HeadNodePosition( slot ), headFwd, moving: false );
			}
		}
		catch ( Exception ex ) { Log.Warning( $"[ride] head sync failed: {ex.Message}" ); }
	}

	/// <summary>Mirror the VM walk table: position a gliding peep marker for each non-free slot between its
	/// start + end walk nodes, interpolated by the slot's own clock; free slots drop their marker.
	/// <paramref name="slots"/> is <c>RideVM.WalkSlots</c>, <paramref name="gameTime"/> is <c>RideVM.GameTime</c>.</summary>
	public void SyncWalk( IReadOnlyList<RideVM.WalkSlot> slots, int gameTime )
	{
		if ( !GameSettings.Current.ShowDebug ) // stand-in node figures — debug only (T-066), see SyncHeads
		{
			foreach ( var m in walkProxies.Values )
				Entity.All.Remove( m.Entity );
			walkProxies.Clear();
			return;
		}
		try
		{
			for ( int slot = 0; slot < slots.Count; slot++ )
			{
				var s = slots[slot];
				if ( s.State == RideVM.WalkState.Free )
				{
					if ( walkProxies.TryGetValue( slot, out var dead ) )
					{
						Entity.All.Remove( dead.Entity );
						walkProxies.Remove( slot );
					}
					continue;
				}

				var from = NodePosition( s.StartNode );
				var to = NodePosition( s.EndNode );
				bool atEnd = s.State is RideVM.WalkState.Arrived or RideVM.WalkState.Done;
				var (pos, _) = WalkSample( from, to, s.StartTime, s.EndTime, gameTime, atEnd );

				if ( !walkProxies.TryGetValue( slot, out var marker ) )
				{
					marker = new RideSpriteMarker( Peep.KidSpriteDir, Peep.KidSpriteName( slot ),
						height: 15f, 250, 250, 250 );
					walkProxies[slot] = marker;
				}
				// The peep glides along its travel direction (drives the directional frame + walk cycle); it
				// holds the standing frame once Arrived/Done (atEnd). The marker faces the camera itself.
				marker.Update( pos, to - from, moving: !atEnd );
			}
		}
		catch ( Exception ex ) { Log.Warning( $"[ride] walk sync failed: {ex.Message}" ); }
	}

	/// <summary>Position + facing of a peep partway between two walk nodes — the linear interpolation by the
	/// slot's start/end clock + the original's <c>atan2</c> travel facing. Pure (unit-tested). When
	/// <paramref name="atEnd"/> (Arrived/Done) the peep is pinned at the end node.</summary>
	internal static (Vector3 Pos, float Yaw) WalkSample( Vector3 from, Vector3 to, int startTime, int endTime, int now, bool atEnd )
	{
		float span = endTime - startTime;
		float t = atEnd || span <= 0f ? 1f : Math.Clamp( (now - startTime) / span, 0f, 1f );
		var pos = from + (to - from) * t;
		float yaw = MathF.Atan2( to.Y - from.Y, to.X - from.X );
		return (pos, yaw);
	}

	// The object/head node id (type 0x80) head slot `slot` mounts on, or -1 if the ride has no head nodes.
	// Modulo the head-node count so an over-capacity slot still lands on a node.
	private int HeadNodeId( int slot )
	{
		var ids = NodeField?.ObjectNodeIds;
		return ids != null && ids.Count > 0 ? ids[slot % ids.Count] : -1;
	}

	// World position of head slot `slot`: its object/head node resolved through the node field, else the
	// ride centre (a ride with no decoded head nodes behaves as before).
	private Vector3 HeadNodePosition( int slot )
	{
		int nodeId = HeadNodeId( slot );
		return nodeId >= 0 ? NodePosition( nodeId ) : LightPosition( SelfId );
	}

	// A small unlit emissive material of the given colour (the light/particle/marker proxy pattern).
	private static Material<ObjectUniformBuffer> SolidMaterial( byte r, byte g, byte b )
	{
		var mat = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader", MaterialFlags.DoubleSided );
		mat.Set( "Color", new Texture( [r, g, b, 255], 1, 1 ) );
		return mat;
	}

	public void SetReverb( int level ) => Log.Trace( $"[ride] SETREVERB {level}" );

	/// <summary>Where the riders are right now (the ride's moving train / car), or null — screams are placed there.</summary>
	public Func<Vector3?>? RiderPosition { get; set; }

	// DIPMUSIC: the music ducks by `amount` percent for a couple of seconds.
	public void DipMusic( int amount )
	{
		Log.Trace( $"[ride] DIPMUSIC {amount}" );
		Audio.DipMusic( amount / 100f, MusicDipSeconds );
	}

	private const float MusicDipSeconds = 2f;

	//
	// Ride lights (opcodes ENABLELIGHT/DISABLELIGHT/SETLIGHT/COLOURLIGHT, RE'd from op_82..op_85). The
	// original toggles a light object's enable flag and sets its colour×intensity on the scene light. Our
	// renderer is unlit (no dynamic per-pixel lighting), so each enabled light shows a small **emissive
	// colour proxy** at its position — a visible, verifiable stand-in. Real scene lighting is a renderer
	// follow-up. State is kept regardless so the proxy reflects the latest colour/brightness/enable.
	//
	private sealed class RideLight
	{
		public bool Enabled;
		public float R = 1f, G = 1f, B = 1f; // colour 0..1
		public float Brightness = 1f;        // intensity 0..1
		public ModelEntity? Proxy;           // emissive marker, present only while enabled
	}

	private readonly Dictionary<int, RideLight> lights = new();

	/// <summary>How many ride lights are currently enabled (diagnostics / tests).</summary>
	public int EnabledLightCount => lights.Values.Count( l => l.Enabled );

	public void EnableLight( int id )
	{
		var light = GetLight( id );
		light.Enabled = true;
		UpdateLightProxy( id, light );
		Log.Info( $"[ride] ENABLELIGHT {id} (now {EnabledLightCount} on)" );
	}

	public void DisableLight( int id )
	{
		var light = GetLight( id );
		light.Enabled = false;
		UpdateLightProxy( id, light );
		Log.Info( $"[ride] DISABLELIGHT {id} (now {EnabledLightCount} on)" );
	}

	public void SetLight( int id, float brightness )
	{
		var light = GetLight( id );
		light.Brightness = Math.Clamp( brightness, 0f, 1f );
		UpdateLightProxy( id, light );
	}

	public void ColourLight( int id, float r, float g, float b )
	{
		var light = GetLight( id );
		light.R = Math.Clamp( r, 0f, 1f );
		light.G = Math.Clamp( g, 0f, 1f );
		light.B = Math.Clamp( b, 0f, 1f );
		UpdateLightProxy( id, light );
	}

	private RideLight GetLight( int id ) =>
		lights.TryGetValue( id, out var l ) ? l : lights[id] = new RideLight();

	// Show/refresh (or hide) the emissive colour proxy for a light. Best-effort: any renderer failure
	// (e.g. headless) is swallowed so the VM keeps running and the light state stays correct.
	private void UpdateLightProxy( int id, RideLight light )
	{
		try
		{
			// The emissive cube is a debug stand-in for real light rendering — hide it unless the debug overlay
			// is on, so the park reads as real rides, not floating coloured cubes (T-066). The light STATE is
			// unaffected (tracked in `light`); only the marker is gated.
			if ( !light.Enabled || !GameSettings.Current.ShowDebug )
			{
				if ( light.Proxy != null )
				{
					Entity.All.Remove( light.Proxy );
					light.Proxy = null;
				}
				return;
			}

			byte R = (byte)(Math.Clamp( light.R * light.Brightness, 0f, 1f ) * 255f);
			byte G = (byte)(Math.Clamp( light.G * light.Brightness, 0f, 1f ) * 255f);
			byte B = (byte)(Math.Clamp( light.B * light.Brightness, 0f, 1f ) * 255f);

			var mat = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader", MaterialFlags.DoubleSided );
			mat.Set( "Color", new Texture( [R, G, B, 255], 1, 1 ) );
			light.Proxy ??= new ModelEntity { Scale = new Vector3( 3f ) };
			light.Proxy.Model = Primitives.Cube.GenerateModel( mat );
			light.Proxy.Position = LightPosition( id ) + Vector3.Up * 6f;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ride] light {id} proxy failed: {e.Message}" );
		}
	}

	// A light's world position: its own object's first part if it has one, else the ride body, else origin.
	private Vector3 LightPosition( int id )
	{
		if ( objects.TryGetValue( id, out var obj ) && obj.Parts.Count > 0 )
			return obj.Parts[0].Entity.Position;
		if ( objects.TryGetValue( SelfId, out var body ) && body.Parts.Count > 0 )
			return body.Parts[0].Entity.Position;
		return Vector3.Zero;
	}

	// Play an actual peep scream at the given volume, and stamp the time so a sustained scream paces its
	// re-triggers. Screams are peep voices in KidsHD.sdt (sceem*/screem*/yell*/whoop*), NOT RideHD — the
	// script's scream code (0) is not an index into a sound bank, so we pick a scream sample by name.
	// (Playing RideHD[code] before gave Backfire/Crunch — the "explosions/gunshots" bug.)
	private void PlayScream( int code, float gain )
	{
		lastScreamAt = Time.Now;
		try
		{
			var path = Path.Join( GameDir.GamePath, "data", "global", "sound", "KidsHD.sdt" );
			if ( !File.Exists( path ) )
				return;
			peepSounds ??= new SdtArchive( path );
			screamIndices ??= peepSounds.soundFiles
				.Select( ( f, i ) => (f.Name, i) )
				.Where( x => x.Name.Contains( "sceem", StringComparison.OrdinalIgnoreCase )
					|| x.Name.Contains( "screem", StringComparison.OrdinalIgnoreCase )
					|| x.Name.Contains( "yell", StringComparison.OrdinalIgnoreCase )
					|| x.Name.Contains( "whoop", StringComparison.OrdinalIgnoreCase ) )
				.Select( x => x.i )
				.ToArray();
			if ( screamIndices.Length == 0 )
				return;

			var track = peepSounds.soundFiles[screamIndices[Random.Shared.Next( screamIndices.Length )]];
			// The riders scream from where they are: the moving train / car when the ride has one (T-047), else the body.
			Audio.PlaySfx3D( $"scream_{track.Name}", track.SoundData, RiderPosition?.Invoke() ?? LightPosition( SelfId ), gain );
			Log.Info( $"[ride] scream -> {track.Name}" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ride] scream failed: {e.Message}" );
		}
	}

	/// <summary>
	/// Supplies the decoded keyframe tracks for an animation channel (the ride's <c>&lt;base&gt;&lt;c&gt;[n].md2</c>
	/// files, parsed by <see cref="RideKeyframeFile"/>). When that channel is the active animation, the
	/// engine drives each animated surface's part by its real rotation track instead of the bob.
	/// </summary>
	public void SetChannelKeyframes( int anim, RideKeyframeFile keyframes )
	{
		FixRotationOrder( keyframes );
		if ( keyframes.Surfaces.Count > 0 )
			channelAnims[anim] = keyframes;
	}

	/// <summary>
	/// Chooses the body's running loop + idle channels once keyframes are loaded, and leaves the body
	/// idle (occupancy drives whether it runs — see <see cref="SetActive"/>). The running loop prefers
	/// Main, then Idle, otherwise any other channel with real keyframe data except the one-shot Create / Break /
	/// Repair; the idle loop is the ride's Idle channel when it ships one (else the body
	/// rests). Called by the Ride after <see cref="SetChannelKeyframes"/>.
	/// </summary>
	public void StartBestBodyAnim()
	{
		if ( !objects.TryGetValue( SelfId, out var body ) )
			return;

		int chosen = -1;
		foreach ( var pref in new[] { (int)ScriptDefs.Animations.ANIM_Main, (int)ScriptDefs.Animations.ANIM_Idle } )
			if ( channelAnims.ContainsKey( pref ) ) { chosen = pref; break; }

		// Not the one-shots: Create builds the ride up out of nothing, Break / Repair take it apart — looping one would
		// show the ride forever half-built (now that their morphs reach the right meshes).
		if ( chosen < 0 )
			chosen = channelAnims.Keys.Where( k => k is not ((int)ScriptDefs.Animations.ANIM_Create or (int)ScriptDefs.Animations.ANIM_Break
				or (int)ScriptDefs.Animations.ANIM_Repair) ).OrderBy( k => k ).DefaultIfEmpty( -1 ).First();

		bodyLoopAnim = chosen;
		// Idle loop for an empty ride: only if the ride ships an Idle channel (else it rests).
		bodyIdleAnim = FrameCount( (int)ScriptDefs.Animations.ANIM_Idle ) > 0 ? (int)ScriptDefs.Animations.ANIM_Idle : -1;

		// The real-world length of one full pass of the running loop (its decoded keyframe track), used
		// as the authentic per-ride ride duration. Null when the loop has no decoded keyframes.
		BodyLoopDuration = chosen >= 0 && channelAnims.TryGetValue( chosen, out var loopKf ) && loopKf.Duration > 0
			? loopKf.Duration / KeyframeRate
			: null;

		if ( chosen >= 0 )
			Log.Info( $"[ride] body loop {(ScriptDefs.Animations)chosen}, idle "
				+ ( bodyIdleAnim >= 0 ? $"{(ScriptDefs.Animations)bodyIdleAnim}" : "rest" ) + " (keyframed)" );

		GoIdle( body );
	}

	/// <summary>How much faster than authored the body runs its motion (the ride's speed setting).</summary>
	public float BodySpeed { get; set; } = 1f;

	private bool bodyBuilt; // the body's Create has played
	private bool bodyBroken; // broken down: the Break animation holds its last frame

	/// <summary>The body stands built already (a loaded park): the script's Create is ignored.</summary>
	public void SkipBuild() => bodyBuilt = true;

	/// <summary>The ride broke down: it stops, and plays its Break animation (the totem's figures slump, the crates
	/// burst), holding the last frame until it is repaired.</summary>
	public void BreakDown()
	{
		if ( !objects.TryGetValue( SelfId, out var body ) )
			return;
		bodyRunning = false;
		bodyBroken = true;
		bodyQueue.Clear();
		if ( channelAnims.ContainsKey( (int)ScriptDefs.Animations.ANIM_Break ) )
			StartAnim( body, (int)ScriptDefs.Animations.ANIM_Break, loop: false );
		else
			StopAnim( body );
	}

	/// <summary>A mechanic fixed the ride: it plays its Repair animation, then settles back to idle.</summary>
	public void Repaired()
	{
		if ( !objects.TryGetValue( SelfId, out var body ) )
			return;
		bodyBroken = false;
		bodyQueue.Clear();
		if ( channelAnims.ContainsKey( (int)ScriptDefs.Animations.ANIM_Repair ) )
			StartAnim( body, (int)ScriptDefs.Animations.ANIM_Repair, loop: false );
		else
			GoIdle( body );
	}
	private int bodyLoopAnim = -1;
	private int bodyIdleAnim = -1;
	private bool bodyRunning;

	/// <summary>Real-world seconds for one full pass of the ride's running loop animation (null if it has
	/// no decoded keyframes). The owning Ride uses it as the authentic ride duration.</summary>
	public float? BodyLoopDuration { get; private set; }

	// The body's pending animation stages (the rest of the boarding/unloading cycle after the stage it
	// is currently playing). Update advances through these as each one-shot stage finishes.
	private readonly Queue<(int Anim, bool Loop)> bodyQueue = new();

	/// <summary>
	/// Runs (true) or idles (false) the ride body, tying its animation to occupancy. The first rider
	/// starts the boarding cycle (Load → Start → Main loop); emptying runs the unloading cycle (End →
	/// Unload → Idle loop, or rest). Stages the ride doesn't ship are skipped, so a ride with no cycle
	/// art simply switches between its run loop and idle. Idempotent while occupied (a second rider
	/// boarding does not restart the cycle).
	/// </summary>
	public void SetActive( bool active )
	{
		if ( !objects.TryGetValue( SelfId, out var body ) || bodyBroken )
			return; // a broken ride holds its breakdown until repaired

		if ( active )
		{
			if ( bodyRunning )
				return; // already running — another rider joining must not restart the cycle
			bodyRunning = true;
			var seq = BuildCycle( running: true );
			Log.Trace( $"[ride] boarding cycle: {DescribeCycle( seq )}" );
			PlayBodySequence( body, seq );
		}
		else if ( bodyRunning )
		{
			bodyRunning = false;
			var seq = BuildCycle( running: false );
			Log.Trace( $"[ride] unloading cycle: {DescribeCycle( seq )}" );
			PlayBodySequence( body, seq );
		}
		else
		{
			GoIdle( body ); // not running (initial setup / already empty): just settle into idle
		}
	}

	// Builds the body's animation cycle. Running: optional Load/Start lead-ins then the run loop.
	// Stopping: optional End/Unload outros then the idle loop (or rest). Only stages the ride actually
	// ships are included.
	private List<(int Anim, bool Loop)> BuildCycle( bool running )
	{
		var seq = new List<(int Anim, bool Loop)>();
		void AddIfShipped( ScriptDefs.Animations a )
		{
			if ( FrameCount( (int)a ) > 0 )
				seq.Add( ((int)a, false) );
		}

		if ( running )
		{
			AddIfShipped( ScriptDefs.Animations.ANIM_Load );
			AddIfShipped( ScriptDefs.Animations.ANIM_Start );
			if ( bodyLoopAnim >= 0 )
				seq.Add( (bodyLoopAnim, true) );
		}
		else
		{
			AddIfShipped( ScriptDefs.Animations.ANIM_End );
			AddIfShipped( ScriptDefs.Animations.ANIM_Unload );
			if ( bodyIdleAnim >= 0 )
				seq.Add( (bodyIdleAnim, true) );
		}
		return seq;
	}

	private static string DescribeCycle( List<(int Anim, bool Loop)> seq ) =>
		seq.Count == 0 ? "rest" : string.Join( " -> ", seq.Select( s => $"{(ScriptDefs.Animations)s.Anim}{( s.Loop ? "(loop)" : "" )}" ) );

	// Starts the first stage of a cycle now and queues the rest; an empty cycle rests the body.
	private void PlayBodySequence( RideObject body, List<(int Anim, bool Loop)> seq )
	{
		bodyQueue.Clear();
		if ( seq.Count == 0 )
		{
			StopAnim( body );
			return;
		}

		StartAnim( body, seq[0].Anim, seq[0].Loop );
		for ( int i = 1; i < seq.Count; i++ )
			bodyQueue.Enqueue( seq[i] );
	}

	// Puts the body into its empty-ride state directly (no End/Unload outro): the idle loop if shipped,
	// otherwise the rest pose.
	private void GoIdle( RideObject body )
	{
		bodyQueue.Clear();
		if ( bodyIdleAnim >= 0 )
			StartAnim( body, bodyIdleAnim, loop: true );
		else
			StopAnim( body );
	}

	private readonly Dictionary<int, RideKeyframeFile> channelAnims = new();

	/// <summary>Per-frame animation: drive real keyframe tracks where we have them, else the placeholder bob.</summary>
	public void Update( float now )
	{
		// Sustain an active scream by re-playing it each period (PlaySfx is one-shot; there's no looping
		// sfx source). STOPSCREAM clears `screaming`.
		if ( screaming && now - lastScreamAt >= ScreamPeriod )
			PlayScream( screamCode, screamGain );

		particles.Tick( now ); // drift + expire the particle-effect proxies

		foreach ( var obj in objects.Values )
		{
			if ( obj.AnimId == null || obj.Parts.Count == 0 )
				continue;

			var anim = obj.AnimId.Value;
			var t = ( now - obj.AnimStart ) * obj.AnimSpeed * ( obj.Id == SelfId ? BodySpeed : 1f );
			if ( !obj.AnimLoop && t > DurationOf( anim ) && !(obj.Id == SelfId && bodyBroken && anim == (int)ScriptDefs.Animations.ANIM_Break) )
			{
				// The ride body steps through its boarding/unloading cycle: when a one-shot stage
				// (Load/Start/End/Unload) finishes, advance to the next queued stage rather than stopping.
				if ( obj.Id == SelfId && bodyQueue.Count > 0 )
				{
					var (next, loop) = bodyQueue.Dequeue();
					StartAnim( obj, next, loop );
				}
				else if ( obj.Id == SelfId && bodyRunning && bodyLoopAnim >= 0 )
					StartAnim( obj, bodyLoopAnim, loop: true ); // a one-shot (the build, a repair) over: back to the run
				else if ( obj.Id == SelfId )
					GoIdle( obj );                              // … or to the idle loop
				else
					StopAnim( obj );
				continue;
			}

			if ( channelAnims.TryGetValue( anim, out var kf ) )
				ApplyKeyframes( obj, kf, t );
			else
				ApplyBob( obj, t );

			if ( obj.Id == SelfId )
				PublishBodyNodes( obj );
		}
	}

	// Feeds the ride's body-attached nodes their REAL per-frame positions (T-048): the keyframe animation
	// above has just written each body part's live world transform, and the original reads a node's position
	// off the surface matrix it's bound to (FUN_00556b90, translation row +0x30; docs/08). So instead of the
	// static footprint ring, each non-walk node (head/object/particle mount) tracks an animated body part.
	//
	// The node→surface *binding* isn't in the file (the node entry's pointer slots are null), so which part a
	// node binds to is a deterministic stand-in — nodes are ordered onto the model's animated surfaces (the
	// ones the keyframe file actually moves; all parts if none animate) — but the positions themselves are
	// the real per-frame transforms. A car/seat node the vehicle owns still wins (PublishMoving > PublishBody).
	private void PublishBodyNodes( RideObject body )
	{
		if ( NodeField == null || body.Parts.Count == 0 )
			return;

		var nodeIds = NodeField.BodyNodeIds;
		if ( nodeIds.Count == 0 )
			return;

		// Candidate parts: prefer the surfaces the active keyframe file animates (those genuinely move, like
		// the original's animated bind targets); fall back to every body part when nothing animates (bob/static).
		var candidates = AnimatedSurfaceIndices( body );

		for ( int i = 0; i < nodeIds.Count; i++ )
		{
			int part = candidates[i % candidates.Count];
			var entity = body.Parts[part].Entity;
			// Forward = the part's rotated +X axis (the surface matrix forward row +0x20 the original reads),
			// so an object/head bound to an animated surface turns with it. Consumers take the yaw.
			var sf = System.Numerics.Vector3.Transform( System.Numerics.Vector3.UnitX, entity.Rotation );
			NodeField.PublishBody( nodeIds[i], entity.Position, new Vector3( sf.X, sf.Y, sf.Z ) );
		}
	}

	// The valid body-part indices the active animation drives, in order — used to bind nodes onto moving
	// surfaces. Falls back to all parts (0..n) when the channel has no keyframes or none are in range.
	private List<int> AnimatedSurfaceIndices( RideObject body )
	{
		var result = new List<int>();
		if ( body.AnimId is int anim && channelAnims.TryGetValue( anim, out var kf ) )
		{
			foreach ( var sa in kf.Surfaces )
				if ( sa.HasAnimation && sa.SurfaceIndex >= 0 && sa.SurfaceIndex < body.Parts.Count
					&& !result.Contains( sa.SurfaceIndex ) )
					result.Add( sa.SurfaceIndex );
		}
		if ( result.Count == 0 )
			for ( int p = 0; p < body.Parts.Count; p++ )
				result.Add( p );
		return result;
	}

	// Drives each animated surface's part by its keyframe tracks. The model-space TRS is swizzled to
	// our world (Y/Z swapped — the same swizzle the loader applies to the mesh transform; rotation W
	// is negated to match) and composed onto the part's loaded base transform. Each track loops over
	// its own last-key time (looping anims) or clamps to its end (one-shots), so a short rotation
	// track keeps spinning while a full-length identity scale track stays a no-op.
	private void ApplyKeyframes( RideObject obj, RideKeyframeFile kf, float elapsed )
	{
		var clock = elapsed * KeyframeRate;
		if ( tree != null && obj.Id == SelfId )
		{
			ApplyTreeKeyframes( obj, kf, clock );
			ApplyMorph( obj, kf, clock );
			return;
		}

		foreach ( var sa in kf.Surfaces )
		{
			if ( sa.SurfaceIndex < 0 || sa.SurfaceIndex >= obj.Parts.Count || !sa.HasAnimation )
				continue;

			var (entity, basePos, baseRot, baseScale) = obj.Parts[sa.SurfaceIndex];

			if ( sa.Rotation.Count > 0 )
			{
				var q = RideKeyframeFile.SampleRotation( sa.Rotation, TrackTime( clock, sa.Rotation[^1].Time, obj.AnimLoop ) );
				entity.Rotation = baseRot * new System.Numerics.Quaternion( q.X, q.Z, q.Y, -q.W );
			}

			if ( sa.Scale.Count > 0 )
			{
				var s = RideKeyframeFile.SampleVector( sa.Scale, TrackTime( clock, sa.Scale[^1].Time, obj.AnimLoop ), System.Numerics.Vector3.One );
				entity.Scale = new Vector3( baseScale.X * s.X, baseScale.Y * s.Z, baseScale.Z * s.Y );
			}

			if ( sa.Translation.Count > 0 )
			{
				var tr = RideKeyframeFile.SampleVector( sa.Translation, TrackTime( clock, sa.Translation[^1].Time, obj.AnimLoop ), System.Numerics.Vector3.Zero );
				entity.Position = basePos + new Vector3( tr.X, tr.Z, tr.Y );
			}
		}

		ApplyMorph( obj, kf, clock );
	}

	// ── The model's object tree (T-033 corrected by the advisor's gestures, T-046) ──────────────────────────────────
	// A key is an object's whole local rotation / scale, and objects hang off one another (the cars off the wheel,
	// the arms off the body; nodes are pivots between them). Each part is posed as L · B⁻¹ · A: its bind local, back
	// out of its hierarchical bind pose, into its hierarchical animated pose — exactly the loaded look at rest, and
	// moving with everything above it.

	private ModelHierarchy? tree;
	// Per mesh: each of its positions' vertices (ModelFile.Mesh.VertexOrder inverted), for the morphs.
	private List<int[][]>? vertexOfPosition;

	/// <summary>The meshes' vertex orders (which position each vertex is), so morphs reach the right vertices.</summary>
	public void SetVertexOrders( IEnumerable<ushort[]> orders )
		=> vertexOfPosition = orders.Select( order => Enumerable.Range( 0, order.Length == 0 ? 0 : order.Max() + 1 )
			.Select( position => Enumerable.Range( 0, order.Length ).Where( v => order[v] == position ).ToArray() ).ToArray() ).ToList();
	private Matrix4x4[]? bindWorld;
	private static readonly Matrix4x4 SwapYZ = new( 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1 );

	private void FixRotationOrder( RideKeyframeFile keyframes )
	{
		if ( tree != null )
			KeyframePoser.FixRotationOrder( tree, keyframes );
	}

	/// <summary>The ride model's object tree (from its .md2), so keyframes pose the parts through it.</summary>
	public void SetHierarchy( ModelHierarchy? hierarchy )
	{
		tree = hierarchy;
		foreach ( var kf in channelAnims.Values )
			FixRotationOrder( kf );
		bindWorld = null;
		if ( tree == null )
			return;
		bindWorld = new Matrix4x4[tree.ObjectCount];
		var done = new bool[tree.ObjectCount];
		for ( int i = 0; i < tree.ObjectCount; i++ )
			WorldOf( i, j => tree.Local[j], bindWorld, done );
	}

	private Matrix4x4 WorldOf( int i, Func<int, Matrix4x4> local, Matrix4x4[] world, bool[] done )
	{
		if ( done[i] )
			return world[i];
		int parent = tree!.Parent[i];
		// A root mesh keeps its own matrix (as the loader placed it); a root node is the scene's frame, left out.
		var m = parent < 0 ? (i < tree.MeshCount ? local( i ) : Matrix4x4.Identity) : local( i ) * WorldOf( parent, local, world, done );
		world[i] = m;
		done[i] = true;
		return m;
	}

	private static bool Playing( RideKeyframeFile.SurfaceAnim sa, RideKeyframeFile kf, float clock, bool loop, out float local )
		=> KeyframePoser.Playing( sa, kf, clock, loop, out local );

	private readonly Dictionary<int, RideKeyframeFile.SurfaceAnim> poseTracks = new();
	private Matrix4x4[] poseWorld = Array.Empty<Matrix4x4>();
	private bool[] poseDone = Array.Empty<bool>();

	private void ApplyTreeKeyframes( RideObject obj, RideKeyframeFile kf, float clock )
	{
		// The tracks by object, from the file of the channel's sequence playing now.
		var byObject = poseTracks;
		byObject.Clear();
		foreach ( var sa in kf.Surfaces )
			if ( sa.SurfaceIndex < tree!.ObjectCount && (sa.Rotation.Count > 0 || sa.Scale.Count > 0 || sa.Translation.Count > 0)
				&& Playing( sa, kf, clock, obj.AnimLoop, out _ ) )
				byObject[sa.SurfaceIndex] = sa;
		if ( byObject.Count == 0 )
			return;

		Matrix4x4 Local( int i )
		{
			var bind = tree!.Local[i];
			if ( !byObject.TryGetValue( i, out var sa ) || !Playing( sa, kf, clock, obj.AnimLoop, out var t ) )
				return bind;
			Matrix4x4.Decompose( bind, out var scl, out var rot, out var pos );
			if ( sa.Rotation.Count > 0 )
				rot = RideKeyframeFile.SampleRotation( sa.Rotation, t );
			if ( sa.Scale.Count > 0 )
				scl = RideKeyframeFile.SampleVector( sa.Scale, t, scl );
			if ( sa.Translation.Count > 0 )
				pos += RideKeyframeFile.SampleVector( sa.Translation, t, System.Numerics.Vector3.Zero );
			return Matrix4x4.CreateScale( scl ) * Matrix4x4.CreateFromQuaternion( rot ) * Matrix4x4.CreateTranslation( pos );
		}

		// Reused every frame (a pose per ride per frame would otherwise be steady garbage).
		if ( poseWorld.Length != tree!.ObjectCount )
		{
			poseWorld = new Matrix4x4[tree.ObjectCount];
			poseDone = new bool[tree.ObjectCount];
		}
		var world = poseWorld;
		var done = poseDone;
		Array.Clear( done );
		for ( int p = 0; p < obj.Parts.Count && p < tree.MeshCount; p++ )
		{
			// Only parts with something animated at or above them move.
			bool moves = false;
			for ( int a = p; a >= 0 && !moves; a = tree.Parent[a] )
				moves = byObject.ContainsKey( a );
			if ( !moves || !Matrix4x4.Invert( bindWorld![p], out var bindInverse ) )
				continue;

			var animated = tree.Local[p] * bindInverse * WorldOf( p, Local, world, done );
			// Into the parts' frame (the loader's Y/Z swap) and onto the part's placement in the park.
			var (entity, basePos, baseRot, baseScale) = obj.Parts[p];
			var baseMatrix = Matrix4x4.CreateScale( baseScale.GetSystemVector3() ) * Matrix4x4.CreateFromQuaternion( baseRot )
				* Matrix4x4.CreateTranslation( basePos.GetSystemVector3() );
			if ( !Matrix4x4.Invert( SwapYZ * tree.Local[p] * SwapYZ, out var restInverse ) )
				continue;
			var final = SwapYZ * animated * SwapYZ * restInverse * baseMatrix;
			if ( !Matrix4x4.Decompose( final, out var scale, out var rotation, out var position ) )
				continue;
			entity.Position = new Vector3( position.X, position.Y, position.Z );
			entity.Rotation = rotation;
			entity.Scale = new Vector3( scale.X, scale.Y, scale.Z );
		}
	}

	// Vertex-morph: a global additive blend-shape over one combined vertex buffer (see
	// docs/08-ghidra-animation.md). Resets each touched part to its rest pose, then writes the
	// keyframed positions (model space → world Y/Z-swizzled, the same swizzle the loader applies to
	// vertices) at the morph's global vertex slots, and re-uploads only the parts that changed.
	private void ApplyMorph( RideObject obj, RideKeyframeFile kf, float clock )
	{
		Span<bool> touched = obj.Parts.Count <= 64 ? stackalloc bool[obj.Parts.Count] : new bool[obj.Parts.Count];

		foreach ( var sa in kf.Surfaces )
		{
			// A morph addresses its own mesh's positions (each written to that position's vertices); without the mesh's
			// vertex order, fall back to one combined buffer across the parts.
			int[][]? positions = obj.Id == SelfId && vertexOfPosition != null && sa.SurfaceIndex < vertexOfPosition.Count
				&& sa.SurfaceIndex < obj.Parts.Count ? vertexOfPosition[sa.SurfaceIndex] : null;
			if ( sa.Morph.Count == 0 || !Playing( sa, kf, clock, obj.AnimLoop, out var within ) )
				continue;
			foreach ( var m in sa.Morph )
			{
				if ( m.VertexSlots.Length == 0 || m.Times.Length == 0 )
					continue;

				var t = within;
				for ( int i = 0; i < m.VertexSlots.Length; i++ )
				{
					var p = m.Sample( i, t );
					if ( positions != null )
					{
						int part = sa.SurfaceIndex;
						var verts = obj.Parts[part].Entity.Model?.Vertices;
						if ( verts == null || m.VertexSlots[i] >= positions.Length )
							continue;
						if ( !touched[part] )
						{
							ResetPartVerts( obj, part );
							touched[part] = true;
						}
						foreach ( int v in positions[m.VertexSlots[i]] )
							if ( v < verts.Length )
								verts[v].Position = new Vector3( p.X, p.Z, p.Y );
						continue;
					}

					if ( !TryMapSlot( obj, m.VertexSlots[i], out var slotPart, out var local ) )
						continue;

					if ( !touched[slotPart] )
					{
						ResetPartVerts( obj, slotPart );
						touched[slotPart] = true;
					}
					obj.Parts[slotPart].Entity.Model!.Vertices[local].Position = new Vector3( p.X, p.Z, p.Y );
				}
			}
		}

		for ( int part = 0; part < touched.Length; part++ )
			if ( touched[part] )
				obj.Parts[part].Entity.Model?.UploadVertices();
			else if ( obj.Morphed.Contains( part ) )
			{
				ResetPartVerts( obj, part ); // its morph is over (the next file of the sequence leaves it alone)
				obj.Parts[part].Entity.Model?.UploadVertices();
			}
		obj.Morphed.Clear();
		for ( int part = 0; part < touched.Length; part++ )
			if ( touched[part] )
				obj.Morphed.Add( part );
	}

	// Maps a global morph vertex slot to its part + local index (parts hold contiguous global ranges).
	private static bool TryMapSlot( RideObject obj, int slot, out int part, out int local )
	{
		for ( int p = obj.Parts.Count - 1; p >= 0; p-- )
		{
			if ( slot >= obj.PartVertexOffset[p] )
			{
				local = slot - obj.PartVertexOffset[p];
				part = p;
				var verts = obj.Parts[p].Entity.Model?.Vertices;
				return verts != null && local < verts.Length;
			}
		}
		part = local = -1;
		return false;
	}

	private static void ResetPartVerts( RideObject obj, int part )
	{
		var verts = obj.Parts[part].Entity.Model?.Vertices;
		var basePos = obj.PartBaseVerts[part];
		if ( verts == null )
			return;
		for ( int i = 0; i < verts.Length && i < basePos.Length; i++ )
			verts[i].Position = basePos[i];
	}

	// Maps the animation clock onto a single track's [0, last] domain: wrap for a looping anim, clamp
	// for a one-shot. A zero-length track collapses to 0.
	private static float TrackTime( float clock, float last, bool loop ) =>
		last <= 0 ? 0f : ( loop ? clock % last : MathF.Min( clock, last ) );

	private static void ApplyBob( RideObject obj, float t )
	{
		// Procedural placeholder for channels we have no decoded keyframes for: a gentle vertical bob.
		var bob = MathF.Sin( t * 2f ) * BobAmplitude;
		foreach ( var (entity, basePos, _, _) in obj.Parts )
			entity.Position = basePos + new Vector3( 0, 0, bob );
	}

	private static void StartAnim( RideObject o, int anim, bool loop )
	{
		o.AnimId = anim;
		o.AnimLoop = loop;
		o.AnimStart = Time.Now;
	}

	private static void StopAnim( RideObject o )
	{
		o.AnimId = null;
		foreach ( var (entity, basePos, baseRot, baseScale) in o.Parts )
		{
			entity.Position = basePos;
			entity.Rotation = baseRot;
			entity.Scale = baseScale;
		}
		// And the shapes a morph left (the crates the Crazy Ape burst).
		foreach ( int part in o.Morphed )
			if ( part < o.Parts.Count )
			{
				ResetPartVerts( o, part );
				o.Parts[part].Entity.Model?.UploadVertices();
			}
		o.Morphed.Clear();
	}

	// Unknown ids fall back to the ride body, so a script animating a part we don't model yet still
	// produces visible motion (stage 1). Once parts get their own models this resolves them directly.
	private RideObject? Resolve( int id ) =>
		objects.TryGetValue( id, out var obj ) ? obj : objects.GetValueOrDefault( SelfId );
}
