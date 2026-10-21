namespace OpenTPW;

/// <summary>
/// A set of particle-effect bursts. The original instantiates a <c>.PLB</c> effect at a 3D position; our renderer has
/// no particle system, so this uses the <b>decoded .PLB</b> (T-019): the effect is looked up by its <c>par_lib.h</c>
/// code, and a burst of soft camera-facing sprites walking its colour ramp is spawned with the effect's emission
/// velocity / acceleration / lifetime. Each ride engine owns one (REPAIREFFECT/SPARK/EVENT particles, T-047); the
/// shared <see cref="World"/> emitter plays park-level effects (build / demolish / upgrade puffs) and is ticked by
/// the level. A real GPU particle system is a renderer follow-up.
/// </summary>
public sealed class ParticleEmitter
{
	/// <summary>The park-wide emitter for effects not tied to a ride's lifetime (ticked by <see cref="Level"/>).</summary>
	public static ParticleEmitter World { get; } = new();

	// The build/demolish puffs every theme's category .sam declares (Info.Create/DestroyParticleEffect in Rides.sam,
	// SideShow.sam, Shops.sam, Features.sam — the same in all four themes) and Rides.sam's UpgradeParticleEffect:
	// par_lib.h P_EFFECT_Create1..4 / Destroy1..4 / Upgrade.
	public const int CreateRide = 79, CreateSideshow = 80, CreateShop = 81, CreateFeature = 82;
	public const int DestroyRide = 75, DestroySideshow = 76, DestroyShop = 77, DestroyFeature = 78;
	public const int Upgrade = 89;

	/// <summary>Remove every live particle.</summary>
	public void Clear()
	{
		foreach ( var p in particleProxies )
			Entity.All.Remove( p.Entity );
		particleProxies.Clear();
	}

	private static ParticleLibraryFile? particleLib;
	private static bool particleLibTried;
	private static ParticleLibraryFile? ParticleLib
	{
		get
		{
			if ( !particleLibTried )
			{
				particleLibTried = true;
				try
				{
					// Through the VFS (rooted at the install's data/), case-insensitive (T-014).
					using var s = FileSystem.OpenRead( "Particle/Tp2.plb" );
					if ( s != null )
						particleLib = new ParticleLibraryFile( s );
				}
				catch ( Exception e ) { Log.Warning( $"[particle] library load failed: {e.Message}" ); }
			}
			return particleLib;
		}
	}

	// A single live particle: a soft camera-facing sprite that drifts under its own velocity + the effect's
	// acceleration and walks the effect's colour ramp until it expires. Driven by the .PLB fields decoded in T-019.
	private sealed class ParticleProxy
	{
		public required ModelEntity Entity;
		public Vector3 Velocity;
		public Vector3 Acceleration;
		public float Expire;
		public float Born;
		public required Model[] Ramp; // one billboard model per colour-ramp stop (or a single stop)
	}

	// Billboard models per effect: a soft round sprite tinted by each of the effect's ramp stops, so a particle can
	// walk the .PLB colour ramp over its life without a material per particle.
	private static readonly Dictionary<int, Model[]> rampModels = new();

	private static Model[] RampModels( int effectCode, ParticleEffect? fx, (byte R, byte G, byte B) fallback )
	{
		if ( rampModels.TryGetValue( effectCode, out var cached ) )
			return cached;

		var stops = fx is { ColorRamp.Count: > 0 } && fx.CyclesColorRamp
			? fx.ColorRamp.ToList()
			: new List<ParticleColor> { new( fallback.R, fallback.G, fallback.B, 255 ) };
		var blob = Peep.ShadowBlob( 16 ); // radial alpha falloff (black) — recoloured per stop below
		var models = stops.Select( stop =>
		{
			var rgba = (byte[])blob.Clone();
			for ( int i = 0; i < rgba.Length; i += 4 )
			{
				rgba[i] = stop.R; rgba[i + 1] = stop.G; rgba[i + 2] = stop.B;
				rgba[i + 3] = (byte)Math.Min( 255f, rgba[i + 3] * 2.3f ); // the blob peaks at ~110: lift it to opaque
			}
			var mat = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader",
				MaterialFlags.DoubleSided | MaterialFlags.DisableDepthWrite );
			mat.Set( "Color", new Texture( rgba, 16, 16 ) );
			return Primitives.Plane.GenerateModel( mat );
		} ).ToArray();
		rampModels[effectCode] = models;
		return models;
	}

	// The rotation turning a plane (normal +Z) to face the camera.
	private static System.Numerics.Quaternion FacingCamera( Vector3 position )
	{
		var to = (Camera.Position - position).GetSystemVector3();
		if ( to.LengthSquared() < 1e-6f )
			return System.Numerics.Quaternion.Identity;
		to = System.Numerics.Vector3.Normalize( to );
		var z = System.Numerics.Vector3.UnitZ;
		float d = System.Numerics.Vector3.Dot( z, to );
		if ( d < -0.9999f )
			return System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitX, MathF.PI );
		return System.Numerics.Quaternion.Normalize( new System.Numerics.Quaternion( System.Numerics.Vector3.Cross( z, to ), 1f + d ) );
	}

	private readonly List<ParticleProxy> particleProxies = new();
	private float lastNow; // most recent Update time, for timing proxy expiry + integration

	// Burst tuning. The .PLB stores velocity/lifetime in engine fixed-point; these map them to world units at
	// a visible scale (the relative magnitudes + signs are authentic, the absolute scale is a display choice).
	private const float ParticleProxyLife = 1.2f; // fallback proxy life when the effect has no decoded lifetime
	private const int MaxBurst = 24;            // cap particles per effect so a big burst stays cheap
	private const float TickToSeconds = 1f / 200f; // LifetimeTicks → seconds (Sparks 100→0.5s, Smoke 800→4s)
	private const float VelScale = 0.04f;       // fixed-point velocity → world units/sec
	private const float SpreadSpeed = 3.5f;     // random outward burst speed added to every particle (units/sec)
	private const float MaxEmissionSpeed = 8f;  // cap on the authored emission speed (world units/sec)

	/// <summary>Burst effect <paramref name="effectCode"/> (a <c>par_lib.h</c> <c>P_EFFECT_*</c> code) at a world position.</summary>
	public void Spawn( int effectCode, Vector3 position )
	{
		// Particles render as soft camera-facing sprites walking the effect's .PLB colour ramp (they were coloured
		// debug cubes, hidden behind the debug overlay — T-066).
		var (name, r, g, b) = ResolveEffectColour( effectCode );
		if ( name == "?" )
		{
			// Code outside the decoded Tp2.plb (e.g. a per-ride custom/other-pool effect we don't have) —
			// don't render a meaningless white proxy; just record it. See T-037 (per-type pool mapping).
			Log.Trace( $"[particle] effect {effectCode} (unresolved)" );
			return;
		}
		try
		{
			// Drive the burst from the .PLB fields (T-019): how many particles, how long they live, their
			// emission velocity + acceleration. A continuous emitter (BurstCount 0, e.g. Smoke/Repair) still
			// shows a small puff. The cube colour is the effect's representative ramp stop.
			var fx = EffectFor( effectCode );
			int count = Math.Clamp( fx?.BurstCount ?? 1, 1, MaxBurst );
			// A continuous emitter (BurstCount 0): a short puff, or — for a long-lived one like the build/demolish
			// Create*/Destroy* (4.5–8.5 s) — a fuller cloud, since we release it all at once.
			bool longStream = fx is { BurstCount: 0, LifetimeTicks: >= 2000 };
			if ( fx is { BurstCount: 0 } )
				count = longStream ? 14 : 4;
			float life = fx is { LifetimeTicks: > 0 }
				? Math.Clamp( fx.LifetimeTicks * TickToSeconds, 0.3f, 4f )
				: ParticleProxyLife;
			var baseVel = fx != null
				? new Vector3( fx.EmissionVelocity.X, fx.EmissionVelocity.Y, fx.EmissionVelocity.Z ) * VelScale
				: Vector3.Zero;
			if ( baseVel.Length > MaxEmissionSpeed ) // the Create*/Destroy* 873 would fling the cloud off-screen in a second
				baseVel = baseVel * (MaxEmissionSpeed / baseVel.Length);
			var accel = fx != null
				? new Vector3( fx.Acceleration.X, fx.Acceleration.Y, fx.Acceleration.Z ) * VelScale
				: Vector3.Zero;

			var ramp = RampModels( effectCode, fx, (r, g, b) );
			float scale = count > 1 && !longStream ? 1.2f : 2.5f; // a burst uses small motes; a lone particle or a cloud stays chunky

			for ( var i = 0; i < count; i++ )
			{
				var proxy = new ModelEntity
				{
					Model = ramp[0],
					Scale = new Vector3( scale ),
					Position = position,
					Rotation = FacingCamera( position ),
				};
				particleProxies.Add( new ParticleProxy
				{
					Entity = proxy,
					Velocity = baseVel + RandomSpread() * SpreadSpeed,
					Acceleration = accel,
					Expire = lastNow + life,
					Born = lastNow,
					Ramp = ramp,
				} );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[particle] effect {effectCode} proxy failed: {e.Message}" );
		}
		Log.Info( $"[particle] effect {effectCode} ({name}) @{position}" );
	}

	// The decoded effect record for a code (for its spawn fields), or null if the library/effect is absent.
	private static ParticleEffect? EffectFor( int effectCode )
	{
		var lib = ParticleLib;
		return lib != null && effectCode >= 0 && effectCode < lib.Effects.Count ? lib.Effects[effectCode] : null;
	}

	// A random unit-ish vector biased gently upward, so a burst sprays outward + up rather than sideways only.
	private static Vector3 RandomSpread()
	{
		float a = (float)(Random.Shared.NextDouble() * Math.PI * 2);
		float r = (float)Random.Shared.NextDouble();
		return new Vector3( MathF.Cos( a ) * r, MathF.Sin( a ) * r, 0.4f + (float)Random.Shared.NextDouble() * 0.8f );
	}

	// The effect's name + a representative colour (its brightest ramp stop) from the decoded .PLB (T-019);
	// a white fallback if the library/effect isn't available.
	private static (string Name, byte R, byte G, byte B) ResolveEffectColour( int effectCode )
	{
		var lib = ParticleLib;
		if ( lib == null || effectCode < 0 || effectCode >= lib.Effects.Count )
			return ("?", 255, 255, 255);

		var fx = lib.Effects[effectCode];
		if ( fx.ColorRamp.Count == 0 )
			return (fx.Name, 255, 255, 255);

		var stop = fx.ColorRamp.OrderByDescending( c => c.R + c.G + c.B ).First();
		return (fx.Name, stop.R, stop.G, stop.B);
	}

	/// <summary>Drift live particles and drop the expired ones; <paramref name="now"/> is the game time in seconds.</summary>
	public void Tick( float now )
	{
		float dt = Math.Clamp( now - lastNow, 0f, 0.1f ); // integrate motion; clamp so a long stall can't fling them
		lastNow = now;
		for ( var i = particleProxies.Count - 1; i >= 0; i-- )
		{
			var p = particleProxies[i];
			if ( now >= p.Expire )
			{
				Entity.All.Remove( p.Entity );
				particleProxies.RemoveAt( i );
				continue;
			}
			// Drift the mote under its velocity + the effect's acceleration (gravity/buoyancy), keep it facing the
			// camera, and step through the colour ramp over its life.
			p.Velocity += p.Acceleration * dt;
			p.Entity.Position += p.Velocity * dt;
			p.Entity.Rotation = FacingCamera( p.Entity.Position );
			float age = (now - p.Born) / MathF.Max( 0.01f, p.Expire - p.Born );
			p.Entity.Model = p.Ramp[Math.Clamp( (int)(age * p.Ramp.Length), 0, p.Ramp.Length - 1 )];
		}
	}
}
