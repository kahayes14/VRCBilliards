Shader "harry_t/TableSurface"
{
	Properties
	{
		[HDR] _EmissionColour ("Color", Color) = (1,1,1,1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_StuffTex ("Metalic/Smoothness/Emit (RGB)", 2D) = "white" {}
		_ClothColour ("Cloth Colour", Color) = (1,1,1,1)
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 200

		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types

		#pragma surface surf Standard fullforwardshadows vertex:vert

		// Use shader model 3.0 target, to get nicer looking lighting
		#pragma target 3.0

		sampler2D _MainTex;
		sampler2D _StuffTex;

		struct Input
		{
			float2 uv_MainTex;
			float3 modelPos;
		};

		fixed4 _EmissionColour;
		fixed3 _ClothColour;

		void vert ( inout appdata_full v, out Input o ) 
		{
			UNITY_INITIALIZE_OUTPUT(Input,o);
         o.modelPos = v.vertex.xyz;
      }

		void surf (Input IN, inout SurfaceOutputStandard o)
		{
			// Albedo comes from a texture tinted by color
			fixed4 c = tex2D (_MainTex, IN.uv_MainTex);

			float xy_angle = (atan2( IN.modelPos.x, IN.modelPos.z ) + 3.4) * 0.14915494309;
			float angle_cl = clamp((xy_angle - _EmissionColour.a) * 40.0, 0, 1);

			fixed4 stuff = tex2D( _StuffTex, IN.uv_MainTex );
			o.Albedo = lerp( c.rgb, _ClothColour * c.rgb * 2.0, pow(stuff.a,0.1) );
			o.Metallic = stuff.r;
			o.Smoothness = stuff.g;
			o.Alpha = c.a;
			o.Emission = stuff.b * _EmissionColour * angle_cl;
		}

		ENDCG
	}
	FallBack "Diffuse"
}
