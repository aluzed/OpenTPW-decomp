namespace OpenTPW;

/// <summary>
/// Loads a shop's real building model from the active theme's WAD (a food/drink stall under <c>shops/</c>,
/// a toilet under <c>features/</c>) as a set of textured child entities — so a stall reads as a building
/// instead of a flat coloured billboard. TPW ships several stalls per theme under different names; the first
/// candidate that loads is used. Returns false (leaving the caller on its billboard) when none resolve, so a
/// theme without a matching model still renders something.
/// </summary>
internal static class ShopBuilding
{
	// Candidate archive paths per kind for a theme, tried in order. Food/drink stalls live under shops/ and are
	// authored corner-anchored at their own scale, so BuildParts re-centres + fits each to its footprint. Toilets
	// are a "feature" under features/: a base plate, a door and a booth (J_WC01, ~8×7×10). The booth looked missing
	// — its textures (gk_roof5, GK_side2) live in the level's sharetex.wad, and a texture missing from the toilet
	// WAD used to load as a chroma-keyed (invisible) placeholder instead of falling back (T-073).
	private static IEnumerable<string> Candidates( ShopKind kind, string theme ) => kind switch
	{
		ShopKind.Drink => new[] { "coconut", "drinks", "pumpkin", "cola" }.Select( n => $"levels/{theme}/shops/{n}" ),
		// Each theme names its small toilet differently: jungle toilet, fantasy/space loo, hallow horloo.
		ShopKind.Toilet => new[] { "toilet", "loo", "horloo" }.Select( n => $"levels/{theme}/features/{n}" ),
		// And its litter bin: jungle pelbin (a pelican), hallow bin, fantasy bfbin, space spacbin.
		ShopKind.Bin => new[] { "pelbin", "bin", "bfbin", "spacbin" }.Select( n => $"levels/{theme}/features/{n}" ),
		_ => new[] { "burger", "fries", "steak", "hotdog", "sburger", "gchips" }.Select( n => $"levels/{theme}/shops/{n}" ),
	};

	/// <summary>A stall's authored data from its <c>.sam</c> (e.g. jungle Burger Shop: type 2, price 30, cost of goods
	/// 20, build 800, hunger −25, happiness +5, litter 50%): <c>Info.Name</c>, <c>UsageInfo.ShopType</c>
	/// (1 gifts, 2 snacks, 3 restaurant, 4 drinks, 5 costumes, 6 balloons), <c>UsageInfo.InitPricePerUse</c>,
	/// <c>UsageInfo.InitCostOfGoods</c>, the first level's <c>Upgrades[0].CostOfUpgrade</c> (the build price, as for
	/// rides) and the <c>UsageInfo.*Effect</c> values a purchase has on the visitor.</summary>
	public readonly record struct Stats( string Name, float Price, float CostOfGoods, float BuildCost,
		int ShopType = 2, float HungerEffect = 0f, float ThirstEffect = 0f, float HappinessEffect = 0f,
		float FatigueEffect = 0f, float LitterEffect = 0f );

	/// <summary>Which need a <c>UsageInfo.ShopType</c> serves: 2 snacks / 3 restaurant → food, 4 → drinks, 1 gifts /
	/// 5 costumes / 6 balloons → impulse items.</summary>
	public static ShopKind KindOf( int shopType ) => shopType switch
	{
		4 => ShopKind.Drink,
		1 or 5 or 6 => ShopKind.Item,
		_ => ShopKind.Food,
	};

	/// <summary>What a stall sells, for the per-product counters the challenges measure (T-054): the shop type
	/// splits drinks / gifts / costumes / balloons / restaurant meals; snacks (type 2) are told apart by their
	/// authored (English, pre-localization) <c>Info.Name</c> — every theme names its fries, burger and ice-cream
	/// stalls "Fries Shop" / "Burger Shop" / "Ice Cream Shop" whatever the model (hallow gchips, fantasy sburger…).</summary>
	public static SaleCategory CategoryOf( Stats stats ) => stats.ShopType switch
	{
		1 => SaleCategory.Gift,
		3 => SaleCategory.Restaurant,
		4 => SaleCategory.Drink,
		5 => SaleCategory.Costume,
		6 => SaleCategory.Balloon,
		_ => stats.Name switch
		{
			"Fries Shop" => SaleCategory.Fries,
			"Burger Shop" => SaleCategory.Burger,
			"Ice Cream Shop" => SaleCategory.IceCream,
			_ => SaleCategory.Snack,
		},
	};

	/// <summary>A visitor's needs/mood after buying from a stall with <paramref name="stats"/> (pure). The authored
	/// effects are on the original's scale, where a burger's 25 hunger or a drink's 40 thirst sates the visitor:
	/// those relieve the full detour threshold, smaller items proportionally less (fries 20 → 80 %, an ice cream's
	/// 15 hunger + 5 thirst → 60 % + 12.5 %). Happiness rises by its effect; <c>FatigueEffect</c> restores energy.</summary>
	public static (float Hunger, float Thirst, float Happiness, float Energy) ApplyEffects( Stats stats,
		float hunger, float thirst, float happiness, float energy, float hungerThreshold, float thirstThreshold, float maxEnergy )
	{
		const float FullMeal = 25f, FullDrink = 40f;
		return (
			MathF.Max( 0f, hunger - stats.HungerEffect / FullMeal * hungerThreshold ),
			MathF.Max( 0f, thirst - stats.ThirstEffect / FullDrink * thirstThreshold ),
			MathF.Min( 100f, happiness + stats.HappinessEffect ),
			MathF.Min( maxEnergy, energy + stats.FatigueEffect / 100f * maxEnergy ) );
	}

	/// <summary>Every stall archive a theme ships under <c>shops/</c> (sorted), e.g. jungle balloon, burger, coconut…</summary>
	public static IReadOnlyList<string> ShopArchives( string theme )
	{
		var dir = $"levels/{theme}/shops";
		try
		{
			return FileSystem.GetDirectories( dir ).Select( d => $"{dir}/{Path.GetFileName( d )}" )
				.OrderBy( a => a, StringComparer.OrdinalIgnoreCase ).ToList();
		}
		catch { return Array.Empty<string>(); }
	}

	/// <summary>The theme's big toilet feature ("Super Toilet": space loo_big, fantasy royaloo, hallow horsuloo — jungle
	/// has none), or null.</summary>
	public static string? BigToiletArchive( string theme ) =>
		new[] { "loo_big", "royaloo", "horsuloo" }.Select( n => $"levels/{theme}/features/{n}" ).FirstOrDefault( a => StatsOf( a ) != null );

	/// <summary>The first stall archive of a kind this theme ships (the generic "shop"/"drink"/"toilet" items), or null.</summary>
	public static string? DefaultArchive( ShopKind kind, string theme )
	{
		// The preferred names first, then any stall of that kind the theme ships (a toilet is a feature, not a shop).
		bool feature = kind is ShopKind.Toilet or ShopKind.Bin;
		foreach ( var archive in Candidates( kind, theme ).Concat( feature ? [] : ShopArchives( theme ) ) )
			if ( StatsOf( archive ) is { } st && (feature || KindOf( st.ShopType ) == kind) )
				return archive;
		return null;
	}

	/// <summary>The stats of the first <paramref name="kind"/> stall the theme ships, or null (the caller keeps its
	/// defaults).</summary>
	public static Stats? StatsFor( ShopKind kind, string theme ) =>
		DefaultArchive( kind, theme ) is { } archive ? StatsOf( archive ) : null;

	private static readonly Dictionary<string, Stats?> statsCache = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The stats in a stall archive's <c>.sam</c> (cached), or null when it has none.</summary>
	public static Stats? StatsOf( string archive )
	{
		if ( statsCache.TryGetValue( archive, out var cached ) )
			return cached;
		Stats? result = null;
		var name = Path.GetFileName( archive );
		try
		{
			// The .sam's case varies per stall (Burger.sam, Toilet.sam): look it up in the archive's listing.
			var samPath = FileSystem.GetFiles( archive )
				.FirstOrDefault( f => Path.GetFileName( f ).Equals( $"{name}.sam", StringComparison.OrdinalIgnoreCase ) );
			if ( samPath != null )
			{
				using var stream = FileSystem.OpenRead( samPath );
				result = ParseStats( new SettingsFile( stream ), name );
			}
		}
		catch { /* no such archive / unreadable */ }
		statsCache[archive] = result;
		return result;
	}

	/// <summary>Pure parse of <see cref="Stats"/> from a stall's settings (fallbacks: snacks, price 8, no goods cost,
	/// build 500, no effects).</summary>
	public static Stats ParseStats( SettingsFile sam, string fallbackName )
	{
		float F( string key, float fallback ) =>
			float.TryParse( sam[key], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v ) ? v : fallback;
		var title = sam["Info.Name"];
		return new Stats(
			string.IsNullOrWhiteSpace( title ) ? fallbackName : title,
			F( "UsageInfo.InitPricePerUse", 8f ),
			F( "UsageInfo.InitCostOfGoods", 0f ),
			F( "Upgrades[0].CostOfUpgrade", 500f ),
			(int)F( "UsageInfo.ShopType", 2f ),
			F( "UsageInfo.HungerEffect", 0f ),
			F( "UsageInfo.ThirstEffect", 0f ),
			F( "UsageInfo.HappinessEffect", 0f ),
			F( "UsageInfo.FatigueEffect", 0f ),
			F( "UsageInfo.LitterEffect", 0f ) );
	}

	/// <summary>Try to build the real shop model centred on <paramref name="origin"/> and scaled to fill a
	/// <paramref name="footprintSize"/>-wide cell, appending its per-mesh entities to <paramref name="parts"/>
	/// (the caller removes them on demolish). True if a model loaded.</summary>
	public static bool TryLoad( ShopKind kind, Vector3 origin, float footprintSize, List<ModelEntity> parts )
	{
		foreach ( var archive in Candidates( kind, Level.Current.Name ) )
			if ( TryLoadArchive( archive, origin, footprintSize, parts ) )
				return true;
		return false;
	}

	/// <summary>Build the model of one specific stall archive (see <see cref="TryLoad"/>).</summary>
	public static bool TryLoadArchive( string archive, Vector3 origin, float footprintSize, List<ModelEntity> parts )
	{
		var name = Path.GetFileName( archive );
		try
		{
			var modelFile = new ModelFile( $"{archive}/{name}.md2" );
			if ( modelFile.Meshes.Count == 0 )
				return false;
			BuildParts( modelFile, archive, origin, footprintSize, parts );
			if ( parts.Count > 0 )
			{
				Log.Info( $"[shop] building '{name}' ({parts.Count} mesh(es))" );
				return true;
			}
		}
		catch { /* no model */ }
		return false;
	}

	// One textured ModelEntity per mesh, Y/Z-swizzled (the LobbyIsland / Ride pattern), then re-centred on the
	// model's ground base-plate (mesh 0) and uniformly scaled so that plate fills the stall footprint — TPW models
	// are authored corner-anchored at varying scales, so without this a small model (a toilet) sits in a corner of
	// its cell at a fraction of the size. Textures load through the shared ride loader (stexture/, chroma-keyed).
	private static void BuildParts( ModelFile modelFile, string archive, Vector3 origin, float footprintSize, List<ModelEntity> parts )
	{
		var meshes = modelFile.Meshes;

		// The base-plate (mesh 0) is the model's ground tile = its footprint. Measure its XY span (in swizzled
		// model space: model-X = raw X, model-Y = raw Z) to get the centre to pull to origin and the scale to fit.
		float minx = 1e9f, maxx = -1e9f, miny = 1e9f, maxy = -1e9f;
		if ( meshes.Count > 0 )
		{
			System.Numerics.Matrix4x4.Decompose( meshes[0].TransformMatrix, out var bscl, out _, out var bpos );
			foreach ( var raw in meshes[0].Vertices )
			{
				float x = bpos.X + bscl.X * raw.Position.X;
				float y = bpos.Z + bscl.Z * raw.Position.Z;
				minx = MathF.Min( minx, x ); maxx = MathF.Max( maxx, x );
				miny = MathF.Min( miny, y ); maxy = MathF.Max( maxy, y );
			}
		}
		float baseSize = MathF.Max( maxx - minx, maxy - miny );
		float fit = baseSize > 1e-2f && footprintSize > 0f ? footprintSize / baseSize : 1f;
		var centre = new Vector3( (minx + maxx) / 2f, (miny + maxy) / 2f, 0f );

		foreach ( var mesh in meshes )
		{
			var textures = mesh.Materials.Select( m => Ride.LoadRideTexture( archive, m.Name ) ).ToList();

			var vertices = new List<Vertex>( mesh.Vertices.Length );
			for ( int i = 0; i < mesh.Vertices.Length; ++i )
				vertices.Add( new Vertex
				{
					Position = new Vector3( mesh.Vertices[i].Position.X, mesh.Vertices[i].Position.Z, mesh.Vertices[i].Position.Y ),
					Normal = mesh.Normals[i],
					TexCoords = mesh.TexCoords[i],
					TexIndex = (int)mesh.Vertices[i].TextureIndex,
					MatFlags = mesh.Materials[(int)mesh.Vertices[i].TextureIndex].Flags,
				} );

			System.Numerics.Matrix4x4.Decompose( mesh.TransformMatrix, out var scl, out var rot, out var pos );
			var spos = new Vector3( pos.X, pos.Z, pos.Y );
			foreach ( var model in MaterialSlots.BuildModels( [.. vertices], mesh.Indices, textures,
				() => new Material<ObjectUniformBuffer>( "content/shaders/test.shader" ) ) )
				parts.Add( new ModelEntity
				{
					Model = model,
					Scale = new Vector3( scl.X, scl.Z, scl.Y ) * fit,
					Rotation = new System.Numerics.Quaternion( rot.X, rot.Z, rot.Y, -rot.W ),
					Position = origin + (spos - centre) * fit,
				} );
		}
	}
}
