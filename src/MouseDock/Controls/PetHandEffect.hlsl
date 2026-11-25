sampler2D artwork : register(s0);
float2 displacement : register(c0);
float stroke : register(c1);
float2 joint : register(c2); // shoulder and cuff y in source texture coordinates
float4 main(float2 uv : TEXCOORD0) : COLOR
{
    float2 shift = displacement + float2(0, stroke);
    // Invert the shoulder-to-cuff stretch in destination coordinates. The entire
    // palm below the cuff translates rigidly, including its painted mouse shell.
    float weight = saturate((uv.y - joint.x) / max(.01, joint.y + shift.y - joint.x));
    return tex2D(artwork, uv - shift * weight);
}
