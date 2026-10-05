#version 450

// 頂点から渡された色は、三角形の内部でGPUによって補間されます。
layout(location = 0) in vec3 vertexColor;
layout(location = 0) out vec4 outputColor;

void main()
{
    outputColor = vec4(vertexColor, 1.0);
}
