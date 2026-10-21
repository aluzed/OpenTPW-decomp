namespace OpenTPW;

public class Sky : ModelEntity
{
	private Material material = null!;

	public override void Spawn()
	{
		base.Spawn();

		material = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader" );
		SetColor( Fog.Default );

		Model = Primitives.Cube.GenerateModel( material );
		Scale = new Vector3( -10000f );
	}

	/// <summary>Fill the backdrop with <paramref name="color"/> — a park's horizon, so the far fog meets a matching sky.</summary>
	public void SetColor( System.Numerics.Vector4 color )
	{
		byte C( float v ) => (byte)Math.Clamp( v * 255f, 0f, 255f );
		material.Set( "Color", new Texture( [C( color.X ), C( color.Y ), C( color.Z ), 255], 1, 1 ) );
	}
}
