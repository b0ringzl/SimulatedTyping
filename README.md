# 模拟手打输入

Windows x64 桌面应用。粘贴文本后，可以按固定字速或设定速度范围，模拟中文拼音打字、短暂停顿与可见的退格修正。

## 使用

1. 运行 `SimulatedTyping.exe`，在窗口中设置速度、删改率、输入方式、停顿等参数。
2. 在任意程序中选中**要模拟输入的文字**，按 **Ctrl+F7**。程序读取选区文字并就绪，屏幕下方出现约 2 秒的淡入淡出提示。无需先复制粘贴到应用窗口。
3. 点选实际要输入的位置，按 **Ctrl+F8** 开始、暂停或继续；按 **Ctrl+F9** 立即停止。双击托盘图标可重新打开窗口。

输入期间，目标屏幕右下角会显示圆环进度和百分比；暂停、句末等待时圆环保留，完成或停止后淡出。进度按已完成的原文字数计算。

Ctrl+F7 会暂时使用系统剪贴板读取选区，随后尽量恢复原来的剪贴板内容；如果没有选中文字，不会拿剪贴板中旧的内容就绪。仍可手动粘贴内容并点“就绪并隐藏”，或点“试打预览”。

默认的“系统拼音预览”模式需要目标程序的输入法已经切换为中文拼音。程序先把中文切成词组，逐键输入拼音，让输入法显示组合与候选；随后取消组合并按原文上屏该词组。这样可保留拼音打字过程，同时避免“优先”被首选候选“有限”替代。此模式不会按空格实际选择输入法候选。应用内的“试打预览”会比较最终文本与原文。“准确按字”模式不显示拼音候选，直接逐字发送 Unicode。

删改率是每个中文输入词组发生修正的概率。系统拼音预览模式会模拟拼音键入后退格，或同音字先上屏再删改；准确按字模式会模拟重复字或同音字再回删。标点有短暂停顿，句末有较长停顿。默认换行是 `Shift+Enter`，也可以选择 `Enter` 或跳过；在聊天框使用前请先试打。

“句末与段末停顿”可分别设置触发概率。默认句末 35%、段末 100%；触发后等待“参考等待”秒数再加 1 秒，默认共 11 秒。句子结束标点包括 `。！？.!?`；换行或文本结尾视为段末，同一处只触发一次。停顿时可用 Ctrl+F9 停止；手动暂停或切换窗口后，等待计时也会暂停。

Google 文档没有公布固定的自动保存间隔，因此默认 10 秒只是可调整的参考值。停顿有助于给自动保存留时间，不能保证每次停顿都产生独立的历史版本；Google 文档可能合并修订。若必须长期保留特定进度，请在 Google 文档的版本历史中为该版本命名。

## 构建

需要 Windows x64 和 .NET 10 SDK：

```powershell
dotnet build .\SimulatedTyping\SimulatedTyping.csproj -c Release
dotnet run --project .\SimulatedTyping\SimulatedTyping.csproj
```

发布为单文件：

```powershell
dotnet publish .\SimulatedTyping\SimulatedTyping.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -o .\artifacts\portable-win-x64
```

拼音转换使用 [ToolGood.Words.Pinyin](https://www.nuget.org/packages/ToolGood.Words.Pinyin)，中文分词使用 [Acme.Jieba.NET](https://www.nuget.org/packages/Acme.Jieba.NET)。所有输入文本只在本机内存中处理，不上传，也不写入应用日志。

## 已知边界

- 不同目标程序对拼音组合、`Esc` 取消和 Unicode 输入的处理可能不同；请先用“试打预览”并在目标程序做短句验证。程序目前不读取目标输入框内容做自动校验。
- 目标窗口切换时会暂停，但同一窗口内切换到另一个输入框尚不能自动识别。
- 管理员权限程序、远程桌面、游戏及安全输入框可能拒绝模拟按键。
- 紧急停止发生在拼音尚未上屏时，目标输入框可能留下未完成的拼音组合，可手动按 `Esc` 清除。
