using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

public sealed unsafe partial class VulkanApp
{
    // GLSLのvec2位置 + vec2 UV。各floatを連続配置し、strideは16バイトです。
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Vertex(float x, float y, float u, float v)
    {
        public float X = x, Y = y, U = u, V = v;
    }

    private Buffer _vertexBuffer;
    private DeviceMemory _vertexMemory;
    private Buffer _indexBuffer;
    private DeviceMemory _indexMemory;
    private double _rotationAngle;
    private const double RotationSpeed = Math.PI / 6; // 毎秒30度、12秒で1回転。

    private void CreateVertexBuffer()
    {
        // 中心が原点の四角形。UVの(0,0)が画像の左上、(1,1)が右下です。
        Vertex* vertices = stackalloc Vertex[4]
        {
            new(-0.5f, -0.5f, 0f, 0f),
            new( 0.5f, -0.5f, 1f, 0f),
            new( 0.5f,  0.5f, 1f, 1f),
            new(-0.5f,  0.5f, 0f, 1f)
        };
        CreateHostBuffer((ulong)(sizeof(Vertex) * 4), BufferUsageFlags.VertexBufferBit,
            ref _vertexBuffer, ref _vertexMemory);
        WriteHostMemory(_vertexMemory, vertices, (ulong)(sizeof(Vertex) * 4));
        // 対角線で分けた2枚の三角形。4頂点を6個のインデックスで共有します。
        ushort* indices = stackalloc ushort[6] { 0, 1, 2, 2, 3, 0 };
        CreateHostBuffer(6 * sizeof(ushort), BufferUsageFlags.IndexBufferBit,
            ref _indexBuffer, ref _indexMemory);
        WriteHostMemory(_indexMemory, indices, 6 * sizeof(ushort));
        Console.WriteLine("四角形: 4頂点（64バイト）、6インデックス（12バイト）。回転速度: 毎秒30度。");
    }

    private uint FindMemoryType(uint typeBits, MemoryPropertyFlags required)
    {
        _vk!.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var properties);
        for (uint i = 0; i < properties.MemoryTypeCount; i++)
            if ((typeBits & (1u << (int)i)) != 0 &&
                (properties.MemoryTypes[(int)i].PropertyFlags & required) == required)
                return i;
        throw new InvalidOperationException($"必要なGPUメモリがありません: {required}");
    }

    // 頂点・インデックス・画像転送用の、CPUから書き込めるバッファを作ります。
    // refでフィールドへ順次保存し、途中で失敗してもDisposeで解放できるようにします。
    private void CreateHostBuffer(ulong size, BufferUsageFlags usage, ref Buffer buffer, ref DeviceMemory memory)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo, Size = size,
            Usage = usage, SharingMode = SharingMode.Exclusive
        };
        Check(_vk!.CreateBuffer(_device, in bufferInfo, null, out buffer), "バッファ作成");
        _vk.GetBufferMemoryRequirements(_device, buffer, out var requirements);
        var allocation = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };
        Check(_vk.AllocateMemory(_device, in allocation, null, out memory), "バッファメモリ確保");
        Check(_vk.BindBufferMemory(_device, buffer, memory, 0), "バッファメモリ関連付け");
    }

    private void WriteHostMemory(DeviceMemory memory, void* source, ulong size)
    {
        void* mapped = null;
        Check(_vk!.MapMemory(_device, memory, 0, size, 0, &mapped), "メモリマップ");
        try { System.Buffer.MemoryCopy(source, mapped, size, size); }
        finally { _vk.UnmapMemory(_device, memory); }
        // HostCoherentなのでFlushは不要。GPUへの送信前に一度だけ書き込みます。
    }

    private void DestroyVertexBuffer()
    {
        if (_indexBuffer.Handle != 0) _vk!.DestroyBuffer(_device, _indexBuffer, null);
        if (_indexMemory.Handle != 0) _vk!.FreeMemory(_device, _indexMemory, null);
        if (_vertexBuffer.Handle != 0) _vk!.DestroyBuffer(_device, _vertexBuffer, null);
        if (_vertexMemory.Handle != 0) _vk!.FreeMemory(_device, _vertexMemory, null);
        _vertexBuffer = default;
        _vertexMemory = default;
        _indexBuffer = default;
        _indexMemory = default;
    }
}
