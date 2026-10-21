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
	/// <summary>A gift / balloon / costume shop: an impulse buy that lifts the mood (no need satisfied).</summary>
	Item,
	/// <summary>A litter bin (<c>UsageInfo.HoldsLitter</c>): a visitor near one bins its rubbish instead of dropping it.</summary>
	Bin,
}

/// <summary>What a sale was, for the per-product challenge counters (see <see cref="ShopBuilding.CategoryOf"/>).</summary>
public enum SaleCategory
{
	Snack, Fries, Burger, IceCream, Drink, Restaurant, Gift, Costume, Balloon,
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

	/// <summary>How filthy a toilet is (0 clean … 100): each visit dirties it, a handyman cleans it. At
	/// <see cref="DirtyFilth"/> it upsets and sickens its users (Standard.sam RegionFX "Dirty Toilet": illness) and the
	/// advisor complains (Advisor.sam FeaturesDirtyToilets*.FilthierThan 60 / 90).</summary>
	public float Filth { get; set; }

	// ---- Occupancy (toilets): Upgrades[0].InitCapacity cubicles; visitors queue for a free one ----

	/// <summary>How many visitors can use it at once (<c>Upgrades[0].InitCapacity</c>: a small toilet 1, a super toilet 5).</summary>
	public int Capacity => Stats?.Capacity ?? 1;

	/// <summary>Visitors using it right now.</summary>
	public int InUse { get; set; }

	/// <summary>Visitors waiting for a free place.</summary>
	public int Waiting { get; set; }

	/// <summary>Seconds a visitor spends in a toilet cubicle.</summary>
	public const float ToiletSeconds = 2f;

	/// <summary>The average toilet queue (FeaturesNotEnoughToilets.AverageQueueLongerThan 2.5).</summary>
	public static float AverageToiletQueue()
	{
		var toilets = Stalls.Where( s => s.Kind == ShopKind.Toilet ).ToList();
		return toilets.Count == 0 ? 0f : toilets.Average( s => (float)s.Waiting );
	}

	public const float FilthPerVisit = 1f, DirtyFilth = 60f, FilthyFilth = 90f;

	/// <summary>Whether a handyman should come and clean this toilet.</summary>
	public bool NeedsCleaning => Kind == ShopKind.Toilet && Filth >= CleanAtFilth;
	private const float CleanAtFilth = 30f;

	/// <summary>The park's toilets' average cleanliness, 0–100 (100 with no toilets) — challenge type 24.</summary>
	public static float ToiletCleanliness()
	{
		float sum = 0f;
		int n = 0;
		foreach ( var s in Stalls )
			if ( s.Kind == ShopKind.Toilet ) { sum += 100f - s.Filth; n++; }
		return n == 0 ? 100f : sum / n;
	}

	/// <summary>A free facility (toilet, litter bin): nothing to sell, so no price.</summary>
	public bool IsFree => Kind is ShopKind.Toilet or ShopKind.Bin;

	/// <summary>How close (world units, ~3 tiles) a visitor must be to a bin to use it rather than litter.</summary>
	public const float BinReach = 30f;

	/// <summary>Pieces of rubbish visitors put in bins rather than on the ground.</summary>
	public static int LitterBinned { get; private set; }

	/// <summary>A visitor gets rid of a piece of rubbish at <paramref name="position"/>: into a bin within
	/// <see cref="BinReach"/> if there is one, else onto the ground (<see cref="Litter.TryDrop"/>).</summary>
	public static void Discard( Vector3 position )
	{
		foreach ( var s in Stalls )
			if ( s.Kind == ShopKind.Bin && (s.Position - position).WithZ( 0 ).LengthSquared <= BinReach * BinReach )
			{
				LitterBinned++;
				return;
			}
		Litter.TryDrop( position );
	}

	/// <summary>Display name (for the manage UI): the stall's own <c>Info.Name</c> when its data loaded.</summary>
	public string Name => title ?? Kind switch { ShopKind.Drink => "Drink Stall", ShopKind.Toilet => "Toilet", ShopKind.Item => "Gift Shop", ShopKind.Bin => "Litter Bin", _ => "Food Stall" };
	private readonly string? title;

	/// <summary>What one sale costs the park in goods (<c>UsageInfo.InitCostOfGoods</c>); the profit is the price minus this.</summary>
	public float CostOfGoods { get; set; }

	// This month's trade and last month's (the shop window's "Customers / Takings / Profit last month").
	private int customers;
	private float takings, goods;
	public int CustomersLastMonth { get; private set; }
	public float TakingsLastMonth { get; private set; }
	public float ProfitLastMonth { get; private set; }

	/// <summary>A visitor bought something here.</summary>
	public void NoteSale( float price, float costOfGoods )
	{
		customers++;
		takings += price;
		goods += costOfGoods;
	}

	/// <summary>Close the month: this month's trade becomes last month's (every stall).</summary>
	public static void RollMonth()
	{
		foreach ( var s in Stalls )
		{
			s.CustomersLastMonth = s.customers;
			s.TakingsLastMonth = s.takings;
			s.ProfitLastMonth = s.takings - s.goods;
			s.customers = 0;
			s.takings = s.goods = 0f;
		}
	}

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

	/// <summary>The stall's archive (e.g. <c>levels/jungle/shops/fries</c>), or null for a generic stall of its kind.</summary>
	public string? Archive { get; }

	/// <summary>The stall's authored stats (type, effects…), when its data loaded.</summary>
	internal ShopBuilding.Stats? Stats { get; }

	/// <summary>What a sale here counts as (drink/snack by kind when the stall has no data).</summary>
	public SaleCategory Category => Stats is { } s ? ShopBuilding.CategoryOf( s ) : Kind == ShopKind.Drink ? SaleCategory.Drink : SaleCategory.Snack;

	/// <summary>The catalog key this stall was built from (saved, so a load rebuilds the same stall).</summary>
	public string CatalogKey { get; set; } = "shop";

	public Shop( ParkTerrain terrain, Vector3 position, ShopKind kind = ShopKind.Food, float footprintSize = 20f, float price = 8f, string? archive = null )
	{
		Price = price;
		Archive = archive ?? ShopBuilding.DefaultArchive( kind, Level.Current.Name );
		// The stall's own price / cost of goods / name / type from its .sam (a toilet stays free).
		Stats = Archive != null ? ShopBuilding.StatsOf( Archive ) : null;
		Kind = Stats is { } typed && kind is not (ShopKind.Toilet or ShopKind.Bin) ? ShopBuilding.KindOf( typed.ShopType ) : kind;
		if ( Stats is { } stats )
		{
			title = ObjectNames.Localize( stats.Name );
			if ( !IsFree )
			{
				Price = stats.Price;
				CostOfGoods = stats.CostOfGoods;
			}
		}
		Position = position.WithZ( terrain.SampleHeight( position.X, position.Y ) );

		// Prefer the theme's real stall/toilet building; keep the coloured billboard only as a fallback.
		if ( Archive != null && ShopBuilding.TryLoadArchive( Archive, Position, footprintSize, buildingParts ) )
		{
			isBuilding = true;
			Model = ModelFor( Kind );  // retained for the click/selection bounds, but rendered invisibly
			Scale = Vector3.Zero;      // the real building renders through buildingParts
		}
		else
		{
			Model = ModelFor( Kind );
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
			ShopKind.Item => Billboard.Make( 220, 120, 200 ),   // pink gifts
			ShopKind.Bin => Billboard.Make( 90, 90, 90 ),       // dark grey bin
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
