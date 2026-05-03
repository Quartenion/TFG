Shader "Custom/Liquid"
{
    Properties
    {
        [Header(Liquid)]
        _Color ("Liquid Color", Color) = (0.78, 0.05, 0.1, 1)
        _TopColor ("Top (inside) Color", Color) = (1.0, 0.3, 0.3, 1)
        _SurfaceColor ("Surface Edge Color", Color) = (1.0, 0.6, 0.6, 1)
        _SurfaceWidth ("Surface Edge Width", Range(0.0, 0.2)) = 0.02

        [Header(Fill and wobble)]
        _FillAmount ("Fill Amount (world Y)", Range(-10, 10)) = 0.0
        _WobbleX ("Wobble X", Range(-1, 1)) = 0.0
        _WobbleZ ("Wobble Z", Range(-1, 1)) = 0.0

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1, 0.85, 0.85, 1)
        _FoamWidth ("Foam Width", Range(0.0, 0.2)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "LiquidPass"
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float  fillEdgeY   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                half4  _TopColor;
                half4  _SurfaceColor;
                half4  _FoamColor;
                float  _FillAmount;
                float  _WobbleX;
                float  _WobbleZ;
                float  _SurfaceWidth;
                float  _FoamWidth;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.worldPos = worldPos;

                // Wobble: la linea de llenado se inclina segun la posicion relativa al centro
                float3 worldOrigin = TransformObjectToWorld(float3(0, 0, 0));
                float dx = worldPos.x - worldOrigin.x;
                float dz = worldPos.z - worldOrigin.z;
                OUT.fillEdgeY = _FillAmount + dx * _WobbleX + dz * _WobbleZ;

                return OUT;
            }

            half4 frag (Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // Distancia al borde del liquido en Y
                float distFromTop = IN.fillEdgeY - IN.worldPos.y;

                // Por encima del nivel: fragment descartado (no hay liquido)
                if (distFromTop < 0) discard;

                // Borde superior: pequena banda que simula el menisco / superficie visible
                if (distFromTop < _SurfaceWidth)
                {
                    return _SurfaceColor;
                }

                // Foam justo debajo del borde
                if (_FoamWidth > 0 && distFromTop < (_SurfaceWidth + _FoamWidth))
                {
                    return _FoamColor;
                }

                // Cara trasera = vemos el "fondo" del liquido desde arriba/dentro -> top color
                // Cara frontal = vemos el liquido desde fuera -> color principal
                return isFrontFace ? _Color : _TopColor;
            }
            ENDHLSL
        }
    }
}
