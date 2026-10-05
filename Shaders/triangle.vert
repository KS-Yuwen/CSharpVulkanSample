#version 450

// 今回は頂点バッファを使わず、頂点の位置と色をシェーダー内に定義します。
// Vulkanの正の高さのViewportでは、画面のY軸は下向きです。
const vec2 positions[3] = vec2[](
    vec2( 0.0, -0.6),  // 上
    vec2(-0.6,  0.6),  // 左下
    vec2( 0.6,  0.6)   // 右下
);
const vec3 colors[3] = vec3[](
    vec3(1.0, 0.0, 0.0),
    vec3(0.0, 1.0, 0.0),
    vec3(0.0, 0.0, 1.0)
);

layout(location = 0) out vec3 vertexColor;

void main()
{
    // CmdDrawの頂点番号0・1・2で、位置と色を取り出します。
    gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
    vertexColor = colors[gl_VertexIndex];
}
