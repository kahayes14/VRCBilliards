Shader "harry_t/TableSurface_quest"
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

		#pragma surface surf Lambert noforwardadd

		sampler2D _MainTex;
		sampler2D _StuffTex;

		struct Input
		{
			float2 uv_MainTex;
		};

		fixed4 _EmissionColour;
		fixed3 _ClothColour;

		void surf (Input IN, inout SurfaceOutput o)
		{
			fixed4 c = tex2D (_MainTex, IN.uv_MainTex);
			fixed4 stuff = tex2D( _StuffTex, IN.uv_MainTex );
			o.Albedo = lerp( c.rgb, _ClothColour * c.rgb * 2.0, pow(stuff.a,0.1) );
			o.Emission = stuff.b * _EmissionColour;
		}

		ENDCG
	}
	FallBack "Diffuse"
}