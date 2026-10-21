namespace OpenTPW;

/// <summary>What a stall sells — and so which peep need it satisfies (T-039).</summary>
public enum ShopKind
{
	/// <summary>A food stall: satisfies hunger.</summary>
	Food,
	/// <summary>A drink stall: satisfies thirst.</summary>
	Drink,
	/// <summary>A toilet: relieves the bladder (a free facility — no income).</summary>
	Toilet,
}

/// <summary>
/// A food/drink stall. A hungry or thirsty visitor detours to the nearest stall of the matching
/// <see cref="ShopKind"/>, pays (park income) and has that need satisfied. Rendered as the theme's real stall
/// building (<see cref="ShopBuilding"/>), falling back to a coloured billboard (green = food, blue = drink,
/// grey = toilet) when no model resolves; tracked in <see cref="Stalls"/> so peeps can find the closest cheaply.
/// </summary>
public sealed class Shop : ModelEntity
{
	/// <summary>Every shop in the park.</summary>
	public static readonly List<Shop> Stalls = new();

	/// <summary>What a snack/drink costs (park income when bought) — settable by the player (T-041), like a
	/// ride's ticket price. A toilet stays free (its price is unused).</summary>
	public float Price { get; set; }

	/// <summary>What this stall sells (which need it satisfies).</summary>
	public ShopKind Kind { get; }

	/// <summary>Display name (for the manage UI): the stall's own <c>Info.Name</c> when its data loaded.</summary>
	public string Name => title ?? Kind switch { ShopKind.Drink => "Drink Stall", ShopKind.Toilet => "Toilet", _ => "Food Stall" };
	private readonly string? title;

	/// <summary>What one sale costs the park in goods (<c>UsageInfo.InitCostOfGoods</c>); the profit is the price minus this.</summary>
	public float CostOfGoods { get; set; }

	/// <summary>Grid footprint (set when placed) — used to select it by clicking a covered tile, and to
	/// free its cells on sell/demolish (T-041).</summary>
	public int TileX { get; set; }
	public int TileY { get; set; }
	public int TileW { get; set; }
	public int TileH { get; set; }

	/// <summary>What the player paid to build this stall — used for the sell refund (T-041).</summary>
	public float BuildCost { get; set; }

	/// <summary>True if the grid tile (tx,ty) is within this stall's footprint.</summary>
	public bool Covers( int tx, int ty ) => tx >= TileX && tx < TileX + TileW && ty >= TileY && ty < TileY + TileH;

	/// <summary>Remove this stall from the world (sold/demolished) — caller frees its grid cells + refunds.</summary>
	public void Despawn()
	{
		Stalls.Remove( this );
		foreach ( var p in buildingParts )
			Entity.All.Remove( p );
		buildingParts.Clear();
		Entity.All.Remove( this );
	}

	private static readonly Dictionary<ShopKind, Model> sharedModels = new();

	// The real building's per-mesh entities when the theme model loaded (empty when it fell back to the
	// billboard) — removed on demolish. A building has a fixed orientation, so it doesn't face the camera.
	private readonly List<ModelEntity> buildingParts = new();
	private readonly bool isBuilding;

	public Shop( ParkTerrain terrain, Vector3 position, ShopKind kind = ShopKind.Food, float footprintSize = 20f, float price = 8f )
	{
		Price = price;
		Kind = kind;
		// The stall's own price / cost of goods / name from its .sam (a toilet stays free).
		if ( ShopBuilding.StatsFor( kind, Level.Current.Name ) is { } stats )
		{
			title = stats.Name;
			if ( kind != ShopKind.Toilet )
			{
				Price = stats.Price;
				CostOfGoods = stats.CostOfGoods;
			}
		}
		Position = position.WithZ( terrain.SampleHeight( position.X, position.Y ) );

		// Prefer the theme's real stall/toilet building; keep the coloured billboard only as a fallback.
		if ( ShopBuilding.TryLoad( kind, Position, footprintSize, buildingParts ) )
		{
			isBuilding = true;
			Model = ModelFor( kind );  // retained for the click/selection bounds, but rendered invisibly
			Scale = Vector3.Zero;      // the real building renders through buildingParts
		}
		else
		{
			Model = ModelFor( kind );
			Scale = new Vector3( 9f, 1f, 12f ); // bigger than a peep so a stall reads as a building
		}

		Stalls.Add( this );
	}

	// The closest stall of a given kind to a point, or null if the park has none of that kind.
	public static Shop? Nearest( ShopKind kind, Vector3 from )
	{
		Shop? best = null;
		float bestD2 = float.MaxValue;
		foreach ( var s in Stalls )
		{
			if ( s.Kind != kind )
				continue;
			float dx = s.Position.X - from.X, dy = s.Position.Y - from.Y;
			float d2 = dx * dx + dy * dy;
			if ( d2 < bestD2 )
			{
				bestD2 = d2;
				best = s;
			}
		}
		return best;
	}

	private static Model ModelFor( ShopKind kind )
	{
		if ( sharedModels.TryGetValue( kind, out var m ) )
			return m;
		var model = kind switch
		{
			ShopKind.Drink => Billboard.Make( 50, 130, 220 ),   // blue
			ShopKind.Toilet => Billboard.Make( 210, 210, 220 ), // pale grey/white
			_ => Billboard.Make( 40, 170, 70 ),                 // green food
		};
		sharedModels[kind] = model;
		return model;
	}

	protected override void OnUpdate()
	{
		// A real building has a fixed authored orientation; only the fallback billboard faces the camera.
		if ( isBuilding )
			return;

		// Stalls face the camera like the other billboards.
		var cam = Camera.Position;
		float yaw = MathF.Atan2( -(cam.X - Position.X), cam.Y - Position.Y );
		Rotation = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, yaw );
	}
}
