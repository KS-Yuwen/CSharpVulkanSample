using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

public sealed unsafe partial class VulkanApp
{
    private PipelineLayout _pipelineLayout;
    private Pipeline _graphicsPipeline;

    private ShaderModule LoadShader(string filename)
    {
        // カレントディレクトリに依存せず、実行ファイルと一緒に配置したSPIR-Vを読みます。
        var path = Path.Combine(AppContext.BaseDirectory, "Shaders", filename);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes.Length % sizeof(uint) != 0)
            throw new InvalidOperationException($"SPIR-Vのサイズが不正です: {path}");
        // Vulkanが要求する32bit境界に揃えた配列へコピーします。
        var words = new uint[bytes.Length / sizeof(uint)];
        System.Buffer.BlockCopy(bytes, 0, words, 0, bytes.Length);
        if (words[0] != 0x07230203)
            throw new InvalidOperationException($"SPIR-Vの識別子が不正です: {path}");
        fixed (uint* code = words)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)bytes.Length, PCode = code
            };
            Check(_vk!.CreateShaderModule(_device, in info, null, out var module), "ShaderModule作成");
            return module;
        }
    }

    private void CreateGraphicsPipeline()
    {
        ShaderModule vertex = default, fragment = default;
        var entryPoint = SilkMarshal.StringToPtr("main");
        try
        {
            vertex = LoadShader("triangle.vert.spv");
            fragment = LoadShader("triangle.frag.spv");
            // シェーダーはPipeline作成時に読み込まれ、作成後はModuleを解放できます。
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit, Module = vertex, PName = (byte*)entryPoint
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit, Module = fragment, PName = (byte*)entryPoint
            };

            // 頂点はgl_VertexIndexでシェーダー内から取得するため、頂点入力は空です。
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo
            };
            var assembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList
            };
            // ViewportとScissorの実際の値は毎フレームCmdSet...で指定します。
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1, ScissorCount = 1
            };
            var rasterization = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise, LineWidth = 1f
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };
            // 透明合成は使わず、シェーダーのRGBAをそのまま書き込みます。
            var colorAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit
            };
            var blend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1, PAttachments = &colorAttachment
            };
            var states = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2, PDynamicStates = states
            };
            // DescriptorやPush Constantはまだ使わないため、空のLayoutです。
            var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo };
            Check(_vk!.CreatePipelineLayout(_device, in layoutInfo, null, out _pipelineLayout), "PipelineLayout作成");
            var info = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2, PStages = stages, PVertexInputState = &vertexInput,
                PInputAssemblyState = &assembly, PViewportState = &viewportState,
                PRasterizationState = &rasterization, PMultisampleState = &multisample,
                PColorBlendState = &blend, PDynamicState = &dynamicState,
                Layout = _pipelineLayout, RenderPass = _renderPass, Subpass = 0,
                BasePipelineIndex = -1
            };
            Check(_vk.CreateGraphicsPipelines(_device, default, 1, in info, null, out _graphicsPipeline), "Graphics Pipeline作成");
        }
        finally
        {
            if (fragment.Handle != 0) _vk!.DestroyShaderModule(_device, fragment, null);
            if (vertex.Handle != 0) _vk!.DestroyShaderModule(_device, vertex, null);
            SilkMarshal.Free(entryPoint);
        }
    }

    private void RecordTriangle()
    {
        _vk!.CmdBindPipeline(_commandBuffer, PipelineBindPoint.Graphics, _graphicsPipeline);
        // 中央に正方形のViewportを置き、横長・縦長でも三角形の縦横比を保ちます。
        float side = Math.Min(_extent.Width, _extent.Height);
        var viewport = new Viewport
        {
            X = (_extent.Width - side) / 2f, Y = (_extent.Height - side) / 2f,
            Width = side, Height = side, MinDepth = 0f, MaxDepth = 1f
        };
        var scissor = new Rect2D(default, _extent);
        _vk.CmdSetViewport(_commandBuffer, 0, 1, in viewport);
        _vk.CmdSetScissor(_commandBuffer, 0, 1, in scissor);
        // 3頂点で1つの三角形を描きます。頂点バッファは不要です。
        _vk.CmdDraw(_commandBuffer, 3, 1, 0, 0);
    }

    private void DestroyGraphicsPipeline()
    {
        if (_graphicsPipeline.Handle != 0) _vk!.DestroyPipeline(_device, _graphicsPipeline, null);
        if (_pipelineLayout.Handle != 0) _vk!.DestroyPipelineLayout(_device, _pipelineLayout, null);
        _graphicsPipeline = default;
        _pipelineLayout = default;
    }
}
