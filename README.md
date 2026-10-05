# C# + Silk.NET + Vulkan 学習サンプル

Windowsでウィンドウを作り、Vulkan対応GPUを列挙し、青色の背景の中央にRGBのグラデーション三角形を表示します。ソースはC#ですが、Silk.NETからネイティブのGLFWとVulkanローダー・GPUドライバを呼び出します。

## 必要な環境

- Windows 64 bit、Vulkan対応GPUとドライバ
- .NET SDK 10.0.302（net10.0）
- Vulkan SDK 1.4.328.1（ビルド時に付属のglslc.exeでシェーダーをコンパイルします）
- VS Code。C# Dev Kitがあると編集・デバッグに便利です
- 初回のNuGetパッケージ取得時にインターネット接続

Silk.NET.Windowing / Silk.NET.Vulkan / Silk.NET.Vulkan.Extensions.KHR は2.23.0に固定しています。KHR拡張パッケージはSurfaceとSwapchainに使います。

## ビルドと実行

このフォルダのファイルを F:\CSharpVulkanSample に配置し、PowerShellで実行します。

```powershell
cd F:\CSharpVulkanSample
code .
dotnet restore
dotnet build
dotnet run
```

`code` コマンドがない場合はVS Codeの「ファイル → フォルダーを開く」で指定フォルダを開いてください。VS Code内のターミナルからも同じコマンドを実行できます。

960×540のウィンドウが開き、コンソールに初期化完了とGPU名・種類・対応Vulkanバージョン・IDが表示されます。×ボタンで終了します。

**青色の背景に、上が赤・左下が緑・右下が青の三角形が表示されます。** 内側は色がなめらかに混ざります。ウィンドウのリサイズに合わせてSwapchainとPipelineを再作成し、中央の正方形Viewportで三角形の縦横比を保ちます。最小化中は描画を止めます。現在は描画と表示を同じQueue familyで処理できるGPUを選びます。別々のQueue familyが必要なGPUはこの学習サンプルの対象外です。

`dotnet build` / `dotnet run` はGLSLの `.vert` / `.frag` をVulkan 1.0用のSPIR-Vへ自動コンパイルします。生成物は `obj/Shaders` に置き、実行先の `Shaders` フォルダへコピーします。変更のないシェーダーは再コンパイルしません。`dotnet publish` でもSPIR-Vを配布先へコピーするため、ビルド済みアプリの実行にはglslcは不要です。

起動確認用のモードは60フレームを表示して自動終了します。途中で800×450へリサイズし、960×540へ戻してSwapchain再作成も確認します。

```powershell
dotnet run -- --smoke-test
```

## ファイルの役割

| ファイル | 役割 |
| --- | --- |
| CSharpVulkanSample.csproj | .NET 10、unsafe、Silk.NET依存関係、シェーダーの自動コンパイルと配置 |
| Program.cs | エントリーポイント。日本語出力、アプリの実行、例外時の表示と終了コード |
| VulkanApp.cs | ウィンドウ、Instance作成、GPU列挙、リソース解放。概念を日本語コメントで説明 |
| VulkanRenderer.cs | Surface、GPU選択、Device、Swapchain、青色の描画、同期、リサイズ対応。同じVulkanAppクラスをpartialで分割 |
| VulkanPipeline.cs | SPIR-V読込、ShaderModule、Graphics Pipeline、Viewport、三角形の描画命令 |
| Shaders/triangle.vert | 頂点シェーダー。3つの頂点の位置とRGBを定義 |
| Shaders/triangle.frag | フラグメントシェーダー。補間された色をピクセルへ出力 |
| README.md | 準備、実行方法、学習の流れ |
| .gitignore | ビルド結果とローカルキャッシュの除外 |

## コードを読む順序

1. Program.csの `using var app`：終了時や失敗時にもリソースを解放します。
2. VulkanAppのコンストラクターとRun：ウィンドウの設定とイベントループ。
3. OnLoadとCreateInstance：ウィンドウ初期化後、Vulkanへの入口を作ります。
4. EnumerateGpus：件数と内容を2回に分けて取得します。
5. VulkanRenderer.csのCreateRendererとSelectDevice：Surfaceへ描画・表示できるGPUとQueueを選び、Deviceを作ります。
6. CreateSwapchainとCreateRenderPass：表示画像を用意し、LoadOp.Clearで背景色を書き込む設定をします。
7. Shaders/triangle.vertとtriangle.frag：頂点番号から位置・色を取り出し、三角形内で補間された色を出力します。
8. VulkanPipeline.csのLoadShaderとCreateGraphicsPipeline：SPIR-VからShaderModuleを作り、描画設定をPipelineにまとめます。
9. DrawFrameとRecordTriangle：画像取得 → 背景のクリア → Pipelineをバインド → Viewport・Scissor設定 → CmdDraw → GPUへ送信 → 画面表示。Fenceで前の描画を待ち、SemaphoreでGPU側の順序を制御します。
10. DisposeとDestroyRenderer：GPUの処理を待ち、作成したリソースを逆順で破棄します。

InstanceはVulkanへの入口、PhysicalDeviceはGPU、DeviceはGPUを操作する論理デバイスです。SurfaceがウィンドウとVulkanをつなぎ、Swapchainが表示用の画像を管理します。RenderPassの開始時に背景を塗りつぶし、その上へGraphics Pipelineで三角形を描きます。

頂点シェーダーは `gl_VertexIndex` の0・1・2から位置と色を取得します。GPUが三角形内の色を補間し、フラグメントシェーダーが不透明度1で出力します。頂点バッファ、Descriptor、Push Constant、深度バッファはまだ使いません。PipelineLayoutは空です。Viewportは描画座標を画面へ変換し、Scissorは描画を許可する矩形です。

同時実行フレームは1つに限定します。画像取得用Semaphoreは前のGPU処理をFenceで待ってから再利用し、表示用SemaphoreはSwapchainの画像ごとに用意します。毎フレームの配列生成はありません。リサイズ時は学習用の簡単な方式としてDeviceWaitIdleで待機してから画像関連のリソースを作り直します。

背景色はVulkanRenderer.csのDrawFrameにある `new ClearColorValue(0.02f, 0.12f, 0.45f, 1f)` で変更できます。値は赤・緑・青・不透明度の順です。sRGB形式では線形のRGB値から表示用の値へ変換されます。

三角形の位置や色は `Shaders/triangle.vert` の `positions` / `colors` を編集し、`dotnet run` で確認できます。今回は `gl_Position.w = 1` なので座標はそのままNDCになります。Xは右向き、正の高さのViewportではYは下向きです。

## トラブル対処

- GPUが見つからない／Vulkanの読み込みに失敗：GPUメーカーのVulkan対応ドライバを確認してください。SDKのインストールだけではGPUのVulkan対応は保証されません。
- SDK付属の診断：`& "$env:VULKAN_SDK\Bin\vulkaninfo.exe" --summary` でGPU情報を確認できます。
- NuGet復元失敗：ネットワーク接続、プロキシ、NuGetソース設定を確認してください。
- glslc.exeが見つからない：`Test-Path "$env:VULKAN_SDK\Bin\glslc.exe"` で確認し、SDKインストール後はターミナルを開き直してください。別の場所を使う場合は `dotnet build -p:GlslcPath="C:\VulkanSDK\1.4.328.1\Bin\glslc.exe"` で指定できます。
- シェーダーのコンパイル失敗：出力されたGLSLのファイル名と行番号を確認してください。
- SPIR-Vファイルが見つからない：`dotnet build` を実行し、実行ファイルと同じ場所にある `Shaders` フォルダごと配置してください。
- ウィンドウ初期化失敗：ローカルのWindowsデスクトップで実行し、GPUドライバを確認してください。
- SDKとGPUのバージョンが違う：SDKは開発ツールの版、表示するAPIバージョンは各GPUドライバの対応版です。このサンプルは基本機能だけを使うためAPI 1.0を要求します。

次の学習段階は頂点バッファ → C#からGPUへ頂点データを渡す処理です。

## 参考

- Silk.NET公式: https://dotnet.github.io/Silk.NET/
- Silk.NETソース: https://github.com/dotnet/Silk.NET
- Vulkan仕様: https://registry.khronos.org/vulkan/specs/latest/html/vkspec.html
- 表示Semaphoreの再利用: https://docs.vulkan.org/guide/latest/swapchain_semaphore_reuse.html
- シェーダーとSPIR-V: https://docs.vulkan.org/tutorial/latest/03_Drawing_a_triangle/02_Graphics_pipeline_basics/01_Shader_modules.html
