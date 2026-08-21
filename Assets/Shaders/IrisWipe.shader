// Shader de cierre de iris para efectos de Game Over y vineteo de poca vida.
//
// FORMA:
//   Por defecto (ScaleX=1, ScaleY=1) el iris es un ovalo natural de pantalla:
//   en 16:9 aparece mas ancho que alto, como un ojo.
//   Sube ScaleX para un ovalo mas horizontal.
//   Sube ScaleY para un ovalo mas vertical.
//   Para circulo perfecto en 16:9: ScaleX = 1.777 (= 16/9).
//
// BORDES:
//   Softness controla cuanto se difuminan los bordes.
//   0.01 = borde duro. 0.3-0.5 = borde muy suave y degradado.
//
// COLOR:
//   Cambia el Tint del material para distintos colores:
//     Negro  #000000 -> cierre de muerte
//     Rojo   #660000 -> vineteo de poca vida

Shader "UI/IrisWipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color    ("Tint",                           Color)       = (0, 0, 0, 1)
        _Progress ("Progress  (0 = abierto, 1 = cerrado)", Range(0, 1))   = 0
        _Softness ("Suavizado del borde",            Range(0.01, 0.5))    = 0.22
        _ScaleX   ("Escala horizontal (oval)",       Range(0.1, 4.0))     = 1.0
        _ScaleY   ("Escala vertical   (oval)",       Range(0.1, 4.0))     = 1.0

        // UI internals — no tocar
        [HideInInspector] _StencilComp     ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil         ("Stencil ID",         Float) = 0
        [HideInInspector] _StencilOp       ("Stencil Operation",  Float) = 0
        [HideInInspector] _StencilWriteMask("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask",  Float) = 255
        [HideInInspector] _ColorMask       ("Color Mask",         Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType"      = "Transparent"
            "PreviewType"     = "Plane"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull      Off
        Lighting  Off
        ZWrite    Off
        ZTest     [unity_GUIZTestMode]
        Blend     SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color    : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 color         : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            float4    _ClipRect;
            float     _Progress;
            float     _Softness;
            float     _ScaleX;
            float     _ScaleY;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex   = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color    = v.color * _Color;
                return OUT;
            }

            float4 frag(v2f IN) : SV_Target
            {
                // UV centradas en (0,0); rango -0.5..0.5 en ambos ejes.
                float2 uv = IN.texcoord - 0.5;

                // Escalar para forma del ovalo.
                // Sin correccion de AR el ovalo sigue las proporciones de pantalla
                // (mas ancho que alto en 16:9). ScaleX/ScaleY permiten ajuste fino.
                float2 scaledUV = float2(uv.x / _ScaleX, uv.y / _ScaleY);
                float dist = length(scaledUV);

                // Distancia maxima: esquina mas alejada del centro en el espacio escalado.
                float maxDist = length(float2(0.5 / _ScaleX, 0.5 / _ScaleY)) + _Softness;

                // Radio del agujero visible:
                //   Progress=0 -> holeRadius=maxDist  (toda la pantalla visible)
                //   Progress=1 -> holeRadius=-Softness (todo negro, incluido el centro)
                float holeRadius = lerp(-_Softness, maxDist, 1.0 - _Progress);

                // Alpha: 0 dentro del agujero (visible), 1 fuera (color del tint).
                // El ancho de la zona de transicion es _Softness -> mas alto = mas difuminado.
                float irisAlpha = smoothstep(holeRadius, holeRadius + _Softness, dist);

                float4 color = IN.color;
                color.a *= irisAlpha;
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);

                clip(color.a - 0.001);
                return color;
            }
            ENDCG
        }
    }
}
