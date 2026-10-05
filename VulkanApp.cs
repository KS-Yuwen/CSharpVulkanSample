using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

// unsafe はネイティブAPIへポインターを渡すために必要です。
// C#で書いたコードからSilk.NETを通してVulkanとGLFWを呼び出します。
public sealed unsafe partial class VulkanApp : IDisposable
{
    private readonly IWindow _window;
    private Vk? _vk;
    private Instance _instance;
    private bool _instanceCreated;
    private bool _disposed;
    private bool _smokeTest;

    public VulkanApp()
    {
        // OpenGL用のコンテキストを作らず、Vulkan用のウィンドウを作ります。
        var options = WindowOptions.DefaultVulkan;
        options.Size = new Vector2D<int>(960, 540);
        options.Title = "C# + Silk.NET + Vulkan | Blue clear";
        // 学習用に描画回数を抑えます。
        options.UpdatesPerSecond = 30;
        options.FramesPerSecond = 30;
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += DrawFrame;
        _window.FramebufferResize += _ => _resizeRequested = true;
    }

    public void Run(bool smokeTest = false)
    {
        _smokeTest = smokeTest;
        // Run がウィンドウの初期化とイベント処理を行います。
        // ×ボタンで閉じるまで、この呼び出しは戻りません。
        _window.Run();
    }

    private void OnLoad()
    {
        Console.WriteLine("ウィンドウを作成しました。");
        _vk = Vk.GetApi();
        CreateInstance();
        EnumerateGpus();
        CreateRenderer();
        Console.WriteLine("初期化完了。終了するにはウィンドウの×ボタンを押してください。");
        // 起動確認用。通常の dotnet run ではウィンドウを開いたままにします。
        // smoke-testも実際に描画してから終了します。
    }

    private void CreateInstance()
    {
        // Instance はアプリとVulkanの入口です。GPUそのものではありません。
        // ウィンドウ作成後に、OSのウィンドウ連携で必要な拡張名を取得します。
        // Surfaceの作成に必要な拡張を有効にします。
        var surface = _window.VkSurface
            ?? throw new InvalidOperationException("Vulkan対応ウィンドウを作成できませんでした。");
        var extensions = surface.GetRequiredExtensions(out var extensionCount);
        var appName = SilkMarshal.StringToPtr("CSharpVulkanSample");
        try
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appName,
                ApplicationVersion = new Version32(1, 0, 0),
                // 基本機能だけなのでVulkan 1.0を要求します。
                // SDKのバージョンとGPUが対応するAPIのバージョンは別物です。
                ApiVersion = Vk.Version10
            };
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = extensionCount,
                PpEnabledExtensionNames = extensions
            };
            Check(_vk!.CreateInstance(in createInfo, null, out _instance), "Instance作成");
            _instanceCreated = true;
            Console.WriteLine("Vulkan Instanceを作成しました（要求API: 1.0）。");
        }
        finally
        {
            // ネイティブ文字列はGC任せにできないため、必ず解放します。
            SilkMarshal.Free(appName);
        }
    }

    private void EnumerateGpus()
    {
        // Vulkanの列挙は「件数を取得 → 配列を用意 → 内容を取得」の2段階です。
        // PhysicalDevice = 実際のGPU、Device = GPUを操作する論理デバイス。
        // この後、Surfaceへ表示できるGPUを選んでDeviceを作ります。
        while (true)
        {
            uint count = 0;
            Check(_vk!.EnumeratePhysicalDevices(_instance, ref count, null), "GPU件数取得");
            if (count == 0) throw new InvalidOperationException("Vulkan対応GPUが見つかりません。");
            var devices = new PhysicalDevice[checked((int)count)];
            fixed (PhysicalDevice* pointer = devices)
            {
                var result = _vk.EnumeratePhysicalDevices(_instance, ref count, pointer);
                // 列挙の途中で件数が変わった場合は、サイズを取り直します。
                if (result == Result.Incomplete) continue;
                Check(result, "GPU列挙");
            }
            Console.WriteLine($"検出したGPU: {count}台");
            for (var i = 0; i < count; i++)
            {
                _vk.GetPhysicalDeviceProperties(devices[i], out var properties);
                var name = SilkMarshal.PtrToString((nint)properties.DeviceName);
                var version = (Version32)properties.ApiVersion;
                Console.WriteLine($"  [{i}] {name}");
                Console.WriteLine($"      種類: {properties.DeviceType}, Vulkan: {version.Major}.{version.Minor}.{version.Patch}");
                Console.WriteLine($"      Vendor ID: 0x{properties.VendorID:X4}, Device ID: 0x{properties.DeviceID:X4}");
            }
            break;
        }
        // 配列と文字列の生成は初期化時だけです。毎フレームnewする処理はありません。
    }

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
            throw new InvalidOperationException($"{operation}に失敗しました: {result}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyRenderer();
        // 作成したVulkanオブジェクトは明示的に破棄します。
        // 初期化が途中で失敗しても、作成済みのリソースを解放できます。
        if (_instanceCreated) _vk!.DestroyInstance(_instance, null);
        _vk?.Dispose();
        _window.Dispose();
    }
}

