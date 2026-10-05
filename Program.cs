using System.Text;

// 日本語の説明やGPU名をコンソールへ表示します。
Console.OutputEncoding = Encoding.UTF8;
try
{
    using var app = new VulkanApp();
    app.Run(args.Contains("--smoke-test"));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"起動に失敗しました: {ex.Message}");
    Console.Error.WriteLine("GPUドライバのVulkan対応とREADMEのトラブル対処を確認してください。");
    return 1;
}
