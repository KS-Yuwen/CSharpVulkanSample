using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

public sealed unsafe partial class VulkanApp
{
    private KhrSurface? _surfaceApi;
    private KhrSwapchain? _swapchainApi;
    private SurfaceKHR _surface;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _queue;
    private uint _queueFamily;
    private SwapchainKHR _swapchain;
    private Extent2D _extent;
    private ImageView[] _views = [];
    private Framebuffer[] _framebuffers = [];
    private Semaphore[] _presentReady = [];
    private RenderPass _renderPass;
    private CommandPool _commandPool;
    private CommandBuffer _commandBuffer;
    private Semaphore _imageAvailable;
    private Fence _frameFence;
    private bool _resizeRequested;
    private int _presentedFrames;

    private void CreateRenderer()
    {
        if (!_vk!.TryGetInstanceExtension(_instance, out _surfaceApi))
            throw new InvalidOperationException("Surface拡張を取得できません。");
        _surface = _window.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();
        SelectDevice();

        // まずは描画と表示を同じQueueで処理できるGPUを使います。
        float priority = 1;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _queueFamily, QueueCount = 1, PQueuePriorities = &priority
        };
        var extension = SilkMarshal.StringToPtr(KhrSwapchain.ExtensionName);
        try
        {
            byte* extensionName = (byte*)extension;
            var info = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1, PQueueCreateInfos = &queueInfo,
                EnabledExtensionCount = 1, PpEnabledExtensionNames = &extensionName
            };
            Check(_vk.CreateDevice(_physicalDevice, in info, null, out _device), "Device作成");
        }
        finally { SilkMarshal.Free(extension); }
        _vk.GetDeviceQueue(_device, _queueFamily, 0, out _queue);
        if (!_vk.TryGetDeviceExtension(_instance, _device, out _swapchainApi))
            throw new InvalidOperationException("Swapchain拡張を取得できません。");

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = _queueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };
        Check(_vk.CreateCommandPool(_device, in poolInfo, null, out _commandPool), "CommandPool作成");
        var allocate = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo, CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary, CommandBufferCount = 1
        };
        Check(_vk.AllocateCommandBuffers(_device, in allocate, out _commandBuffer), "CommandBuffer作成");
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        Check(_vk.CreateSemaphore(_device, in semaphoreInfo, null, out _imageAvailable), "画像取得Semaphore作成");
        // 最初のフレームは待たずに進めるよう、Fenceを完了済みで作ります。
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        Check(_vk.CreateFence(_device, in fenceInfo, null, out _frameFence), "Fence作成");
        CreateSwapchain();
    }

    private void SelectDevice()
    {
        uint count = 0;
        Check(_vk!.EnumeratePhysicalDevices(_instance, ref count, null), "GPU件数取得");
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices)
            Check(_vk.EnumeratePhysicalDevices(_instance, ref count, p), "GPU列挙");
        foreach (var device in devices)
        {
            uint extensionCount = 0;
            Check(_vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extensionCount, null), "Device拡張件数取得");
            var extensions = new ExtensionProperties[extensionCount];
            fixed (ExtensionProperties* p = extensions)
                Check(_vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extensionCount, p), "Device拡張取得");
            bool hasSwapchain = false;
            for (int i = 0; i < extensionCount; i++)
            {
                var extension = extensions[i];
                if (SilkMarshal.PtrToString((nint)extension.ExtensionName) == KhrSwapchain.ExtensionName)
                    hasSwapchain = true;
            }
            if (!hasSwapchain) continue;
            uint queueCount = 0;
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueCount, null);
            var families = new QueueFamilyProperties[queueCount];
            fixed (QueueFamilyProperties* p = families)
                _vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueCount, p);
            for (uint i = 0; i < queueCount; i++)
            {
                Check(_surfaceApi!.GetPhysicalDeviceSurfaceSupport(device, i, _surface, out var canPresent), "表示対応確認");
                if (families[i].QueueCount == 0 || (families[i].QueueFlags & QueueFlags.GraphicsBit) == 0 || !canPresent) continue;
                Check(_surfaceApi.GetPhysicalDeviceSurfaceCapabilities(device, _surface, out var caps), "Surface能力取得");
                uint formats = 0, modes = 0;
                Check(_surfaceApi.GetPhysicalDeviceSurfaceFormats(device, _surface, ref formats, null), "形式件数取得");
                Check(_surfaceApi.GetPhysicalDeviceSurfacePresentModes(device, _surface, ref modes, null), "表示モード件数取得");
                if (formats == 0 || modes == 0 || (caps.SupportedUsageFlags & ImageUsageFlags.ColorAttachmentBit) == 0) continue;
                _physicalDevice = device;
                _queueFamily = i;
                _vk.GetPhysicalDeviceProperties(device, out var properties);
                Console.WriteLine($"描画GPU: {SilkMarshal.PtrToString((nint)properties.DeviceName)}, Queue family: {i}");
                return;
            }
        }
        throw new InvalidOperationException("同じQueueで描画と表示ができるVulkan GPUが見つかりません。");
    }

    private void CreateSwapchain()
    {
        Check(_surfaceApi!.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out var caps), "Surface能力取得");
        uint count = 0;
        Check(_surfaceApi.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref count, null), "Surface形式件数取得");
        var formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* p = formats)
            Check(_surfaceApi.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, ref count, p), "Surface形式取得");
        var format = formats[0];
        if (count == 1 && format.Format == Format.Undefined)
            format.Format = Format.B8G8R8A8Srgb;
        foreach (var candidate in formats)
            if (candidate.Format == Format.B8G8R8A8Srgb && candidate.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr) format = candidate;
        var size = _window.FramebufferSize;
        _extent = caps.CurrentExtent.Width != uint.MaxValue ? caps.CurrentExtent : new Extent2D
        {
            Width = Math.Clamp((uint)size.X, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
            Height = Math.Clamp((uint)size.Y, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
        };
        uint imageCount = caps.MinImageCount + 1;
        if (caps.MaxImageCount > 0) imageCount = Math.Min(imageCount, caps.MaxImageCount);
        var alpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
        foreach (var candidate in new[] { CompositeAlphaFlagsKHR.OpaqueBitKhr, CompositeAlphaFlagsKHR.PreMultipliedBitKhr, CompositeAlphaFlagsKHR.PostMultipliedBitKhr, CompositeAlphaFlagsKHR.InheritBitKhr })
            if ((caps.SupportedCompositeAlpha & candidate) != 0) { alpha = candidate; break; }
        var info = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr, Surface = _surface,
            MinImageCount = imageCount, ImageFormat = format.Format, ImageColorSpace = format.ColorSpace,
            ImageExtent = _extent, ImageArrayLayers = 1, ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = SharingMode.Exclusive, PreTransform = caps.CurrentTransform,
            CompositeAlpha = alpha, PresentMode = PresentModeKHR.FifoKhr, Clipped = true
        };
        Check(_swapchainApi!.CreateSwapchain(_device, in info, null, out _swapchain), "Swapchain作成");
        count = 0;
        Check(_swapchainApi.GetSwapchainImages(_device, _swapchain, ref count, null), "画像件数取得");
        var images = new Image[count];
        fixed (Image* p = images)
            Check(_swapchainApi.GetSwapchainImages(_device, _swapchain, ref count, p), "画像取得");
        _views = new ImageView[count];
        _framebuffers = new Framebuffer[count];
        _presentReady = new Semaphore[count];
        CreateRenderPass(format.Format);
        for (int i = 0; i < count; i++)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo, Image = images[i], ViewType = ImageViewType.Type2D,
                Format = format.Format,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            Check(_vk!.CreateImageView(_device, in viewInfo, null, out _views[i]), "ImageView作成");
            var view = _views[i];
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo, RenderPass = _renderPass,
                AttachmentCount = 1, PAttachments = &view, Width = _extent.Width, Height = _extent.Height, Layers = 1
            };
            Check(_vk.CreateFramebuffer(_device, in framebufferInfo, null, out _framebuffers[i]), "Framebuffer作成");
            // 表示完了Semaphoreは画像ごとに用意し、表示処理中の再利用を防ぎます。
            var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            Check(_vk.CreateSemaphore(_device, in semaphoreInfo, null, out _presentReady[i]), "表示Semaphore作成");
        }
        _resizeRequested = false;
        Console.WriteLine($"Swapchain: {_extent.Width}×{_extent.Height}, {count}画像, {format.Format}");
    }

    private void CreateRenderPass(Format format)
    {
        // LoadOp.Clearが背景色を書き込みます。シェーダーやPipelineはまだ不要です。
        // 前の画像内容を捨て、終了時に画面表示用のレイアウトへ移行します。
        var attachment = new AttachmentDescription
        {
            Format = format, Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare, StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined, FinalLayout = ImageLayout.PresentSrcKhr
        };
        var reference = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics, ColorAttachmentCount = 1, PColorAttachments = &reference
        };
        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal, DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };
        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo, AttachmentCount = 1, PAttachments = &attachment,
            SubpassCount = 1, PSubpasses = &subpass, DependencyCount = 1, PDependencies = &dependency
        };
        Check(_vk!.CreateRenderPass(_device, in info, null, out _renderPass), "RenderPass作成");
    }

    private void DrawFrame(double delta)
    {
        // 最小化中の0×0画像は作らず、復元された後で再開します。
        var size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0) return;
        if (_resizeRequested)
        {
            Check(_vk!.DeviceWaitIdle(_device), "リサイズ前のGPU待機");
            DestroySwapchain();
            CreateSwapchain();
        }
        // 学習用に同時実行フレームを1つに限定します。
        Check(_vk!.WaitForFences(_device, 1, in _frameFence, true, ulong.MaxValue), "描画完了待機");
        uint imageIndex = 0;
        var acquired = _swapchainApi!.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _imageAvailable, default, &imageIndex);
        if (acquired == Result.ErrorOutOfDateKhr) { _resizeRequested = true; return; }
        if (acquired != Result.SuboptimalKhr) Check(acquired, "画像取得");
        Check(_vk.ResetCommandBuffer(_commandBuffer, 0), "描画命令リセット");
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        Check(_vk.BeginCommandBuffer(_commandBuffer, in begin), "描画命令開始");
        // RGBA。sRGB形式では線形値から表示用の色へ変換されます。
        var clear = new ClearValue { Color = new ClearColorValue(0.02f, 0.12f, 0.45f, 1f) };
        var render = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo, RenderPass = _renderPass,
            Framebuffer = _framebuffers[imageIndex], RenderArea = new Rect2D(default, _extent),
            ClearValueCount = 1, PClearValues = &clear
        };
        _vk.CmdBeginRenderPass(_commandBuffer, in render, SubpassContents.Inline);
        _vk.CmdEndRenderPass(_commandBuffer);
        Check(_vk.EndCommandBuffer(_commandBuffer), "描画命令終了");
        var wait = _imageAvailable;
        var signal = _presentReady[imageIndex];
        var command = _commandBuffer;
        var stage = PipelineStageFlags.ColorAttachmentOutputBit;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo, WaitSemaphoreCount = 1, PWaitSemaphores = &wait,
            PWaitDstStageMask = &stage, CommandBufferCount = 1, PCommandBuffers = &command,
            SignalSemaphoreCount = 1, PSignalSemaphores = &signal
        };
        Check(_vk.ResetFences(_device, 1, in _frameFence), "Fenceリセット");
        Check(_vk.QueueSubmit(_queue, 1, in submit, _frameFence), "描画命令送信");
        var swapchain = _swapchain;
        var present = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr, WaitSemaphoreCount = 1, PWaitSemaphores = &signal,
            SwapchainCount = 1, PSwapchains = &swapchain, PImageIndices = &imageIndex
        };
        var result = _swapchainApi.QueuePresent(_queue, in present);
        if (result is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr || acquired == Result.SuboptimalKhr)
            _resizeRequested = true;
        else Check(result, "画面表示");
        if (result == Result.Success || result == Result.SuboptimalKhr)
        {
            _presentedFrames++;
            if (_presentedFrames == 1) Console.WriteLine("青色の背景を描画し、画面へ送信しました。");
            // 起動確認ではリサイズとSwapchain再作成も通します。
            if (_smokeTest && _presentedFrames == 20) _window.Size = new(800, 450);
            if (_smokeTest && _presentedFrames == 40) _window.Size = new(960, 540);
            // 複数画像と同期の再利用も確認してから終了します。
            if (_smokeTest && _presentedFrames >= 60)
            {
                Check(_vk.DeviceWaitIdle(_device), "起動確認のGPU待機");
                Console.WriteLine($"smoke-test完了: {_presentedFrames}フレームを表示しました。");
                _window.Close();
            }
        }
    }

    private void DestroySwapchain()
    {
        foreach (var framebuffer in _framebuffers)
            if (framebuffer.Handle != 0) _vk!.DestroyFramebuffer(_device, framebuffer, null);
        foreach (var view in _views)
            if (view.Handle != 0) _vk!.DestroyImageView(_device, view, null);
        foreach (var semaphore in _presentReady)
            if (semaphore.Handle != 0) _vk!.DestroySemaphore(_device, semaphore, null);
        if (_renderPass.Handle != 0) _vk!.DestroyRenderPass(_device, _renderPass, null);
        if (_swapchain.Handle != 0) _swapchainApi!.DestroySwapchain(_device, _swapchain, null);
        _framebuffers = []; _views = []; _presentReady = [];
        _renderPass = default; _swapchain = default;
    }

    private void DestroyRenderer()
    {
        // 作成順の逆に破棄。途中で初期化に失敗した場合も作成済みのものだけ解放します。
        if (_device.Handle != 0)
        {
            _vk!.DeviceWaitIdle(_device);
            DestroySwapchain();
            if (_frameFence.Handle != 0) _vk.DestroyFence(_device, _frameFence, null);
            if (_imageAvailable.Handle != 0) _vk.DestroySemaphore(_device, _imageAvailable, null);
            if (_commandPool.Handle != 0) _vk.DestroyCommandPool(_device, _commandPool, null);
            _vk.DestroyDevice(_device, null);
        }
        _swapchainApi?.Dispose();
        if (_surface.Handle != 0) _surfaceApi!.DestroySurface(_instance, _surface, null);
        _surfaceApi?.Dispose();
    }
}
