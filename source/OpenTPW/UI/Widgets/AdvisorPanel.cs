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
		Graphics.DrawText( Font, tip, 236f, 106f, TextAlign.Left, 1.3f );

		// Faint lip-sync diagnostic (clip + viseme), for debugging the speech path — with the debug overlay only (T-065).
		if ( !GameSettings.Current.ShowDebug )
			return;
		var part = advisor.CurrentShape.MeshPartName() ?? "(closed)";
		Graphics.DrawText( Font, $"{advisor.ClipName} {advisor.Elapsed:0.0}/{advisor.ClipLength:0.0}s  {advisor.CurrentShape} [{part}]",
			236f, 84f, TextAlign.Left, 0.8f );
	}
}
