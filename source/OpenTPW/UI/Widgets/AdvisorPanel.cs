using OpenTPW.UI;

namespace OpenTPW;

/// <summary>
/// The advisor's lip-sync diagnostics (debug overlay only). What he says is shown in <see cref="ParkHud"/>'s hint
/// strip, as in the original, which has no speech box. The 3D character is the <see cref="Advisor"/> world entity.
/// </summary>
internal sealed class AdvisorPanel : HudPanel
{
	private static Texture? bg;
	private static Texture Bg => bg ??= Solid( 16, 16, 26, 200 );

	protected override void OnRender()
	{
		if ( Advisor.Current is not { } advisor || string.IsNullOrEmpty( advisor.ClipName ) )
			return;

		// What he says goes to the hint strip (ParkHud), as in the original; this only keeps the debug line.
		// Faint lip-sync diagnostic (clip + viseme), for debugging the speech path — with the debug overlay only (T-065).
		if ( !GameSettings.Current.ShowDebug )
			return;
		var mat = Material.UI;
		mat.Set( "Color", Bg );
		Graphics.Quad( new Rectangle( 228f, 120f, 520f, 20f ), mat );
		var part = advisor.CurrentShape.MeshPartName() ?? "(closed)";
		Graphics.DrawText( Font, $"{advisor.ClipName} {advisor.Elapsed:0.0}/{advisor.ClipLength:0.0}s  {advisor.CurrentShape} [{part}]",
			236f, 138f, TextAlign.Left, 0.8f );
	}
}
