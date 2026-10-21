using System.Text;

namespace OpenTPW;

public static class Localization
{
	private static StringFile UIStrings;

	static Localization()
	{
		UIStrings = new StringFile( GameLanguage.File( "UITEXT.str" ) );
	}

	private class LocalizationParser : BaseParser
	{
		public LocalizationParser( string input ) : base( input ) { }

		public string Parse()
		{
			var sb = new StringBuilder();

			while ( !EndOfFile() )
			{
				sb.Append( ConsumeWhile( c => c != '#' ) );

				if ( !EndOfFile() )
				{
					ConsumeChar(); // #

					var key = ConsumeWhile( x => !char.IsWhiteSpace( x ) );
					key = key.Trim();

					var enumVal = Enum.Parse<UIStrings>( key );
					sb.Append( UIStrings[(int)enumVal] );
				}
			}

			return sb.ToString();
		}
	}

	/// <summary>One UITEXT.str entry in the current language (<see cref="GameLanguage"/>), trimmed; the
	/// <paramref name="fallback"/> when the entry is missing or blank.</summary>
	public static string Get( UIStrings key, string fallback = "" )
	{
		int i = (int)key;
		var text = i >= 0 && i < UIStrings.Entries.Length ? UIStrings.Entries[i]?.Trim() : null;
		return string.IsNullOrEmpty( text ) ? fallback : text;
	}

	public static string Parse( string str )
	{
		var parser = new LocalizationParser( str );
		return parser.Parse();
	}
}
