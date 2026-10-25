# C# + Silk.NET + Vulkan 学習サンプル

Windowsでウィンドウを作り、Vulkan対応GPUを列挙し、青色の背景の中央でチェッカー模様の四角形を回転させます。4頂点を6インデックスで共有し、コードで生成した画像をGPUへ転送します。Descriptor Setでテクスチャを参照し、Push Constantで回転角度を渡します。Silk.NETからネイティブのGLFWとVulkanローダー・GPUドライバを呼び出します。

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

**青色の背景に、クリーム色とオレンジ色のチェッカー模様を貼った四角形が回転します。** 回転は毎秒30度の時計回りで、12秒で1回転します。画像は128×128ピクセル、マス目は8×8です。画像ファイルや追加ライブラリは不要です。四角形の中心を原点に置き、中央の正方形Viewportで縦横比を保ちます。ウィンドウのリサイズに合わせてSwapchainとPipelineを再作成し、最小化中は描画を止めます。現在は描画と表示を同じQueue familyで処理できるGPUを選びます。別々のQueue familyが必要なGPUはこの学習サンプルの対象外です。

`dotnet build` / `dotnet run` はGLSLの `.vert` / `.frag` をVulkan 1.0用のSPIR-Vへ自動コンパイルします。生成物は `obj/Shaders` に置き、実行先の `Shaders` フォルダへコピーします。変更のないシェーダーは再コンパイルしません。`dotnet publish` でもSPIR-Vを配布先へコピーするため、ビルド済みアプリの実行にはglslcは不要です。

起動確認用のモードは60フレームを表示して自動終了し、最後に回転角度を出力します。途中で800×450へリサイズし、960×540へ戻してSwapchain再作成も確認します。頂点・インデックス・テクスチャ・Descriptor Setはリサイズ時に作り直しません。

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
| VulkanPipeline.cs | SPIR-V読込、Graphics Pipeline、Descriptor Setとバッファのバインド、インデックス描画 |
| VulkanVertices.cs | 頂点の位置とUV、インデックス、バッファ作成とメモリ書き込み、回転速度 |
| VulkanTexture.cs | チェッカー画像生成、ステージング転送、画像レイアウト、ImageView、Sampler、Descriptor Set |
| Shaders/quad.vert | 頂点シェーダー。位置を回転し、UVをフラグメントシェーダーへ渡す |
| Shaders/quad.frag | フラグメントシェーダー。UVに対応するテクスチャの色を出力 |
| README.md | 準備、実行方法、学習の流れ |
| .gitignore | ビルド結果とローカルキャッシュの除外 |

## コードを読む順序

1. Program.csの `using var app`：終了時や失敗時にもリソースを解放します。
2. VulkanAppのコンストラクターとRun：ウィンドウの設定とイベントループ。
3. OnLoadとCreateInstance：ウィンドウ初期化後、Vulkanへの入口を作ります。
4. EnumerateGpus：件数と内容を2回に分けて取得します。
5. VulkanRenderer.csのCreateRendererとSelectDevice：Surfaceへ描画・表示できるGPUとQueueを選び、Deviceを作ります。
6. VulkanVertices.csのCreateVertexBuffer：4つの位置とUV、6つのインデックスをバッファへ書き込みます。
7. VulkanTexture.csのCreateTextureとUploadTexture：画像生成 → ステージングバッファへ書き込み → 画像へ転送 → シェーダー読み取り用レイアウトへ移行します。
8. CreateTextureDescriptorとCreateGraphicsPipeline：画像とSamplerをDescriptor Setへ登録し、PipelineLayoutへ対応するSetLayoutを指定します。CreateSwapchainとCreateRenderPassは表示画像と背景のクリアを準備します。
9. DrawFrameとRecordQuad：角度更新 → 画像取得 → 背景のクリア → Pipeline・頂点・インデックス・Descriptor Setをバインド → 角度をPush Constantへ記録 → CmdDrawIndexed → GPUへ送信 → 画面表示。FenceとSemaphoreで同期します。quad.vertは位置を回転し、quad.fragは補間されたUVで画像を読み取ります。
10. DisposeとDestroyRenderer：GPUの処理を待ち、作成したリソースを逆順で破棄します。

InstanceはVulkanへの入口、PhysicalDeviceはGPU、DeviceはGPUを操作する論理デバイスです。SurfaceがウィンドウとVulkanをつなぎ、Swapchainが表示用の画像を管理します。RenderPassの開始時に背景を塗りつぶし、その上へGraphics Pipelineで四角形を描きます。

頂点は1つ16バイト（位置のfloat×2、UVのfloat×2）で、4頂点のバッファは64バイトです。PipelineのBinding 0でstrideを16、location 0の位置をoffset 0、location 1のUVをoffset 8に指定します。UVは画像上の座標で、左上が(0,0)、右下が(1,1)です。

インデックスは `ushort` の `{ 0, 1, 2, 2, 3, 0 }`、合計12バイトです。CmdBindIndexBufferでUint16を指定し、CmdDrawIndexedで6つを読み取ります。頂点0・2を共有する2枚の三角形が、1枚の四角形になります。

学習用にHostVisible + HostCoherentのメモリを選び、MapMemoryでCPUから頂点を書き込みます。HostCoherentなのでFlushは不要で、初期化時に一度だけ書き込みます。GPUが使う間はバッファを書き換えません。将来大量の頂点を扱う場合は、ステージングバッファからDeviceLocalメモリへ転送する方法へ進めます。

テクスチャのピクセルはRGBA8 sRGBです。CPUから書けるステージングバッファを転送元に、DeviceLocalメモリを割り当てたOptimal配置のImageを転送先に使います。画像のレイアウトは `Undefined → TransferDstOptimal → ShaderReadOnlyOptimal` の順に変えます。Pipeline Barrierで転送書き込みをフラグメントシェーダーの読み取りへ同期し、転送完了後にステージングバッファを解放します。

ImageViewは画像のどの範囲を参照するか、Samplerはどのように色を読むかを定義します。今回はNearestでマス目をくっきり表示し、ClampToEdgeで端を扱います。MipMapと異方性フィルタは使いません。画像内のsRGB色はサンプル時に線形へ変換され、sRGB形式の画面へ書き込むときに表示用へ戻ります。

DescriptorSetLayoutは `set 0 / binding 0` にCombinedImageSamplerを1個宣言します。DescriptorPoolからSetを確保し、ImageViewとSamplerを登録します。GLSLの `layout(set = 0, binding = 0) uniform sampler2D` と対応し、CmdBindDescriptorSetsで描画時に指定します。画像を更新しないので、全フレームで同じSetを再利用できます。

Push Constantは小さなデータを描画命令へ記録する仕組みです。PipelineLayoutに頂点シェーダー用の4バイトを宣言し、CmdPushConstantsで回転角度（float、ラジアン）を毎フレーム渡します。角度は経過秒数×回転速度で更新し、2π未満に保ちます。頂点シェーダーがsin/cosで位置を回転し、GPUが三角形内のUVを補間します。深度バッファはまだ使いません。Viewportは描画座標を画面へ変換し、Scissorは描画を許可する矩形です。

同時実行フレームは1つに限定します。画像取得用Semaphoreは前のGPU処理をFenceで待ってから再利用し、表示用SemaphoreはSwapchainの画像ごとに用意します。毎フレームの配列生成はありません。リサイズ時は学習用の簡単な方式としてDeviceWaitIdleで待機してから画像関連のリソースを作り直します。

背景色はVulkanRenderer.csのDrawFrameにある `new ClearColorValue(0.02f, 0.12f, 0.45f, 1f)` で変更できます。値は赤・緑・青・不透明度の順です。sRGB形式では線形のRGB値から表示用の値へ変換されます。

四角形の位置とUVは `VulkanVertices.cs` の `vertices` を編集し、`dotnet run` で確認できます。値はX・Y・U・Vの順です。チェッカーの色とマス目は `VulkanTexture.cs` のピクセル生成部分で変更できます。回転速度は `RotationSpeed` で変更できます。0なら静止し、負の値なら逆回転です。今回は `gl_Position.w = 1` なので回転後の座標はそのままNDCになります。Xは右向き、正の高さのViewportではYは下向きです。

## トラブル対処

- GPUが見つからない／Vulkanの読み込みに失敗：GPUメーカーのVulkan対応ドライバを確認してください。SDKのインストールだけではGPUのVulkan対応は保証されません。
- SDK付属の診断：`& "$env:VULKAN_SDK\Bin\vulkaninfo.exe" --summary` でGPU情報を確認できます。
- NuGet復元失敗：ネットワーク接続、プロキシ、NuGetソース設定を確認してください。
- glslc.exeが見つからない：`Test-Path "$env:VULKAN_SDK\Bin\glslc.exe"` で確認し、SDKインストール後はターミナルを開き直してください。別の場所を使う場合は `dotnet build -p:GlslcPath="C:\VulkanSDK\1.4.328.1\Bin\glslc.exe"` で指定できます。
- シェーダーのコンパイル失敗：出力されたGLSLのファイル名と行番号を確認してください。
- SPIR-Vファイルが見つからない：`dotnet build` を実行し、実行ファイルと同じ場所にある `Shaders` フォルダごと配置してください。
- ウィンドウ初期化失敗：ローカルのWindowsデスクトップで実行し、GPUドライバを確認してください。
- SDKとGPUのバージョンが違う：SDKは開発ツールの版、表示するAPIバージョンは各GPUドライバの対応版です。このサンプルは基本機能だけを使うためAPI 1.0を要求します。

次の学習段階は画像ファイルをテクスチャとして読み込む、またはUniform Bufferで変換行列を渡して3D表示へ進む処理です。

## 参考

- Silk.NET公式: https://dotnet.github.io/Silk.NET/
- Silk.NETソース: https://github.com/dotnet/Silk.NET
- Vulkan仕様: https://registry.khronos.org/vulkan/specs/latest/html/vkspec.html
- 表示Semaphoreの再利用: https://docs.vulkan.org/guide/latest/swapchain_semaphore_reuse.html
- シェーダーとSPIR-V: https://docs.vulkan.org/tutorial/latest/03_Drawing_a_triangle/02_Graphics_pipeline_basics/01_Shader_modules.html
- Push Constant: https://docs.vulkan.org/guide/latest/push_constants.html
- テクスチャ画像と転送: https://docs.vulkan.org/tutorial/latest/06_Texture_mapping/00_Images.html
- Combined Image Sampler: https://docs.vulkan.org/tutorial/latest/06_Texture_mapping/02_Combined_image_sampler.html
