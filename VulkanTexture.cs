using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

public sealed unsafe partial class VulkanApp
{
    private const uint TextureSize = 128;
    private const Format TextureFormat = Format.R8G8B8A8Srgb;
    private Image _texture;
    private DeviceMemory _textureMemory;
    private ImageView _textureView;
    private Sampler _textureSampler;
    private DescriptorSetLayout _textureSetLayout;
    private DescriptorPool _texturePool;
    private DescriptorSet _textureSet;
    private Buffer _textureStaging;
    private DeviceMemory _textureStagingMemory;

    private void CreateTexture()
    {
        _vk!.GetPhysicalDeviceFormatProperties(_physicalDevice, TextureFormat, out var properties);
        if ((properties.OptimalTilingFeatures & FormatFeatureFlags.SampledImageBit) == 0)
            throw new InvalidOperationException("RGBA8 sRGB形式のテクスチャを読み取れません。");

        // RGBAのピクセルをCPU側で生成。16pxのマスを8×8に並べます。
        var pixels = new byte[TextureSize * TextureSize * 4];
        for (uint y = 0; y < TextureSize; y++)
        for (uint x = 0; x < TextureSize; x++)
        {
            bool light = ((x / 16 + y / 16) & 1) == 0;
            int offset = checked((int)((y * TextureSize + x) * 4));
            pixels[offset] = light ? (byte)245 : (byte)230;
            pixels[offset + 1] = light ? (byte)235 : (byte)100;
            pixels[offset + 2] = light ? (byte)210 : (byte)40;
            pixels[offset + 3] = 255;
        }
        CreateHostBuffer((ulong)pixels.Length, BufferUsageFlags.TransferSrcBit,
            ref _textureStaging, ref _textureStagingMemory);
        fixed (byte* source = pixels)
            WriteHostMemory(_textureStagingMemory, source, (ulong)pixels.Length);

        // Optimal配置の画像へはCPUから直接書かず、ステージングバッファから転送します。
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo, ImageType = ImageType.Type2D,
            Format = TextureFormat, Extent = new Extent3D(TextureSize, TextureSize, 1),
            MipLevels = 1, ArrayLayers = 1, Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal, Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive, InitialLayout = ImageLayout.Undefined
        };
        Check(_vk.CreateImage(_device, in imageInfo, null, out _texture), "テクスチャ画像作成");
        _vk.GetImageMemoryRequirements(_device, _texture, out var requirements);
        var allocation = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        Check(_vk.AllocateMemory(_device, in allocation, null, out _textureMemory), "テクスチャメモリ確保");
        Check(_vk.BindImageMemory(_device, _texture, _textureMemory, 0), "画像メモリ関連付け");
        UploadTexture();
        // QueueWaitIdle後なので転送元のバッファを安全に解放できます。
        DestroyTextureStaging();

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo, Image = _texture,
            ViewType = ImageViewType.Type2D, Format = TextureFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        Check(_vk.CreateImageView(_device, in viewInfo, null, out _textureView), "テクスチャImageView作成");
        // Nearestでチェッカーの境界をくっきり表示。異方性やMipMapはまだ使いません。
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest, MinFilter = Filter.Nearest,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MaxAnisotropy = 1, MinLod = 0, MaxLod = 0
        };
        Check(_vk.CreateSampler(_device, in samplerInfo, null, out _textureSampler), "Sampler作成");
        CreateTextureDescriptor();
        Console.WriteLine($"テクスチャ: {TextureSize}×{TextureSize}、8×8のチェッカー模様。Descriptor Set: set 0 / binding 0。");
    }

    private void UploadTexture()
    {
        // 初期化中だけ描画用CommandBufferを転送に使用します。
        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        Check(_vk!.BeginCommandBuffer(_commandBuffer, in begin), "転送命令開始");
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _texture, SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            SrcAccessMask = 0, DstAccessMask = AccessFlags.TransferWriteBit
        };
        _vk.CmdPipelineBarrier(_commandBuffer, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit,
            0, 0, null, 0, null, 1, &barrier);
        var region = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(TextureSize, TextureSize, 1)
        };
        _vk.CmdCopyBufferToImage(_commandBuffer, _textureStaging, _texture, ImageLayout.TransferDstOptimal, 1, in region);
        // 転送の書き込み完了をフラグメントシェーダーの読み取りへつなぎます。
        barrier.OldLayout = ImageLayout.TransferDstOptimal;
        barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
        barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
        barrier.DstAccessMask = AccessFlags.ShaderReadBit;
        _vk.CmdPipelineBarrier(_commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, &barrier);
        Check(_vk.EndCommandBuffer(_commandBuffer), "転送命令終了");
        var command = _commandBuffer;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &command
        };
        Check(_vk.QueueSubmit(_queue, 1, in submit, default), "テクスチャ転送送信");
        // 初期化時だけ待機。描画中には画像転送やこの待機を繰り返しません。
        Check(_vk.QueueWaitIdle(_queue), "テクスチャ転送待機");
    }

    private void CreateTextureDescriptor()
    {
        // GLSLのlayout(set=0, binding=0) uniform sampler2Dに対応します。
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0, DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1, StageFlags = ShaderStageFlags.FragmentBit
        };
        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 1, PBindings = &binding
        };
        Check(_vk!.CreateDescriptorSetLayout(_device, in layoutInfo, null, out _textureSetLayout), "DescriptorSetLayout作成");
        var poolSize = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1);
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1,
            PoolSizeCount = 1, PPoolSizes = &poolSize
        };
        Check(_vk.CreateDescriptorPool(_device, in poolInfo, null, out _texturePool), "DescriptorPool作成");
        var layout = _textureSetLayout;
        var allocate = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = _texturePool,
            DescriptorSetCount = 1, PSetLayouts = &layout
        };
        Check(_vk.AllocateDescriptorSets(_device, in allocate, out _textureSet), "DescriptorSet確保");
        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _textureSampler, ImageView = _textureView, ImageLayout = ImageLayout.ShaderReadOnlyOptimal
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet, DstSet = _textureSet, DstBinding = 0,
            DescriptorCount = 1, DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = &imageInfo
        };
        _vk.UpdateDescriptorSets(_device, 1, in write, 0, null);
    }

    private void DestroyTextureStaging()
    {
        if (_textureStaging.Handle != 0) _vk!.DestroyBuffer(_device, _textureStaging, null);
        if (_textureStagingMemory.Handle != 0) _vk!.FreeMemory(_device, _textureStagingMemory, null);
        _textureStaging = default;
        _textureStagingMemory = default;
    }

    private void DestroyTexture()
    {
        // 呼び出し元がGPUを待ち、Pipelineを破棄済み。SetはPoolと一緒に解放されます。
        if (_texturePool.Handle != 0) _vk!.DestroyDescriptorPool(_device, _texturePool, null);
        if (_textureSetLayout.Handle != 0) _vk!.DestroyDescriptorSetLayout(_device, _textureSetLayout, null);
        if (_textureSampler.Handle != 0) _vk!.DestroySampler(_device, _textureSampler, null);
        if (_textureView.Handle != 0) _vk!.DestroyImageView(_device, _textureView, null);
        if (_texture.Handle != 0) _vk!.DestroyImage(_device, _texture, null);
        if (_textureMemory.Handle != 0) _vk!.FreeMemory(_device, _textureMemory, null);
        DestroyTextureStaging();
        _texturePool = default; _textureSetLayout = default; _textureSet = default;
        _textureSampler = default; _textureView = default; _texture = default; _textureMemory = default;
    }
}
