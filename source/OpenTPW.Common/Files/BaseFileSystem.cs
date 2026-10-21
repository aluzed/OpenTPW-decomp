using System.Text;

namespace OpenTPW;

public class BaseFileSystem
{
	private readonly string basePath;
	// Case-insensitive so a ".WAD" file on disc matches a ".wad" handler (T-014).
	private readonly Dictionary<string, Type> archiveHandlers = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, IArchive> archiveCache = new();

	// Path resolution is memoized: every read inside a WAD used to re-list directories per path segment (both for
	// the case-insensitive match and for each archive-prefix probe) — ~7 s of a park load. Game data doesn't change
	// while running; a miss resolves to the requested path itself, which stays right if that file is created later.
	private readonly Dictionary<string, string> resolvedCase = new();
	private readonly Dictionary<string, (string ArchivePath, string InternalPath)> resolvedArchive = new();

	public BaseFileSystem( string relativePath )
	{
		if ( !Directory.Exists( relativePath ) )
			Directory.CreateDirectory( relativePath );

		basePath = Path.GetFullPath( relativePath, Directory.GetCurrentDirectory() );
	}

	public void RegisterArchiveHandler<T>( string extension ) where T : IArchive
	{
		archiveHandlers[extension] = typeof( T );
	}

	public string ReadAllText( string relativePath )
	{
		using var stream = OpenRead( relativePath );
		using var reader = new StreamReader( stream, Encoding.ASCII );
		return reader.ReadToEnd();
	}

	public byte[] ReadAllBytes( string relativePath )
	{
		using var stream = OpenRead( relativePath );
		using var ms = new MemoryStream();
		stream.CopyTo( ms );
		return ms.ToArray();
	}

	public bool FileExists( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		return File.Exists( absolutePath );
	}

	public bool DirectoryExists( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		return Directory.Exists( absolutePath );
	}

	public Stream OpenWrite( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		var (archivePath, _) = FindArchivePath( absolutePath );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			throw new NotImplementedException( "Can't write to archives" );
		}

		string? directoryName = Path.GetDirectoryName( absolutePath );
		if ( !Directory.Exists( directoryName ) )
			Directory.CreateDirectory( directoryName! );

		return File.OpenWrite( absolutePath );
	}

	public Stream OpenRead( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		var (archivePath, internalPath) = FindArchivePath( absolutePath );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			var archive = GetArchive( archivePath );
			return archive?.OpenFile( internalPath )!;
		}

		return File.Open( absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read );
	}

	public long GetSize( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		var (archivePath, internalPath) = FindArchivePath( absolutePath );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			var archive = GetArchive( archivePath );
			return archive?.GetFileSize( internalPath ) ?? 0L;
		}

		return new FileInfo( absolutePath ).Length;
	}

	public DateTime GetModifiedTime( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		var (archivePath, internalPath) = FindArchivePath( absolutePath );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			var archive = GetArchive( archivePath );
			return archive?.GetModifiedTime() ?? DateTime.UnixEpoch;
		}

		return new FileInfo( absolutePath ).LastWriteTime;
	}

	public string[] GetFiles( string relativePath )
	{
		return GetFileSystemEntries( relativePath, false );
	}

	public string[] GetDirectories( string relativePath )
	{
		return GetFileSystemEntries( relativePath, true );
	}

	private string[] GetFileSystemEntries( string relativePath, bool directories )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		var (archivePath, internalPath) = FindArchivePath( absolutePath );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			var archive = GetArchive( archivePath );
			var entries = directories ? archive.GetDirectories( internalPath ) : archive.GetFiles( internalPath );
			return entries.Select( entry => Path.Combine( relativePath, entry ) ).ToArray();
		}

		if ( directories )
		{
			var fileSystemDirectories = Directory.GetDirectories( absolutePath );
			var fileSystemArchives = Directory.GetFiles( absolutePath ).Where( x => archiveHandlers.ContainsKey( Path.GetExtension( x ) ) ).Select( x => x[..x.LastIndexOf( "." )] );

			return fileSystemDirectories.Concat( fileSystemArchives ).ToArray();
		}
		else
		{
			return Directory.GetFiles( absolutePath ).Where( x => !archiveHandlers.ContainsKey( Path.GetExtension( x ) ) ).ToArray();
		}
	}

	private IArchive GetArchive( string archivePath )
	{
		if ( archiveCache.TryGetValue( archivePath, out var archive ) )
		{
			return archive;
		}

		var extension = Path.GetExtension( archivePath );
		if ( archiveHandlers.TryGetValue( extension, out var handlerType ) )
		{
			archive = (IArchive)Activator.CreateInstance( handlerType, new[] { archivePath } )!;
			archiveCache[archivePath] = archive;
			return archive;
		}

		// No handler registered for this extension: signal "no archive" to the caller.
		return null!;
	}

	private (string ArchivePath, string InternalPath) FindArchivePath( string path )
	{
		lock ( resolvedArchive )
			if ( resolvedArchive.TryGetValue( path, out var known ) )
				return known;
		var found = FindArchivePathUncached( path );
		lock ( resolvedArchive )
			resolvedArchive[path] = found;
		return found;
	}

	private (string ArchivePath, string InternalPath) FindArchivePathUncached( string path )
	{
		var parts = path.Split( Path.DirectorySeparatorChar );
		var currentPath = new StringBuilder();

		for ( var i = 0; i < parts.Length; i++ )
		{
			// Append the separator before every part except the first, so an absolute
			// path keeps its leading separator (parts[0] is "" for "/a/b").
			if ( i > 0 )
			{
				currentPath.Append( Path.DirectorySeparatorChar );
			}

			currentPath.Append( parts[i] );

			foreach ( var handler in archiveHandlers )
			{
				// Resolve case-insensitively so an archive on disk as "FONTS.WAD" is found
				// for a lowercase request like "fonts" (see docs/tickets/T-014).
				var potentialArchivePath = ResolveCaseInsensitive( $"{currentPath}{handler.Key}" );

				if ( File.Exists( potentialArchivePath ) )
				{
					var remainingPath = path.Substring( currentPath.Length );
					return (potentialArchivePath, remainingPath.TrimStart( Path.DirectorySeparatorChar ));
				}
			}

			if ( Directory.Exists( ResolveCaseInsensitive( currentPath.ToString() ) ) )
			{
				continue;
			}
		}

		return (string.Empty, path);
	}

	public string GetAbsolutePath( string relativePath )
	{
		// Normalize to the platform's native separator so paths resolve on every OS
		// (the game's virtual paths use '/', Windows uses '\'). See docs/tickets/T-001.
		var absolute = Path.Combine( basePath, relativePath.TrimStart( '/' ) )
			.Replace( '/', Path.DirectorySeparatorChar )
			.Replace( '\\', Path.DirectorySeparatorChar );

		return ResolveCaseInsensitive( absolute );
	}

	/// <summary>
	/// The game references assets in lowercase, but the original media stores them in
	/// uppercase 8.3 names. On a case-sensitive filesystem (Linux) an exact path can miss;
	/// when it does, resolve each path segment case-insensitively against the real
	/// directory entries. See docs/tickets/T-014.
	/// </summary>
	private string ResolveCaseInsensitive( string fullPath )
	{
		lock ( resolvedCase )
			if ( resolvedCase.TryGetValue( fullPath, out var known ) )
				return known;
		var resolved = ResolveCaseInsensitiveUncached( fullPath );
		// Only remember paths that exist: a missing one may be created later (saves, the cache folder).
		if ( File.Exists( resolved ) || Directory.Exists( resolved ) )
			lock ( resolvedCase )
				resolvedCase[fullPath] = resolved;
		return resolved;
	}

	private string ResolveCaseInsensitiveUncached( string fullPath )
	{
		// Fast path: exact hit (always true on Windows / a correctly-cased FS).
		if ( File.Exists( fullPath ) || Directory.Exists( fullPath ) )
			return fullPath;

		if ( !fullPath.StartsWith( basePath, StringComparison.Ordinal ) )
			return fullPath;

		var relative = fullPath.Substring( basePath.Length ).TrimStart( Path.DirectorySeparatorChar );
		if ( relative.Length == 0 )
			return fullPath;

		var current = basePath;
		foreach ( var segment in relative.Split( Path.DirectorySeparatorChar ) )
		{
			var candidate = Path.Combine( current, segment );
			if ( File.Exists( candidate ) || Directory.Exists( candidate ) )
			{
				current = candidate;
				continue;
			}

			// No exact child: look for a case-insensitive match among the real entries.
			string? match = null;
			if ( Directory.Exists( current ) )
			{
				match = Directory.GetFileSystemEntries( current )
					.FirstOrDefault( entry => string.Equals(
						Path.GetFileName( entry ), segment, StringComparison.OrdinalIgnoreCase ) );
			}

			// Fall back to the requested name so downstream errors stay meaningful.
			current = match ?? candidate;
		}

		return current;
	}

	public string GetRelativePath( string absolutePath )
	{
		var path = Path.GetRelativePath( basePath, absolutePath ).Replace( "\\", "/" );
		return $"/{path}";
	}

	public bool IsArchive( string path )
	{
		var (archivePath, internalPath) = FindArchivePath( path );

		return !string.IsNullOrEmpty( archivePath );
	}

	public FileSystemWatcher CreateWatcher( string relativeDir, string filter )
	{
		var directoryName = GetAbsolutePath( relativeDir );
		var watcher = new FileSystemWatcher( directoryName, filter );

		watcher.NotifyFilter = NotifyFilters.Attributes
							 | NotifyFilters.CreationTime
							 | NotifyFilters.DirectoryName
							 | NotifyFilters.FileName
							 | NotifyFilters.LastAccess
							 | NotifyFilters.LastWrite
							 | NotifyFilters.Security
							 | NotifyFilters.Size;

		watcher.EnableRaisingEvents = true;

		return watcher;
	}
}
