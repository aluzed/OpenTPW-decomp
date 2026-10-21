namespace OpenTPW;

/// <summary>
/// A piece of dropped litter lying on the ground: one of the game's litter sprites (<c>esprites/Generic/Litter/SPR_LI</c>
/// — a wrapper, a cup, a can…; frames 6/7 are sick puddles, not dropped here), drawn like a visitor's sprite (camera-facing,
/// anchored at its hotspot, the kids' pixel scale), else a small brown quad. Visitors drop it as they wander;
/// nearby litter sours their mood, and handymen (see <see cref="Staff"/>) roam to pick it up. Tracked in
/// <see cref="All"/> so both peeps and handymen can find it cheaply without scanning every entity.
/// </summary>
public sealed class Litter : ModelEntity
{
	/// <summary>Every piece of litter currently on the ground.</summary>
	public static readonly List<Litter> Active = new();

	private static Model? sharedModel;

	/// <summary>The most litter the park keeps on the ground. Past this a neglected park is already as messy as it
	/// gets; capping it keeps a long session's per-frame litter checks (every visitor scans the litter near it)
	/// bounded instead of growing for ever.</summary>
	public const int MaxPieces = 300;

	/// <summary>Drop a piece of litter at <paramref name="groundPos"/> unless the park is already at
	/// <see cref="MaxPieces"/>. Returns whether one was dropped.</summary>
	public static bool TryDrop( Vector3 groundPos )
	{
		if ( Active.Count >= MaxPieces )
			return false;
		_ = new Litter( groundPos );
		return true;
	}

	private Litter( Vector3 groundPos )
	{
		if ( Sheet() is { } sheet )
		{
			Model = sheet.FrameModel( Random.Shared.Next( Math.Min( DryLitterFrames, sheet.FrameCount ) ) );
			Position = groundPos;
			Scale = new Vector3( SpriteScale, 1f, SpriteScale );
			isSprite = true;
			FaceCamera();
		}
		else
		{
			Model = SharedModel();
			Position = groundPos.WithZ( groundPos.Z + 0.2f ); // just above the terrain to avoid z-fighting
			Scale = new Vector3( 1.5f );
		}
		Active.Add( this );
	}

	// The litter sheet's first six frames are dropped rubbish; the last two are sick puddles.
	private const int DryLitterFrames = 6;
	// World units per sprite pixel: a visitor (~10.4 units) is drawn from a ~43 px tall frame.
	private const float SpriteScale = 10.4f / 43f;
	private readonly bool isSprite;
	private static bool sheetTried;
	private static SpriteSheet? sheet;

	private static SpriteSheet? Sheet()
	{
		if ( !sheetTried )
		{
			sheetTried = true;
			sheet = SpriteSheet.Load( "esprites/Generic/Litter", "SPR_LI" );
		}
		return sheet;
	}

	protected override void OnUpdate()
	{
		if ( isSprite )
			FaceCamera();
	}

	private void FaceCamera()
	{
		var cam = Camera.Position;
		float yaw = MathF.Atan2( -(cam.X - Position.X), cam.Y - Position.Y );
		Rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );
	}

	/// <summary>Remove this litter from the world (a handyman cleaned it up).</summary>
	public void PickUp()
	{
		Active.Remove( this );
		Entity.All.Remove( this );
	}

	// A small brown ground quad, shared so dropping litter allocates nothing extra.
	private static Model SharedModel()
	{
		if ( sharedModel != null )
			return sharedModel;

		var material = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader", MaterialFlags.DoubleSided );
		material.Set( "Color", new Texture( [120, 90, 60, 255], 1, 1 ) ); // muddy brown
		sharedModel = Primitives.Plane.GenerateModel( material );
		return sharedModel;
	}
}
