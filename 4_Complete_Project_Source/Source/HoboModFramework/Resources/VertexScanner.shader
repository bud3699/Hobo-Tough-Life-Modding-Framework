// VertexScanner.shader
// This shader "photographs" vertex positions from a mesh onto a RenderTexture.
// Instead of rendering colors, it encodes vertex positions (x, y, z) as pixel data.
// Uses a Geometry Shader to convert triangles into independent points at specific pixels.

Shader "Hidden/HoboMod/VertexScanner"
{
    Properties
    {
        _TextureWidth ("Texture Width", Float) = 4096
        _TextureHeight ("Texture Height", Float) = 4096
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest Always
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma target 4.0
            
            #include "UnityCG.cginc"
            
            float _TextureWidth;
            float _TextureHeight;
            
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                uint vertexId : SV_VertexID;
            };
            
            struct v2g
            {
                float4 vertex : TEXCOORD0;      // Object space position
                float3 normal : TEXCOORD1;      // Object space normal
                float2 uv : TEXCOORD2;          // UV coordinates
                float4 worldPos : TEXCOORD3;    // World space position
                uint vertexId : TEXCOORD4;      // Vertex index
            };
            
            struct g2f
            {
                float4 pos : SV_POSITION;       // Clip space position (mapped to pixel)
                float4 worldPos : TEXCOORD0;    // World position to output as color
                float3 normal : TEXCOORD1;      // Normal to potentially output
            };
            
            // Vertex Shader: Pass through data
            v2g vert(appdata v)
            {
                v2g o;
                o.vertex = v.vertex;
                o.normal = v.normal;
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.vertexId = v.vertexId;
                return o;
            }
            
            // Geometry Shader: Convert each triangle vertex into a point at a specific pixel
            [maxvertexcount(3)]
            void geom(triangle v2g input[3], inout PointStream<g2f> stream)
            {
                for (int i = 0; i < 3; i++)
                {
                    g2f o;
                    
                    // Map vertex ID to pixel coordinates
                    uint id = input[i].vertexId;
                    float x = fmod(id, _TextureWidth);
                    float y = floor(id / _TextureWidth);
                    
                    // Convert pixel coords to clip space (-1 to 1)
                    // Adding 0.5 to center on pixel
                    float clipX = ((x + 0.5) / _TextureWidth) * 2.0 - 1.0;
                    float clipY = ((y + 0.5) / _TextureHeight) * 2.0 - 1.0;
                    
                    o.pos = float4(clipX, clipY, 0, 1);
                    o.worldPos = input[i].worldPos;
                    o.normal = input[i].normal;
                    
                    stream.Append(o);
                }
            }
            
            // Fragment Shader: Output world position as color
            float4 frag(g2f i) : SV_Target
            {
                // Encode world position as RGBA
                // Using ARGBFloat format, so we can store full float precision
                return float4(i.worldPos.x, i.worldPos.y, i.worldPos.z, 1.0);
            }
            ENDCG
        }
    }
    
    Fallback Off
}
