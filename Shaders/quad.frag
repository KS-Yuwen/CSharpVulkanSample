#version 450

// 頂点のUVは三角形の内部で補間され、その座標でテクスチャを読みます。
layout(location = 0) in vec2 textureUV;
layout(set = 0, binding = 0) uniform sampler2D checkerTexture;
layout(location = 0) out vec4 outputColor;

void main()
{
    outputColor = texture(checkerTexture, textureUV);
}
