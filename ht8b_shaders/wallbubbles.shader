Shader "harry_t/wallbubbles"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
		  _Mask( "Mask", 2D) = "white" {}
		  _MaskSize( "MaskSize", Range(0,1000)) = 1.0
		  _OffsetMap( "Offset", 2D ) = "white" {}
		  _Color( "Color", Color ) = (1,1,1,1)
		  _OffsetIntensity( "Warp Strength", Range(0,10) ) = 0.0
		  _Speed( "Speed", Range(-2, 2)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

				sampler2D _MainTex;
				sampler2D _OffsetMap;
				sampler2D _Mask;
				
				half _OffsetIntensity;
				half _Speed;
				half _MaskSize;
				fixed4 _Color;


            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
					float2 flooredUV = (1.0 / _MaskSize) * floor(i.uv / (1.0 / _MaskSize));

					fixed4 sampleOffset = tex2D( _OffsetMap, flooredUV + float2(0.0, _Time.g * _Speed) );

					fixed4 c = tex2D( _MainTex, flooredUV + float2(0.0, _OffsetIntensity * sampleOffset.r) ) * _Color;
					c *= tex2D( _Mask, i.uv * float2(_MaskSize, _MaskSize) ).r;

					return c * 2;
            }
            ENDCG
        }
    }
}
