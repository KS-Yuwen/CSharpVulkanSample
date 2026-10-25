#version 450

// C#の頂点バッファから位置とUV（画像上の座標）を受け取ります。
layout(location = 0) in vec2 position;
layout(location = 1) in vec2 uv;
layout(push_constant) uniform Rotation
{
    float angle; // C#から渡されるラジアン。PipelineLayoutと同じ4バイトです。
} rotation;

layout(location = 0) out vec2 textureUV;

void main()
{
    float c = cos(rotation.angle);
    float s = sin(rotation.angle);
    vec2 rotated = vec2(c * position.x - s * position.y,
                        s * position.x + c * position.y);
    // 正の高さのViewportではYが下向きなので、画面上では時計回りです。
    gl_Position = vec4(rotated, 0.0, 1.0);
    textureUV = uv;
}
