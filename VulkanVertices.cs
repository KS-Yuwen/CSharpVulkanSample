using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

public sealed unsafe partial class VulkanApp
{
    // GLSLのvec2位置 + vec3色。各floatを連続配置し、strideは20バイトです。
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Vertex(float x, float y, float r, float g, float b)
    {
        public float X = x, Y = y, R = r, G = g, B = b;
    }

    private Buffer _vertexBuffer;
    private DeviceMemory _vertexMemory;
    private double _rotationAngle;
    private const double RotationSpeed = Math.PI / 6; // 毎秒30度、12秒で1回転。

    private void CreateVertexBuffer()
    {
        // 重心が原点の三角形。回転しても中心が移動せず、Viewport内に収まります。
        Vertex* vertices = stackalloc Vertex[3]
        {
            new(0f, -0.6f, 1f, 0f, 0f),
            new(-0.52f, 0.3f, 0f, 1f, 0f),
            new(0.52f, 0.3f, 0f, 0f, 1f)
        };
        ulong size = (ulong)(sizeof(Vertex) * 3);
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo, Size = size,
            Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive
        };
        Check(_vk!.CreateBuffer(_device, in bufferInfo, null, out _vertexBuffer), "頂点バッファ作成");
        _vk.GetBufferMemoryRequirements(_device, _vertexBuffer, out var requirements);
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var properties);
        uint memoryType = uint.MaxValue;
        var required = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        for (uint i = 0; i < properties.MemoryTypeCount; i++)
        {
            if ((requirements.MemoryTypeBits & (1u << (int)i)) != 0 &&
                (properties.MemoryTypes[(int)i].PropertyFlags & required) == required)
            { memoryType = i; break; }
        }
        if (memoryType == uint.MaxValue)
            throw new InvalidOperationException("CPUから書き込めるHostVisible + HostCoherentメモリがありません。");
        var allocation = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size, MemoryTypeIndex = memoryType
        };
        Check(_vk.AllocateMemory(_device, in allocation, null, out _vertexMemory), "頂点メモリ確保");
        Check(_vk.BindBufferMemory(_device, _vertexBuffer, _vertexMemory, 0), "頂点メモリ関連付け");
        void* mapped = null;
        Check(_vk.MapMemory(_device, _vertexMemory, 0, size, 0, &mapped), "頂点メモリマップ");
        try { System.Buffer.MemoryCopy(vertices, mapped, size, size); }
        finally { _vk.UnmapMemory(_device, _vertexMemory); }
        // HostCoherentなのでFlushは不要。GPUへの送信前に一度だけ書き込みます。
        Console.WriteLine($"頂点バッファ: 3頂点、{size}バイト。回転速度: 毎秒30度。");
    }

    private void DestroyVertexBuffer()
    {
        if (_vertexBuffer.Handle != 0) _vk!.DestroyBuffer(_device, _vertexBuffer, null);
        if (_vertexMemory.Handle != 0) _vk!.FreeMemory(_device, _vertexMemory, null);
        _vertexBuffer = default;
        _vertexMemory = default;
    }
}
