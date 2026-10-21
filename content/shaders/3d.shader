vertex "content/shaders/includes/3d.vert"

common {
  layout( set = 0, binding = 0 ) uniform ObjectUniformBuffer {
    mat4 g_mModel;
    mat4 g_mView;
    mat4 g_mProj;

    vec3 g_vLightPos;
    vec3 g_vLightColor;
    vec3 g_vCameraPos;
    float g_flTime;
    vec4 g_vFogColor;
  } g_oUbo;
}

fragment {
  //
  // In
  //
  layout( location = 0 ) in struct VS_OUT {
    vec2 vTexCoords;
    vec3 vNormal;
    vec3 vPosition;
    vec3 vPositionWs;
  } vs_out;

  //
  // Out
  //
  layout( location = 0 ) out vec4 fragColor;

  //
  // Uniforms
  //
  layout( set = 1, binding = 0 ) uniform texture2D Color;
  layout( set = 1, binding = 1 ) uniform sampler s_Color;

  vec3 lambert( vec3 vLightDir, vec3 vNormal ) 
  {
    return max( dot( normalize( vLightDir ), normalize( vNormal ) ), 0.0 ) * g_oUbo.g_vLightColor;
  }

  vec3 ambient( vec3 vLightColor ) 
  {
    return vLightColor * 0.2;
  }

  vec3 specular( vec3 vLightDir, vec3 vNormal, vec3 vCameraDir, float fShininess ) 
  {
    vec3 vReflection = reflect( normalize( -vLightDir ), normalize( vNormal ) );
    return pow( max( dot( normalize( vCameraDir ), vReflection ), 0.0 ), fShininess ) * g_oUbo.g_vLightColor;
  }

  void main() 
  {
    // The sun is a direction, not a point: g_vLightPos (0,100,100) sits next to the origin, ~500 units from the park,
    // so a point-light direction grazed flat surfaces (queue paths read as dark strips).
    vec3 vLightDir = normalize( g_oUbo.g_vLightPos );

    vec3 vLambert = lambert( vLightDir, vs_out.vNormal );
    vec3 vAmbient = ambient( g_oUbo.g_vLightColor );
    
    vec3 vCameraDir = normalize( g_oUbo.g_vCameraPos - vs_out.vPosition );
    vec3 vSpecular = specular( vLightDir, vs_out.vNormal, vCameraDir, 32.0 );

    vec4 vColor = texture( sampler2D( Color, s_Color ), vs_out.vTexCoords ) * 0.75;
    vColor.z *= 1.25;

    vec3 fogColor = g_oUbo.g_vFogColor.rgb; // the park's horizon colour (Fog.cs)
    float fogStart = 2.0;
    float fogEnd = 30.0;

    // float fogAmount = ( length( vs_out.vPositionWs ) - fogStart ) / ( fogEnd - fogStart );
    // fogAmount = clamp( fogAmount, 0.0, 1.0 );
    // smoother fog, better looking
    // Same ramp as test.shader (full at ~920 units). The old one, normalised to fogEnd = 30, fogged anything
    // further than 30 units to pure sky colour — queue paths and coaster track read as cyan strips (T-072).
    float fogAmount = clamp( exp( length( vs_out.vPositionWs ) * 0.004 ) * 0.025, 0.0, 1.0 );

    fragColor = vec4( vLambert + vAmbient, 1.0 ) * vColor;
    fragColor = mix( fragColor, vec4( fogColor, 1.0 ), fogAmount );

    if ( fragColor.w < 0.1 )
      discard;
  }
}