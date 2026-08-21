Shader "Custom/Liquid"
{
    Properties
    {
        [Header(Liquid Colors)]
        _Color ("Liquid Color", Color) = (0.78, 0.05, 0.10, 1)
        _TopColor ("Top (inside) Color", Color) = (1.00, 0.30, 0.30, 1)
        _SurfaceColor ("Surface Edge Color", Color) = (1.00, 0.55, 0.55, 1)
        _FoamColor ("Foam Color", Color) = (1.00, 0.85, 0.85, 1)

        [Header(Surface Geometry)]
        _SurfaceWidth ("Surface Edge Width", Range(0.0, 0.2)) = 0.02
        _FoamWidth ("Foam Width", Range(0.0, 0.2)) = 0.0

        [Header(Fill and Wobble)]
        _FillAmount ("Fill Amount (world Y)", Range(-10, 10)) = 0.0
        _WobbleX ("Wobble X", Range(-1, 1)) = 0.0
        _WobbleZ ("Wobble Z", Range(-1, 1)) = 0.0

        [Header(PBR)]
        _Metallic ("Metallic", Range(0, 1)) = 1.0
        _Smoothness ("Smoothness", Range(0, 1)) = 1.0
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
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Keywords URP para iluminacion y sombras
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float  fillEdgeY   : TEXCOORD2;
                float  fogCoord    : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _TopColor;
                half4 _SurfaceColor;
                half4 _FoamColor;
                float _SurfaceWidth;
                float _FoamWidth;
                float _FillAmount;
                float _WobbleX;
                float _WobbleZ;
                float _Metallic;
                float _Smoothness;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.worldPos = worldPos;
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);

                // Wobble: la linea de llenado se inclina segun la posicion respecto al centro
                float3 worldOrigin = TransformObjectToWorld(float3(0, 0, 0));
                float dx = worldPos.x - worldOrigin.x;
                float dz = worldPos.z - worldOrigin.z;
                OUT.fillEdgeY = _FillAmount + dx * _WobbleX + dz * _WobbleZ;

                OUT.fogCoord = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag (Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // Cutoff: descartar lo que esta por encima del nivel
                float distFromTop = IN.fillEdgeY - IN.worldPos.y;
                if (distFromTop < 0) discard;

                // Color base segun la zona (superficie / foam / interior)
                half4 baseColor;
                if (distFromTop < _SurfaceWidth)
                {
                    baseColor = _SurfaceColor;
                }
                else if (_FoamWidth > 0 && distFromTop < (_SurfaceWidth + _FoamWidth))
                {
                    baseColor = _FoamColor;
                }
                else
                {
                    baseColor = isFrontFace ? _Color : _TopColor;
                }

                // Normal: se invierte en cara trasera para iluminacion correcta
                float3 normalWS = normalize(IN.worldNormal);
                if (!isFrontFace) normalWS = -normalWS;

                // Datos de superficie para PBR
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = baseColor.rgb;
                surfaceData.metallic   = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS   = float3(0, 0, 1);
                surfaceData.occlusion  = 1.0;
                surfaceData.alpha      = 1.0;

                // Input data para el pipeline lit
                InputData inputData = (InputData)0;
                inputData.positionWS       = IN.worldPos;
                inputData.normalWS         = normalWS;
                inputData.viewDirectionWS  = GetWorldSpaceNormalizeViewDir(IN.worldPos);
                inputData.shadowCoord      = TransformWorldToShadowCoord(IN.worldPos);
                inputData.fogCoord         = IN.fogCoord;
                inputData.bakedGI          = SampleSH(normalWS);
                inputData.shadowMask       = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, IN.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // Pass para que la jeringuilla proyecte sombras
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct ShadowAttr { float4 positionOS : POSITION; };
            struct ShadowVary { float4 positionCS : SV_POSITION; };

            ShadowVary ShadowVert(ShadowAttr IN)
            {
                ShadowVary OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 ShadowFrag(ShadowVary IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
