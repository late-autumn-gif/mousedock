// Compile with Windows SDK fxc /T ps_2_0 /E main /Fo PetHandEffect.ps PetHandEffect.hlsl
sampler2D artwork : register(s0);
float2 mouseOffset : register(c0);
float keyStroke : register(c1);
float2 anchors : register(c2); // Mouse and keyboard hand X positions in the pose atlas cell.

float handWeight(float2 uv, float2 center, float2 radii)
{
    float2 p = (uv - center) / radii;
    float t = saturate(1.0 - dot(p, p));
    return t * t * (3.0 - 2.0 * t);
}

float4 main(float2 uv : TEXCOORD0) : COLOR
{
    // Deform each complete drawing locally, fading to zero before the face and desk rim.
    float mouse = handWeight(uv, float2(anchors.x, 0.818) + mouseOffset * 0.5, float2(0.165, 0.135));
    float key = handWeight(uv, float2(anchors.y, 0.778), float2(0.128, 0.115));
    return tex2D(artwork, uv - mouse * mouseOffset - float2(0, key * keyStroke));
}
