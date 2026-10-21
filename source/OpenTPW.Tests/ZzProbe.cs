using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class ZzProbe
{
	[TestMethod]
	public void Dump()
	{
		Log = new();
		using var wad = new WadArchive( "/var/www/recomp/OpenTPW-decomp/install/Data/ui.wad" );
		foreach ( var model in new[] { "f_wlobbg", "f_lobbutbg", "handle" } ) {
		var m = new ModelFile( new MemoryStream( wad.GetFile( model + ".MD2" ).GetData() ) ).Meshes[0];
		var sb = new StringBuilder();
		for ( int i = 0; i < m.Vertices.Length; i++ )
			sb.AppendLine( $"v {m.Vertices[i].Position.X} {m.Vertices[i].Position.Y} {m.Vertices[i].Position.Z} {m.TexCoords[i].X} {m.TexCoords[i].Y} {m.Vertices[i].TextureIndex} {m.Normals[i].X} {m.Normals[i].Y} {m.Normals[i].Z}" );
		for ( int i = 0; i + 2 < m.Indices.Length; i += 3 )
			sb.AppendLine( $"f {m.Indices[i]} {m.Indices[i + 1]} {m.Indices[i + 2]}" );
		File.WriteAllText( "/tmp/claude-1000/-var-www-recomp-OpenTPW-decomp/86c19f67-2252-41f3-8b18-42cc46350f56/scratchpad/mp/" + model + ".obj", sb.ToString().Replace( ',', '.' ) );
		}
		foreach ( var n in new[] { "nu-nupan", "ipan", "grip", "windshine2" } )
		{
			var t = new TextureFile( new MemoryStream( wad.GetFile( $"textures/{n}.wct" ).GetData() ) ).Data;
			File.WriteAllBytes( $"/tmp/claude-1000/-var-www-recomp-OpenTPW-decomp/86c19f67-2252-41f3-8b18-42cc46350f56/scratchpad/mp/{n}.{t.Width}x{t.Height}.rgba", t.Data );
		}
	}
}
