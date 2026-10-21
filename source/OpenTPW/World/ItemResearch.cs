using System.Globalization;

namespace OpenTPW;

/// <summary>
/// Researching new rides, sideshows, shops and features (T-044 follow-up), from the data: each item's <c>.sam</c> gives
/// its <c>Research.Group</c> (0 = available from the start, 1–4 later) and its research cost
/// (<c>Upgrades[0].CostOfResearch</c>, 450–1300 points). <c>Standard.sam</c> gates the groups —
/// <c>ResearchTech[g].PercentageForThisTech</c> (0, 0, 80, 85, 85): group g opens once that share of the items in the
/// groups below it is researched — and splits the scientists' work between the categories
/// (<c>ResearchCategories[c].Effort</c>: rides 100, shops 15, sideshows 30, features 15). Each category researches its
/// cheapest open item at a time. The scientists' rate is OpenTPW's: 1.5 points a second per grade-1 researcher
/// (<see cref="Staff.ResearchPower"/>). Pure (unit-tested); the level feeds it the catalog and the research power.
/// </summary>
public sealed class ItemResearch
{
	/// <summary>One researchable item: its catalog key, build category, group and research cost.</summary>
	public readonly record struct Item( string Key, BuildCategory Category, int Group, float Cost, string Title );

	public const float PointsPerResearcherSecond = 1.5f;

	/// <summary>The active level's research (null outside a park).</summary>
	public static ItemResearch? Current { get; set; }

	private readonly Dictionary<string, Item> items = new( StringComparer.OrdinalIgnoreCase );
	private readonly HashSet<string> unlocked = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<BuildCategory, float> progress = new();
	private readonly Dictionary<BuildCategory, float> effort;
	private readonly float[] techPercent;

	/// <summary>Raised when an item is researched (and so can be built).</summary>
	public event Action<Item>? Researched;

	public ItemResearch( IEnumerable<Item> catalog, IReadOnlyDictionary<BuildCategory, float>? effort = null, float[]? techPercent = null )
	{
		foreach ( var item in catalog )
		{
			items[item.Key] = item;
			if ( item.Group <= 0 || item.Cost <= 0f )
				unlocked.Add( item.Key );
		}
		this.effort = new Dictionary<BuildCategory, float>( effort ?? DefaultEffort );
		this.techPercent = techPercent ?? new float[] { 0, 0, 80, 85, 85 };
	}

	/// <summary>The shipped category efforts (<c>ResearchCategories[n].Effort</c>).</summary>
	public static IReadOnlyDictionary<BuildCategory, float> DefaultEffort { get; } = new Dictionary<BuildCategory, float>
	{
		[BuildCategory.Rides] = 100, [BuildCategory.Shops] = 15, [BuildCategory.Sideshows] = 30, [BuildCategory.Features] = 15,
	};

	/// <summary>Read the efforts and tech gates from a level's <c>Standard.sam</c>.</summary>
	public static (Dictionary<BuildCategory, float> Effort, float[] TechPercent) LoadSettings( SettingsFile sam )
	{
		float F( string key, float fallback ) => float.TryParse( sam[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ? v : fallback;
		var effort = new Dictionary<BuildCategory, float>
		{
			[BuildCategory.Rides] = F( "ResearchCategories[0].Effort", 100 ),
			[BuildCategory.Shops] = F( "ResearchCategories[1].Effort", 15 ),
			[BuildCategory.Sideshows] = F( "ResearchCategories[2].Effort", 30 ),
			[BuildCategory.Features] = F( "ResearchCategories[3].Effort", 15 ),
		};
		var tech = Enumerable.Range( 0, 5 ).Select( g => F( $"ResearchTech[{g}].PercentageForThisTech", g switch { 2 => 80, 3 or 4 => 85, _ => 0 } ) ).ToArray();
		return (effort, tech);
	}

	/// <summary>A category's share of the scientists' work (0–100; the research lab's sliders).</summary>
	public float EffortOf( BuildCategory category ) => effort.GetValueOrDefault( category );

	/// <summary>Set a category's effort, clamped to 0–100.</summary>
	public void SetEffort( BuildCategory category, float value ) => effort[category] = Math.Clamp( value, 0f, 100f );

	/// <summary>Whether an item can be built (researched, or never needed researching). Unknown keys can (staff…).</summary>
	public bool IsUnlocked( string key ) => !items.ContainsKey( key ) || unlocked.Contains( key );

	/// <summary>Every researched item's key (saved with the park).</summary>
	public IReadOnlyCollection<string> Unlocked => unlocked;

	/// <summary>How far along an item is (0–1) when it's its category's current target, else 0.</summary>
	public float ProgressOf( string key )
		=> items.TryGetValue( key, out var item ) && Target( item.Category ) is { } t && t.Key == item.Key && t.Cost > 0f
			? Math.Clamp( progress.GetValueOrDefault( item.Category ) / t.Cost, 0f, 1f ) : 0f;

	/// <summary>The highest group open for research: group g opens once <c>techPercent[g]</c> % of the items in the
	/// groups below it are researched.</summary>
	public int OpenGroup
	{
		get
		{
			int open = 0;
			for ( int g = 1; g < 8; g++ )
			{
				var below = items.Values.Where( i => i.Group < g ).ToList();
				float done = below.Count == 0 ? 100f : 100f * below.Count( i => unlocked.Contains( i.Key ) ) / below.Count;
				if ( done + 1e-3f < (g < techPercent.Length ? techPercent[g] : 100f) )
					break;
				open = g;
			}
			return open;
		}
	}

	/// <summary>What a category is researching now: its cheapest open, unresearched item (null when none).</summary>
	public Item? Target( BuildCategory category )
	{
		int open = OpenGroup;
		return items.Values.Where( i => i.Category == category && i.Group <= open && !unlocked.Contains( i.Key ) )
			.OrderBy( i => i.Group ).ThenBy( i => i.Cost ).ThenBy( i => i.Key ).Cast<Item?>().FirstOrDefault();
	}

	/// <summary>Whether any category has something left to research.</summary>
	public bool Busy => effort.Keys.Any( c => Target( c ) != null );

	/// <summary>Spend <paramref name="dt"/> seconds of <paramref name="researchPower"/> grade-1 researchers, split
	/// between the categories that have something to research by their effort.</summary>
	public void Tick( float dt, float researchPower )
	{
		if ( researchPower <= 0f || dt <= 0f )
			return;
		var active = effort.Where( kv => kv.Value > 0f && Target( kv.Key ) != null ).ToList();
		float total = active.Sum( kv => kv.Value );
		if ( total <= 0f )
			return;
		float points = researchPower * PointsPerResearcherSecond * dt;
		foreach ( var (category, share) in active )
		{
			var target = Target( category )!.Value;
			float p = progress.GetValueOrDefault( category ) + points * share / total;
			if ( p >= target.Cost )
			{
				unlocked.Add( target.Key );
				p = 0f;
				Researched?.Invoke( target );
			}
			progress[category] = p;
		}
	}

	/// <summary>Restore a save's researched items and the categories' progress.</summary>
	public void Restore( IEnumerable<string> researched, IReadOnlyDictionary<string, float>? categoryProgress = null )
	{
		foreach ( var key in researched )
			unlocked.Add( key );
		if ( categoryProgress != null )
			foreach ( var (name, value) in categoryProgress )
				if ( Enum.TryParse<BuildCategory>( name, out var c ) )
					progress[c] = value;
	}

	/// <summary>Each category's progress towards its target (saved with the park).</summary>
	public Dictionary<string, float> CategoryProgress => progress.ToDictionary( kv => kv.Key.ToString(), kv => kv.Value );

	/// <summary>Open everything (the dev park).</summary>
	public void UnlockAll()
	{
		foreach ( var key in items.Keys )
			unlocked.Add( key );
	}
}
