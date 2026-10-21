using OpenTPW.UI;

namespace OpenTPW;

/// <summary>
/// The advisor's on-screen tip (T-046): while the bug head speaks, this shows the <b>readable advice</b> the
/// rule-engine raised (<see cref="AdvisorTips"/>) in a small speech panel, so the advisor is actually useful
/// rather than a mute talking head. A faint second line keeps the lip-sync diagnostics. The 3D character is
/// the <see cref="Advisor"/> world entity.
/// </summary>
internal sealed class AdvisorPanel : HudPanel
{
	private static Texture? bg;
	private static Texture Bg => bg ??= Solid( 16, 16, 26, 200 );

	protected override void OnRender()
	{
		if ( Advisor.Current is not { } advisor || string.IsNullOrEmpty( advisor.ClipName ) )
			return;

		var tip = AdvisorTips.TextFor( advisor.ActiveMessage );

		// A speech panel low on the screen, under the talking head.
		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( new Rectangle( 220f, 92f, 840f, 56f ), mat );

		Graphics.DrawText( Font, "ADVISOR", 236f, 128f, TextAlign.Left, 1.2f );
		// A message wider than the panel (the game's own lines run long) wraps onto a second, smaller line.
		const float TipScale = 1.3f, MaxWidth = 808f;
		int width = Font.Atlas.Measure( tip );
		int split = tip.Length / 2;
		if ( width * TipScale > MaxWidth && tip.LastIndexOf( ' ', split ) is var sp and > 0 )
		{
			float scale = MathF.Min( 1.1f, MaxWidth / MathF.Max( 1f, MathF.Max( Font.Atlas.Measure( tip[..sp] ), Font.Atlas.Measure( tip[(sp + 1)..] ) ) ) );
			Graphics.DrawText( Font, tip[..sp], 236f, 109f, TextAlign.Left, scale );
			Graphics.DrawText( Font, tip[(sp + 1)..], 236f, 95f, TextAlign.Left, scale );
		}
		else
			Graphics.DrawText( Font, tip, 236f, 106f, TextAlign.Left, TipScale );

		// Faint lip-sync diagnostic (clip + viseme), for debugging the speech path — with the debug overlay only (T-065).
		if ( !GameSettings.Current.ShowDebug )
			return;
		var part = advisor.CurrentShape.MeshPartName() ?? "(closed)";
		Graphics.DrawText( Font, $"{advisor.ClipName} {advisor.Elapsed:0.0}/{advisor.ClipLength:0.0}s  {advisor.CurrentShape} [{part}]",
			236f, 84f, TextAlign.Left, 0.8f );
	}
}
