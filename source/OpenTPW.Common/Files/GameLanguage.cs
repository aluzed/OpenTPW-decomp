namespace OpenTPW;

/// <summary>
/// The game's text language. The disc ships one folder per language at its root (<c>English/</c>, <c>French/</c>,
/// <c>German/</c>…), each mirroring <c>data/</c> with the language's strings + fonts (<c>Language/&lt;name&gt;/</c>) and voice
/// (<c>global/speech</c>, <c>levels/*/speech</c>); the installer copied the chosen one over <c>data/</c>. OpenTPW reads it as
/// an overlay (<see cref="BaseFileSystem.Overlay"/>) and resolves language files through <see cref="File"/>.
/// </summary>
public static class GameLanguage
{
	/// <summary>The language name, as its folder is called (English, French, German, Danish, Swedish, Dutch…).</summary>
	public static string Name { get; set; } = "English";

	/// <summary>The VFS path of a language file, e.g. <c>Language/French/THOUGHTS.str</c>.</summary>
	public static string File( string name ) => $"Language/{Name}/{name}";
}
