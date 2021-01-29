Shader "harry_t/additive_screen"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
		  _Noise ("Clouds", 2D) = "white" {}
    }
    SubShader
    {
			Tags { "Queue" = "Transparent" }
		
			ZWrite Off
			Cull Off

        Pass
        {
				Blend One One

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
					 float3 uv1 : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
				sampler2D _Noise;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
					 o.uv1 = mul( unity_ObjectToWorld, v.vertex );
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
					 fixed4 noise = tex2D(_Noise, i.uv1.yz + float2( _Time.x, 0.0 ));

                return col * noise.r * 1.5;
            }
            ENDCG
        }
    }
}
