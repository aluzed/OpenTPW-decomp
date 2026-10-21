namespace OpenTPW;

/// <summary>
/// Handles all debug logging functionality.
/// </summary>
public class Logger
{
	public enum Level
	{
		Trace,
		Info,
		Warning,
		Error
	};

	/// <summary>Whether <see cref="Trace"/> lines are written: off unless <c>OPENTPW_TRACE=1</c>. The ride engine traces
	/// every script event and animation, which buried the useful lines (thousands per minute).</summary>
	public static bool TraceEnabled { get; set; } = Environment.GetEnvironmentVariable( "OPENTPW_TRACE" ) == "1";

	public void Trace( object obj )
	{
		if ( TraceEnabled )
			Log( obj?.ToString(), Level.Trace );
	}
	public void Info( object obj ) => Log( obj?.ToString(), Level.Info );
	public void Info( object obj, bool quiet = false ) => Log( obj?.ToString(), Level.Info, quiet );
	public void Warning( object obj ) => Log( obj?.ToString(), Level.Warning );
	public void Error( object obj ) => Log( obj?.ToString(), Level.Error );

	public delegate void LogDelegate( Level severity, string logText );
	public static LogDelegate? OnLog;
	public static LogDelegate? QuietLog;

	private static void Log( string? str, Level severity = Level.Trace, bool quiet = false )
	{
		if ( str == null )
			return;

		if( quiet )
			QuietLog?.Invoke( severity, str );

		Console.ForegroundColor = SeverityToConsoleColor( severity );
		Console.WriteLine( $"[{DateTime.Now.ToLongTimeString()}] {str}" );

		OnLog?.Invoke( severity, str );
	}

	private static ConsoleColor SeverityToConsoleColor( Level severity ) => severity switch
	{
		Level.Error => ConsoleColor.DarkRed,
		Level.Warning => ConsoleColor.Red,
		Level.Trace => ConsoleColor.DarkGray,
		_ => ConsoleColor.White,
	};
}
