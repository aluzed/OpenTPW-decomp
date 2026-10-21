using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenTPW;

public partial class ModelFile : BaseFormat
{
	public List<Mesh> Meshes { get; private set; } = null!;

	/// <summary>
	/// The model's node graph (T-048): named attachment points the ride VM addresses by type + id —
	/// walk nodes (WALKON), head nodes (ADDHEAD), car/track nodes (TOUR/BUMP/COAST) and sound/particle
	/// nodes (EVENT). RE'd from the loader + the runtime resolver <c>FUN_0044b220</c>: count = <c>u16</c>
	/// at header 0x48, table = <c>u32</c> file offset at header 0x7c, 0x14 bytes/node. The VM resolves a
	/// node by <c>(TypeMask &amp; selector) != 0 &amp;&amp; NodeId == requested</c>. <see cref="Node.TypeMask"/>
	/// is a bitfield of which subsystems may use the node. (The per-node transform pointers are null in the
	/// file and there is <b>no bone table</b> — Ghidra-confirmed there is no file skeleton: a node's world
	/// position is a runtime 4×4 matrix bound on the fly, translation row at <c>+0x30</c>, read by
	/// <c>FUN_00556b90</c>/<c>FUN_00435710</c> and driven by the keyframe anim or the car waypoint sim. So
	/// only the type/id graph is file data; positions are simulation output. See docs/08 + docs/tickets/T-048.)
	/// </summary>
	public List<Node> Nodes { get; } = new();

	/// <summary>
	/// The node-type selectors the engine queries with (RE'd: each subsystem calls the resolver
	/// <c>FUN_0044b220(model, selector, id)</c> and a node matches when <c>TypeMask &amp; selector != 0</c>).
	/// A node may carry several bits. Only the well-identified bits are named here; others are left raw.
	/// </summary>
	public static class NodeSelector
	{
		/// <summary>Object/head attach point — the head-mount setup <c>FUN_005587f0</c> + the TOUR/BUMP car
		/// helpers all query 0x80 (e.g. a tour ride's per-seat nodes).</summary>
		public const uint Object = 0x80;
		/// <summary>Bumper/kart car node (<c>FUN_0054a040</c>).</summary>
		public const uint Car = 0x100;
		/// <summary>Walk node (<c>FUN_00557ab0</c>; e.g. a coaster's queue/walk nodes).</summary>
		public const uint Walk = 0x800;
	}

	public readonly struct Node
	{
		/// <summary>Bitfield of node types/subsystems that may address this node (matched against the
		/// resolver's selector). E.g. coaster track nodes carry the high car/track bits.</summary>
		public uint TypeMask { get; init; }
		/// <summary>The node's id within its type (the VM's node argument).</summary>
		public int NodeId { get; init; }
		/// <summary>The node entry's third word (0 on every shipped sample seen).</summary>
		public uint Extra { get; init; }

		/// <summary>True if this node is addressable by the given selector — mirrors the engine resolver
		/// (<c>TypeMask &amp; selector != 0</c>).</summary>
		public bool Matches( uint selector ) => (TypeMask & selector) != 0;

		/// <summary>Object/head attach node (selector 0x80).</summary>
		public bool IsObject => Matches( NodeSelector.Object );
		/// <summary>Bumper/kart car node (selector 0x100).</summary>
		public bool IsCar => Matches( NodeSelector.Car );
		/// <summary>Walk node (selector 0x800).</summary>
		public bool IsWalk => Matches( NodeSelector.Walk );
	}

	/// <summary>Nodes addressable by the given selector (mirrors the engine resolver's match test).</summary>
	public IEnumerable<Node> NodesMatching( uint selector ) => Nodes.Where( n => n.Matches( selector ) );

	// The .MD2 format version this parser supports (header fields at offsets 4 and 8). The
	// original loader accepts only these values; the legacy static variant (GARROW.MD2 / RARROW.MD2 =
	// 0x18/0x17) has its own packed header, decoded by ReadLegacyStatic. See docs/tickets/T-015.
	private const uint SupportedVersion = 0xDD;
	private const uint SupportedSubVersion = 0xCB;
	private const uint LegacyVersion = 0x18;
	private const uint LegacySubVersion = 0x17;

	public ModelFile( Stream stream )
	{
		ReadFromStream( stream );
	}

	public ModelFile( string path )
	{
		ReadFromFile( path );
	}

	public struct Vertex
	{
		public Vector3 Position { get; set; }
		public uint TextureIndex { get; set; }
	}

	public class Mesh
	{
		public string Name { get; set; } = null!;
		public uint VertexOffset { get; set; }
		public uint UvOffset { get; set; }
		public uint VertCnt { get; set; }
		public uint MaterialOffset { get; set; }
		public uint FaceOffset { get; set; }
		public uint FaceCount { get; set; }
		public uint VertexCount { get; set; }
		public uint VertexOrderLen { get; set; }
		public uint VertexOrderOffset { get; set; }
		public Vertex[] Vertices { get; set; } = null!;
		public uint[] Indices { get; set; } = null!;
		public Vector2[] TexCoords { get; set; } = null!;
		public Matrix4x4 TransformMatrix { get; set; }
		public MaterialData[] Materials { get; set; } = null!;

		public Vector3[] Normals { get; set; } = null!;
	}

	public struct FrameData
	{
		public uint Value;
		public uint MustBeZero;
		public ushort Pad;
		public ushort willBe1;
		public uint FrameNameOff;
	}

	public class MaterialData
	{
		public uint FrameOffset;
		public ushort a;
		public ushort b;
		public ushort StartIndex;
		public ushort EndIndex;
		public uint Pad;

		public string Name = null!;
		public uint Flags;
	}

	protected override void ReadFromStream( Stream stream )
	{
		using ( BinaryReader reader = new BinaryReader( stream, Encoding.ASCII, true ) )
		{
			var fileLength = reader.BaseStream.Length;

			// Validate that an offset we're about to seek to is inside the file. Some .MD2
			// variants (e.g. the static GARROW.MD2 with frameCount 0) use a different
			// header layout, which would otherwise drive reads past EOF; fail with a clear,
			// catchable error instead of an opaque EndOfStreamException. See T-012.
			void Require( long offset, string what )
			{
				if ( offset < 0 || offset > fileLength )
					throw new InvalidDataException(
						$"MD2: {what} offset 0x{offset:X} is out of range (file is {fileLength} bytes); " +
						"unsupported .MD2 layout." );
			}

			// Magic: 0x1CD15D46.
			var magic = reader.ReadUInt32();
			if ( magic != 0x1CD15D46 )
				throw new InvalidDataException( $"MD2: bad magic 0x{magic:X8}, expected 0x1CD15D46." );

			// Two version fields follow the magic, at offsets 4 and 8. The original loader
			// (FUN_0046d6d0 in tp.exe, recovered via Ghidra) accepts only the current format —
			// it requires offset 4 == 0xDD and offset 8 == 0xCB — and rejects older/legacy
			// variants. The static UI arrows (GARROW.MD2 / RARROW.MD2) carry 0x18 / 0x17 there
			// and have a different header layout, so the animated parser below can't read them.
			// Match the game and reject any non-current version with a clear message. See T-015.
			var version = reader.ReadUInt32();      // offset 4 (observed 0xDD on all shipping models)
			var subVersion = reader.ReadUInt32();   // offset 8 (observed 0xCB)

			// The static UI arrows (GARROW.MD2 / RARROW.MD2) carry 0x18 / 0x17 and use a packed legacy
			// header — decode them separately (T-015). Anything else is genuinely unsupported.
			if ( version == LegacyVersion && subVersion == LegacySubVersion )
			{
				ReadLegacyStatic( reader, fileLength );
				return;
			}
			if ( version != SupportedVersion || subVersion != SupportedSubVersion )
				throw new InvalidDataException(
					$"MD2: unsupported version (0x{version:X}, 0x{subVersion:X}); this parser " +
					$"handles the current format (0x{SupportedVersion:X}, 0x{SupportedSubVersion:X}) " +
					$"and the legacy static variant (0x{LegacyVersion:X}, 0x{LegacySubVersion:X}). See T-015." );

			reader.BaseStream.Seek( 0x50, SeekOrigin.Begin );
			uint off2 = reader.ReadUInt32();

			reader.BaseStream.Seek( 0x36, SeekOrigin.Begin );
			ushort frameCount = reader.ReadUInt16();

			Require( off2 + (8L * frameCount), "texture list" );
			reader.BaseStream.Seek( off2 + (8 * frameCount), SeekOrigin.Begin );

			List<string> textures = new();
			for ( int i = 0; i < frameCount; i++ )
			{
				var texName = Encoding.ASCII.GetString( reader.ReadBytes( 20 ) );
				textures.Add( texName );
			}

			reader.BaseStream.Seek( 0x44, SeekOrigin.Begin );
			ushort meshCnt = reader.ReadUInt16();

			reader.BaseStream.Seek( 0x50, SeekOrigin.Begin );
			uint textureListOffset = reader.ReadUInt32();

			reader.BaseStream.Seek( 0x54, SeekOrigin.Begin );
			uint frameListOffset = reader.ReadUInt32();

			Require( frameListOffset, "frame list" );
			reader.BaseStream.Seek( frameListOffset, SeekOrigin.Begin );
			List<FrameData> frameData = new();
			for ( int i = 0; i < frameCount; ++i )
			{
				frameData.Add( new()
				{
					Value = reader.ReadUInt32(),
					MustBeZero = reader.ReadUInt32(),
					Pad = reader.ReadUInt16(),
					willBe1 = reader.ReadUInt16(),
					FrameNameOff = reader.ReadUInt32()
				} );
			}

			reader.BaseStream.Seek( 0x70, SeekOrigin.Begin );
			uint meshPtr = reader.ReadUInt32();

			Require( meshPtr, "mesh table" );
			reader.BaseStream.Seek( meshPtr, SeekOrigin.Begin );

			Meshes = new List<Mesh>( meshCnt );

			for ( int meshIdx = 0; meshIdx < meshCnt; meshIdx++ )
			{
				Require( meshPtr + (160L * meshIdx), "mesh entry" );
				reader.BaseStream.Seek( meshPtr + (160 * meshIdx), SeekOrigin.Begin );

				reader.BaseStream.Seek( 16, SeekOrigin.Current ); // Skip initial mesh data

				// Read mat4
				Matrix4x4 transformMatrix;

				{
					var ma = new Vector4( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
					var mb = new Vector4( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
					var mc = new Vector4( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
					var md = new Vector4( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
					transformMatrix = new Matrix4x4(
						ma.X, ma.Y, ma.Z, ma.W,
						mb.X, mb.Y, mb.Z, mb.W,
						mc.X, mc.Y, mc.Z, mc.W,
						md.X, md.Y, md.Z, md.W
					);
				}

				uint texIndex = reader.ReadUInt32();
				uint nOff = reader.ReadUInt32();
				ushort vertexCount = reader.ReadUInt16();
				ushort materialCount = reader.ReadUInt16();
				ushort faceCount = reader.ReadUInt16();
				ushort vertexOrderLength = reader.ReadUInt16();
				uint vertexOffset = reader.ReadUInt32();
				_ = reader.ReadUInt32();
				uint uvOffset = reader.ReadUInt32();
				uint materialOffset = reader.ReadUInt32();
				uint faceOffset = reader.ReadUInt32();
				reader.BaseStream.Seek( 32, SeekOrigin.Current ); // Skip _idk2 to _37
				uint vertexOrderOffset = reader.ReadUInt32();
				reader.BaseStream.Seek( 8, SeekOrigin.Current ); // Skip _38 and _39

				reader.BaseStream.Seek( nOff, SeekOrigin.Begin );
				string name = "";
				char c;

				do
				{
					c = reader.ReadChar();
					name += c;
				} while ( c != '\0' );

				reader.BaseStream.Seek( materialOffset, SeekOrigin.Begin );
				List<MaterialData> materials = new();

				for ( int i = 0; i < materialCount; ++i )
				{
					materials.Add( new()
					{
						FrameOffset = reader.ReadUInt32(),
						a = reader.ReadUInt16(),
						b = reader.ReadUInt16(),
						StartIndex = reader.ReadUInt16(),
						EndIndex = reader.ReadUInt16(),
						Pad = reader.ReadUInt32()
					} );
				}

				foreach ( var material in materials )
				{
					// start at texIdOffset, divide by 8 to get index
					uint frameId = (material.FrameOffset - textureListOffset) / 8;

					// The Space lobby island has a material pointing past the texture list; leave it unnamed (drawn
					// with the missing texture) rather than failing the whole model.
					if ( frameId < frameData.Count )
					{
						var frame = frameData[(int)frameId];
						reader.BaseStream.Seek( frame.FrameNameOff, SeekOrigin.Begin );
						var texName = Encoding.ASCII.GetString( reader.ReadBytes( 20 ) );
						material.Name = Path.GetFileNameWithoutExtension( texName );
					}
					else
					{
						material.Name = "";
					}

					reader.BaseStream.Seek( material.FrameOffset, SeekOrigin.Begin );
					material.Flags = reader.ReadUInt32();
				}

				Meshes.Add( new Mesh
				{
					Name = name,
					VertCnt = materialCount,
					UvOffset = uvOffset,
					VertexOffset = vertexOffset,
					MaterialOffset = materialOffset,
					FaceOffset = faceOffset,
					FaceCount = faceCount,
					VertexCount = vertexCount,
					VertexOrderLen = vertexOrderLength,
					VertexOrderOffset = vertexOrderOffset,
					TransformMatrix = transformMatrix,
					Materials = materials.ToArray()
				} );
			}

			// Process mesh data
			for ( int meshIdx = 0; meshIdx < Meshes.Count; meshIdx++ )
			{
				Mesh mesh = Meshes[meshIdx];
				uint meshPosEnd = (meshIdx + 1 < Meshes.Count) ? Meshes[meshIdx + 1].VertexOffset : Meshes[0].MaterialOffset;
				uint cnt = (meshPosEnd - mesh.VertexOffset) / (3 * 4 * 4);

				reader.BaseStream.Seek( mesh.UvOffset, SeekOrigin.Begin );
				List<Vector2> uvs = new List<Vector2>();
				uint uvCnt = mesh.VertexOrderLen;
				if ( uvCnt % 4 != 0 )
				{
					uvCnt += (uint)(4 - (uvCnt % 4));
				}

				while ( uvCnt > 0 )
				{
					int elem = (int)Math.Min( uvCnt, 4 );
					Vector2[] points = new Vector2[elem];

					for ( int i = 0; i < elem; i++ )
						points[i].X = reader.ReadSingle();
					for ( int i = 0; i < elem; i++ )
						points[i].Y = reader.ReadSingle();

					uvs.AddRange( points );
					uvCnt -= (uint)elem;
				}

				mesh.TexCoords = uvs.ToArray();

				reader.BaseStream.Seek( mesh.VertexOffset, SeekOrigin.Begin );

				List<Vector3> vertices = new List<Vector3>();
				uint c = mesh.VertexCount;
				if ( c % 4 != 0 )
				{
					c += (uint)(4 - (c % 4));
				}

				while ( c > 0 )
				{
					int elem = (int)Math.Min( c, 4 );
					Vector3[] points = new Vector3[elem];

					for ( int i = 0; i < elem; i++ )
						points[i].X = reader.ReadSingle();
					for ( int i = 0; i < elem; i++ )
						points[i].Y = reader.ReadSingle();
					for ( int i = 0; i < elem; i++ )
						points[i].Z = reader.ReadSingle();

					vertices.AddRange( points );
					c -= (uint)elem;
				}

				// Read vertex order
				reader.BaseStream.Seek( mesh.VertexOrderOffset, SeekOrigin.Begin );
				ushort[] vertexOrder = new ushort[mesh.VertexOrderLen];
				for ( int i = 0; i < mesh.VertexOrderLen; i++ )
				{
					vertexOrder[i] = reader.ReadUInt16();
				}

				// Re-order vertices
				Vertex[] reorderedVertices = new Vertex[vertexOrder.Length];
				for ( int i = 0; i < vertexOrder.Length; i++ )
				{
					int textureIndex = 0;
					for ( int j = 0; j < mesh.Materials.Length; ++j )
					{
						if ( i >= mesh.Materials[j].StartIndex && i <= mesh.Materials[j].EndIndex )
						{
							textureIndex = j;
							break;
						}
					}

					var vertex = new Vertex()
					{
						Position = vertices[vertexOrder[i]],
						TextureIndex = (uint)textureIndex
					};

					reorderedVertices[i] = vertex;
				}

				mesh.Vertices = reorderedVertices;

				// Parse face data
				reader.BaseStream.Seek( mesh.FaceOffset, SeekOrigin.Begin );
				List<uint> indices = new List<uint>();

				for ( int i = 0; i < mesh.FaceCount; i++ )
				{
					reader.ReadUInt16(); // Skip _ptr
					ushort _a = reader.ReadUInt16();
					ushort _b = reader.ReadUInt16();
					ushort _c = reader.ReadUInt16();

					// Reverse winding order
					indices.Add( (uint)_a );
					indices.Add( (uint)_b );
					indices.Add( (uint)_c );
				}

				mesh.Indices = indices.ToArray();

				CalculateNormals( mesh );
			}

			ReadNodeTable( reader, fileLength );
		}
	}

	// Reads the node graph from the full file bytes (parsing delegates to the testable ParseNodeTable).
	private void ReadNodeTable( BinaryReader reader, long fileLength )
	{
		Nodes.Clear();
		reader.BaseStream.Seek( 0, SeekOrigin.Begin );
		var data = new byte[fileLength];
		var read = 0;
		while ( read < data.Length )
		{
			int n = reader.BaseStream.Read( data, read, data.Length - read );
			if ( n <= 0 ) break;
			read += n;
		}
		Nodes.AddRange( ParseNodeTable( data ) );
	}

	/// <summary>
	/// Parses the node table from a full <c>.MD2</c> byte buffer (T-048): count = <c>u16</c> at 0x48,
	/// table file offset = <c>u32</c> at 0x7c, 0x14 bytes/node <c>{u32 typeMask, u32 nodeId, u32 extra,
	/// u32 ptrA, u32 ptrB}</c>. <c>ptrA/ptrB</c> are file-relative (relocated at runtime) and null in
	/// shipped models, so only type+id+extra are taken. Returns an empty list if absent/implausible.
	/// </summary>
	public static List<Node> ParseNodeTable( byte[] data )
	{
		var nodes = new List<Node>();
		if ( data.Length < 0x80 )
			return nodes;

		int U16( int o ) => data[o] | data[o + 1] << 8;
		uint U32( int o ) => (uint)(data[o] | data[o + 1] << 8 | data[o + 2] << 16 | data[o + 3] << 24);

		int count = U16( 0x48 );
		uint tablePtr = U32( 0x7c );
		if ( count <= 0 || count > 4096 || tablePtr == 0 || tablePtr + (long)count * 0x14 > data.Length )
			return nodes;

		for ( int i = 0; i < count; i++ )
		{
			int e = (int)tablePtr + i * 0x14;
			nodes.Add( new Node { TypeMask = U32( e ), NodeId = (int)U32( e + 4 ), Extra = U32( e + 8 ) } );
		}
		return nodes;
	}

	/// <summary>
	/// Decodes the legacy static <c>.MD2</c> variant — version (0x18, 0x17), the UI placement arrows
	/// <c>GARROW.MD2</c> / <c>RARROW.MD2</c>. RE'd from the file (the shipped loader gates this version
	/// behind special flags; the layout is the same family as the animated variant but **2-byte packed**):
	/// <list type="bullet">
	/// <item>header pointer at 0x72 → the <b>mesh table</b>: <c>u32 meshCount</c>, then a 16-byte prologue,
	///   then one 16-byte descriptor per mesh — <c>{u16 numVerts, u16 numFaces, u32 vertPtr, u32 facePtr,
	///   float scale}</c>;</item>
	/// <item><b>vertices</b> (<c>vertPtr</c>): <c>numVerts × 32 B</c> = 8 floats (position xyz, normal xyz,
	///   uv);</item>
	/// <item><b>faces</b> (<c>facePtr</c>): <c>numFaces × 24 B</c>; the three triangle indices are the
	///   <c>u16</c>s at byte offsets +2/+4/+6 (offset 0 is a per-face flag).</item>
	/// </list>
	/// Verified against GARROW/RARROW: 14 vertices (finite arrow geometry) + 24 triangles with all indices
	/// in range. See docs/tickets/T-015.
	/// </summary>
	private void ReadLegacyStatic( BinaryReader reader, long fileLength )
	{
		void Require( long offset, string what )
		{
			if ( offset < 0 || offset > fileLength )
				throw new InvalidDataException(
					$"MD2 (legacy): {what} offset 0x{offset:X} is out of range (file is {fileLength} bytes)." );
		}

		uint ReadU32At( long off )
		{
			reader.BaseStream.Seek( off, SeekOrigin.Begin );
			return reader.ReadUInt32();
		}

		// Model name (null-terminated, from 0x18) — reused as the mesh name.
		reader.BaseStream.Seek( 0x18, SeekOrigin.Begin );
		var nameBuilder = new StringBuilder();
		for ( char ch; (ch = reader.ReadChar()) != '\0' && nameBuilder.Length < 64; )
			nameBuilder.Append( ch );
		var modelName = nameBuilder.ToString();

		// Texture path (header offset 0x5a → e.g. "D:\THEMEPK2\jungle\texture"); use its leaf as the
		// material/texture name so the mesh carries an authentic binding from the file.
		string textureName = "";
		uint texPathPtr = ReadU32At( 0x5a );
		if ( texPathPtr > 0 && texPathPtr < fileLength )
		{
			reader.BaseStream.Seek( texPathPtr, SeekOrigin.Begin );
			var sb = new StringBuilder();
			for ( char ch; sb.Length < 128 && reader.BaseStream.Position < fileLength && (ch = reader.ReadChar()) != '\0'; )
				sb.Append( ch );
			var raw = sb.ToString();
			var slash = raw.LastIndexOfAny( new[] { '\\', '/' } );
			textureName = slash >= 0 ? raw[(slash + 1)..] : raw;
		}

		// Mesh table pointer (this packed layout keeps it at header offset 0x72).
		uint meshTablePtr = ReadU32At( 0x72 );
		Require( meshTablePtr, "mesh table" );
		uint meshCount = ReadU32At( meshTablePtr );
		if ( meshCount == 0 || meshCount > 256 )
			throw new InvalidDataException( $"MD2 (legacy): implausible mesh count {meshCount}." );

		Meshes = new List<Mesh>( (int)meshCount );

		const int descriptorStride = 16;
		const int vertexStride = 32; // pos(3) + normal(3) + uv(2), all floats
		const int faceStride = 24;   // flag(u16) + 3 indices(u16) + per-face data

		for ( int m = 0; m < meshCount; m++ )
		{
			long desc = meshTablePtr + 16 + (long)m * descriptorStride;
			Require( desc + descriptorStride, "mesh descriptor" );
			reader.BaseStream.Seek( desc, SeekOrigin.Begin );
			ushort numVerts = reader.ReadUInt16();
			ushort numFaces = reader.ReadUInt16();
			uint vertPtr = reader.ReadUInt32();
			uint facePtr = reader.ReadUInt32();

			Require( vertPtr + (long)numVerts * vertexStride, "legacy vertex block" );
			Require( facePtr + (long)numFaces * faceStride, "legacy face block" );

			var vertices = new Vertex[numVerts];
			var normals = new Vector3[numVerts];
			var texCoords = new Vector2[numVerts];
			for ( int i = 0; i < numVerts; i++ )
			{
				reader.BaseStream.Seek( vertPtr + (long)i * vertexStride, SeekOrigin.Begin );
				var pos = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
				var nrm = new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
				var uv = new Vector2( reader.ReadSingle(), reader.ReadSingle() );
				vertices[i] = new Vertex { Position = pos, TextureIndex = 0 };
				normals[i] = nrm;
				texCoords[i] = uv;
			}

			var indices = new List<uint>( numFaces * 3 );
			for ( int i = 0; i < numFaces; i++ )
			{
				reader.BaseStream.Seek( facePtr + (long)i * faceStride, SeekOrigin.Begin );
				_ = reader.ReadUInt16(); // per-face flag/type
				ushort a = reader.ReadUInt16();
				ushort b = reader.ReadUInt16();
				ushort c = reader.ReadUInt16();
				if ( a < numVerts && b < numVerts && c < numVerts )
				{
					indices.Add( a );
					indices.Add( b );
					indices.Add( c );
				}
			}

			var materials = string.IsNullOrEmpty( textureName )
				? Array.Empty<MaterialData>()
				: new[] { new MaterialData { Name = textureName, StartIndex = 0, EndIndex = (ushort)Math.Max( 0, numVerts - 1 ) } };

			Meshes.Add( new Mesh
			{
				Name = modelName,
				Vertices = vertices,
				Indices = indices.ToArray(),
				TexCoords = texCoords,
				Normals = normals,
				VertexCount = numVerts,
				FaceCount = numFaces,
				VertCnt = (uint)materials.Length,
				Materials = materials,
			} );
		}
	}

	private void CalculateNormals( Mesh mesh )
	{
		Vector3[] normals = new Vector3[mesh.Vertices.Length];

		// Initialize all normals to zero
		for ( int i = 0; i < normals.Length; i++ )
		{
			normals[i] = Vector3.Zero;
		}

		// Calculate face normals and accumulate them for each vertex
		for ( int i = 0; i < mesh.Indices.Length; i += 3 )
		{
			uint i1 = mesh.Indices[i];
			uint i2 = mesh.Indices[i + 1];
			uint i3 = mesh.Indices[i + 2];

			Vector3 v1 = mesh.Vertices[i1].Position;
			Vector3 v2 = mesh.Vertices[i2].Position;
			Vector3 v3 = mesh.Vertices[i3].Position;

			Vector3 edge1 = v2 - v1;
			Vector3 edge2 = v3 - v1;

			Vector3 faceNormal = Vector3.Cross( edge1, edge2 );
			faceNormal = faceNormal.Normal; // Ensure face normal is unit length

			// Accumulate the face normal to all three vertices
			normals[i1] += faceNormal;
			normals[i2] += faceNormal;
			normals[i3] += faceNormal;
		}

		// Normalize all vertex normals to average them
		for ( int i = 0; i < normals.Length; i++ )
		{
			if ( normals[i] != Vector3.Zero )
				normals[i] = normals[i].Normal;
		}

		mesh.Normals = normals;
	}
}
