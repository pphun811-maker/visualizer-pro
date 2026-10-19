# 验证记录

## 2026-10-20 — v1.1.0 发布

- v1.1.0 汇总工具栏/参数区整理、移除面板数字切换按钮，以及经典蓝 / 柔和多彩 / 标准多彩三种配色。发布准备仅将 App 版本从 1.0.0 升为 1.1.0，功能源码沿用下方已验证实现；未重复桌面、DSP 或长期检查。
- 原便携目录文件被占用，发布使用独立目录 `artifacts/releases/v1.1.0/VisualizerPro-win-x64`，避免覆盖正在使用的文件。对应 ZIP 与 SHA256SUMS.txt 放在 `artifacts/releases/v1.1.0`。

## 2026-10-20 — 新增标准多彩

- 保留经典蓝和柔和多彩，新增“标准多彩”：固定五段蓝 `#4C91D9` / 青 `#36B1B5` / 绿 `#75B65B` / 橙 `#DEA04C` / 紫 `#AC80CC`，使用中等饱和度提升辨识度。FFT、倍频程、二维/三维声谱与分频段声场共用，顶部和图内图例同步更新。柔和版五种颜色数值保持不变。
- 新增 `DisplaySettings.FrequencyPalette`（Soft / Standard），继续兼容已有 `FrequencyColors` 开关。旧多彩配置缺少新字段时仍为 Soft；无效枚举值恢复 Soft，重新加载标准配色能选中正确下拉项。冻结状态直接切换柔和/标准会重着色现有历史并刷新三维缓存，保留同一帧、历史锚点与回看位置。
- Release 构建通过，0 警告 / 0 错误。首次默认构建与无调试信息发布并行造成一次 Core PDB 复制失败，已停止并行，随后顺序构建（`-p:DebugType=None`）通过；构建与便携程序集哈希一致后才启动检查。
- 最终便携版 `artifacts/standard-colors-package/spectral.json` 和 `artifacts/standard-colors-software/spectral.json` 各 8 组全通过：两套配色实际 FFT/二维声谱 RGB、缩放一致、冻结直接换色/历史保留、图例、持久化/恢复与旧配置兼容，以及原快捷键、倍频程、三维/回看和 1120×740 布局检查。标准多彩三维 3 秒短测 OpenGL 约 98.1 FPS，软件约 15.1 FPS，音频丢块均为 0；不同运行的短测不作为受控性能对照。
- 两种多彩模式的截图已目视对照；截图位于对应检查目录的 `soft` / `standard` 子目录。桌面测试均使用隔离数据并退出。DSP 和播放逻辑未改，本轮未重跑 DSP、完整播放回归、物理键鼠或长期测试。

## 2026-10-20 — 界面整理与柔和频段配色

- 顶部收紧为音源/冻结/专注/更多；参考快照与 PNG/CSV/JSON 导入导出迁入“更多”。布局、三个仪表模式与配色保持可见，分析参数和显示设置分组展开；统一面板标题、元信息和灰色网格，移除重复编号/英文标题。经典蓝默认保留，可选择“柔和多彩”。
- 多彩按固定五段映射灰蓝/青灰/鼠尾草绿/沙金/灰紫，共用于 FFT、倍频程、二维声谱、三维瀑布和分频段声场；波形、整体声场与电平保持蓝色。明暗继续表示电平；缩放、冻结/回看、测量、CSV 与音频播放不受配色影响。配置兼容旧文件，选择会保存。
- Release 构建 0 警告 / 0 错误；`dotnet run --project tests/VisualizerPro.Tests --no-restore -c Release`：35 项通过。
- 最终便携版 `artifacts/ui-refine-package/spectral.json` 和 `artifacts/ui-refine-software/spectral.json` 各 7 组通过。新增等能量 FFT/二维声谱的实际 RGB 像素检查、缩放频色一致、冻结历史重着色/位置/数据保持、三维几何不变、配色持久化、1120×740 窗口及参数展开边界、折叠组切换与迁移后的导出入口；保留原快捷键、倍频程、三维/历史检查。多彩三维短测 OpenGL 约 109.2 FPS、软件约 25.9 FPS，音频丢块均为 0。这是各 3 秒短测，不是长期性能指标。
- `artifacts/ui-refine-integration/integration.json`：12 组桌面回归通过；`artifacts/ui-refine-input/input.json`：6 组交互检查通过，60 次滑块变化合并为 32 次参数应用，音频丢块 0。冻结探针、滑块最终值与恢复连续绘制正常；输入队列 P95 约 60.6 ms / 最大 600.7 ms，不作为物理输入延迟证明。
- 经典蓝、多彩 FFT/二维/三维及最小窗口/展开设置截图已目视检查。初版最小窗口展开参数时下方卡片越界，已补控制区尺寸变化后的布局重算与紧凑读数间距，最终包边界检查和截图通过。
- 所有桌面检查使用工作区内独立数据目录并关闭测试窗口；未重复两小时实机长测、物理键鼠或所有 Shell 目录滚动验收。`ui-refine-blue-source` / `ui-refine-color-source` / `ui-refine-final-source` 为过程验证，最终交付以 package / software 两组为准。

2026-10-19：移除三个面板的“切换 1 / 2 / 3”按钮，保留快捷键、顶部模式下拉、放大/还原与电平读数说明。Release 构建 0 警告 / 0 错误；`artifacts/ui-remove-switches-source/spectral.json` 的 5 组现有桌面检查通过，覆盖快捷键/下拉同步、数字输入保护、冻结数据、专注/放大、F1、倍频程和三维声谱。OpenGL 短测约 88.7 FPS，音频丢块 0；面板截图已目视检查。采用 WPF 路由事件，未验收物理键鼠手感，未重复 DSP 和长测。下方为历史验证记录。

2026-10-16，Windows x64 / .NET 10。

## 自动验证

- 文件选择窗口：`scripts/Test-FileDialog.ps1` / `--file-dialog-test <目录>` 实际打开 Windows 原生文件窗口并发送关闭消息取消；实时与冻结模式均检查窗口内仪表停止绘制、取消恢复、冻结保留及异常恢复。`artifacts/file-dialog-source/file-dialog.json` 的 10 项通过，音频丢块 0。此检查不模拟文件列表滚动或物理鼠标，不作为资源管理器所有目录的响应时间保证。
- 发布包 `artifacts/file-dialog-package/file-dialog.json` 相同 10 项检查通过，音频丢块 0；`artifacts/file-dialog-regression/integration.json` 的 12 组播放、定位、循环、冻结、设备恢复与多格式解码回归全部通过。
- 面板按钮与快捷键：`artifacts/panel-controls-source-fixed/spectral.json` 的 5 组检查通过，新增主键盘/小键盘 1/2/3、V、F1/Esc，面板按钮与顶部选择/持久化同步、冻结帧保持、输入框与展开下拉的按键保护，以及专注模式下四个面板的放大/还原和实际 WPF 命中检查；图表截图会合成面板按钮。F1 说明为主动打开的独立窗口，不是悬停弹窗。源码三维短测约 100.8 FPS，音频丢块 0；面板与说明截图已目视检查。首次检查发现下拉焦点转移时保护不足，已修复；失败目录 `artifacts/panel-controls-source` 不作为通过证据。
- 发布包 `artifacts/panel-controls-package/spectral.json` 与 `artifacts/panel-controls-software/spectral.json`：同样 5 组检查均通过，OpenGL / 软件绘制短测约 105.6 / 27.6 FPS，音频丢块均为 0。控件在软件回退中保留，主动帮助窗口正常打开/关闭。
- `artifacts/panel-controls-regression/integration.json` 的 12 组桌面回归与 `artifacts/panel-controls-input/input.json` 的 6 组原有交互检查通过。冻结探针、滑块最终提交、恢复刷新正常，60 次滑块变化合并为 35 次参数应用，音频丢块 0；WPF 输入队列 P95 约 60.6 ms / 最大 548.6 ms，受测试调度和绘制影响，未作为物理输入延迟或手感验收。
- 倍频程与三维瀑布：35 项 DSP 检查通过。新增三种窗函数的 1 kHz 正弦 RMS 标定、同频带双音能量相加、Mid/Side 反相抵消、频率平滑独立、宽带噪声能量覆盖、20 Hz 高分辨率、Nyquist 单边权重、低采样率与静音、视角配置边界。
- `artifacts/spectral-source/spectral.json`：4 组新功能检查通过，覆盖倍频程冻结读数、参考快照与旧格式、非有限导入拒绝、带单位 CSV；三维投影/可见面拾取、缺失数据留空、单谱线脊线保留和网格缓冲复用；历史同步、旧时间窗跟随、视角范围/复位、放大截图和配置持久化。源码 OpenGL 约 108.6 FPS，音频丢块 0。截图已目视检查，检查采用控件事件/坐标，不代表物理鼠标验收。
- `artifacts/spectral-exploration/exploration.json`：原有 6 组历史回看/显示设置/新仪表检查通过，分频段与波形短测约 123.5/114.4 FPS，音频丢块 0。
- 发布包 `artifacts/spectral-package/spectral.json` 与软件回退 `artifacts/spectral-software/spectral.json`：上述 4 组检查均通过，OpenGL / 软件绘制短测约 119.6 / 27.3 FPS，音频丢块均为 0；实时曲面约 61,120 个三角面。两种渲染路径截图已目视检查。
- 调整旧时间窗前沿标签位置后，最终包 `artifacts/spectral-package-final/spectral.json` 同样 4 组通过，约 110.3 FPS，音频丢块 0，放大截图已目视检查。
- `artifacts/spectral-regression/integration.json`：发布包 12 组原有桌面回归通过，覆盖文件播放/预音量分析、暂停/定位/A–B、冻结/参考、整曲任务/取消、全屏/放大、WASAPI/设备重连和多格式解码。
- `artifacts/spectral-input/input.json`：6 组原有路由交互检查通过，冻结探针正常、60 次滑块变化合并为 30 次分析设置应用，操作后恢复持续刷新，音频丢块 0。输入队列数值受测试调度影响，不作为物理鼠标延迟指标。
- `artifacts/spectral-visual/visual.json`：最终发布包 5 组细线/缓存/声场/刷新检查通过，单行细线积分亮度变化仍约 2% 以内，实时短测约 71.6 FPS，音频丢块 0。
- 历史回看与新仪表：27 项 DSP 检查通过，新增分频段同相/反相/正交/静音/Nyquist 边界、显示偏好与测量独立、历史容量/冻结读者/过期/时间空洞/浮点漂移、后台缓冲恢复与播放时钟边界。
- `artifacts/exploration-source/exploration.json`、`artifacts/exploration-package/exploration.json`：6 组新功能检查通过，覆盖后台继续采集、冻结时间锚点、时间/频率/电平探针、各仪表同步历史帧、滑块及回到实时按钮、历史保留、短暂 UI 停顿后补齐、显示预设/自定义/持久化、波形触发、新仪表放大及历史控件随面板移动、FFT 改变与切源清理；60 ms 音频块的恒定色谱像素检查没有周期性黑条。最终发布包两组新仪表各 2 秒短测约 87.1/75.0 FPS，音频丢块 0。检查使用 WPF 控件事件与面板坐标，不等于物理鼠标或人眼动态验收。
- `artifacts/exploration-regression/integration.json` 的 12 组桌面回归与 `artifacts/exploration-input/input.json` 的 6 组路由交互检查通过。冻结探针、参数合并及恢复刷新未回退；本轮交互检查 60 次滑块变化合并为 27 次分析参数更新，音频丢块 0。
- 最终发布包 `artifacts/exploration-package-regression/integration.json` 的 12 组回归、`artifacts/exploration-visual-package/visual.json` 的 5 组细线/声场/刷新检查通过，面积采样的细线积分亮度变化仍约 2% 以内，60 秒历史短测约 77.8 FPS，音频丢块 0。
- 历史回看、分频段声场、波形及放大布局的实际截图位于 `artifacts/exploration-source`，已目视检查。
- 发布包软件回退两种新仪表启动/截图通过：`artifacts/exploration-software-1.png`、`artifacts/exploration-software-2.png` 及对应 `.ok`，已目视检查，无文字或面板裁切。
- 上一轮显示稳定性修复：19 项 DSP 检查通过（原有 17 项加频谱快速波动/瞬态响应和高频声场连续采样）；合成调幅信号中相邻帧变化为即时响应模式的 0.077 倍，阶跃在含 FFT 窗延迟的 167 ms 内接近目标（误差 <0.6 dB）。该比例仅适用于此测试信号，不是所有音乐的体验指标。
- `artifacts/visual-stability-source/visual.json`：60 秒历史、173/317/901 个物理像素高度下，单行细线移动 60 步的积分亮度保持在约 2% 范围；环形缓存接缝、到期移出、重复绘制、声场瞬态边界、增益缓回、冻结和定位重置检查通过。普通线性缩小在前两个高度会完全漏线。`thin-lines.png` 已目视检查。静态截图及数值采样不能替代用户的实际动态观感验收。
- `artifacts/visual-stability-regression/integration.json`：上一轮 12 组桌面回归通过，覆盖播放、暂停、定位、循环、冻结、参考、整曲任务、全屏、WASAPI、设备恢复和多格式解码。
- 17 项 DSP 检查通过：FFT 增益校正、三种窗函数、RMS、同相/反相、Mid 抵消、44.1/48 kHz K 加权、绝对门限、真峰值、非有限输入、显示/测量隔离、采集帧率、切源、配置恢复与文件尾块处理。
- 四组 FFmpeg EBU R128 对照通过：稳态音、相对门限动态信号、采样点间峰值、粉红噪声。对照差值小于 0.05 LU / 0.05 dB；这不等于第三方标准认证。
- 桌面集成检查通过：实际 WASAPI 连接、静音流、设备选择/失联/重连、原生 44.1 kHz 文件、预音量分析、暂停、定位、A–B 循环、冻结、快照、整曲缓存、取消、专注模式、放大和全屏切换。
- WAV、FLAC、MP3、AAC、M4A、OGG 解码验证通过，包括带空格和中文的文件路径。
- 自包含发布包运行及软件绘制回退检查通过；截图经过实际渲染和目视检查。

## 性能

- `artifacts/spectral-retention/performance.json`：发布包倍频程 + 三维瀑布，60 秒历史、最大 16 秒可见时间窗，实际测量 65 秒。平均约 101.7 FPS，历史达到容量 1200 帧、跨度 59.95 秒，音频丢块 0；工作集约 329.8→371.9 MiB，结束时托管堆约 56.6 MiB（不是完整 GC 后保留内存）。仅为本机合成信号短测，不代替两小时实际运行或其他硬件基准。
- `artifacts/exploration-retention/performance.json`：60 秒历史、波形模式实际运行 80 秒，平均约 82.6 FPS；历史为 1200 帧、跨度 59.95 秒、容量 1200，音频丢块 0。工作集约 357→424 MiB（历史逐渐填满），结束时托管堆约 61 MiB；此处包含尚未回收的对象，不能当作 GC 后保留内存或长期无泄漏证明。
- 游标/滑块修复后的 WPF 交互检查通过，见 `artifacts/input-routed/input.json`：坐标与交叉线对齐、Hz/dBFS 插值、冻结时移动、参数合并及最终提交、操作后恢复刷新。60 次滑块值变化合并为 21 次分析参数更新；该检查使用 WPF 坐标和 Thumb 路由事件，不等于物理鼠标验收。
- `artifacts/input-regression/integration.json` 的 12 组桌面回归通过。5 秒正常观察检查约 85.2 FPS、音频丢块 0，见 `artifacts/input-idle-performance/performance.json`。短时数据不能代替长测。

- 2026-10-16 修复检查：2560×1600 / 150% 缩放，启动及全屏往返的 HWND 四边均与屏幕一致，修复前启动实际偏移为 (120, 90) 像素。
- 系统声音模式短时检查 `artifacts/responsiveness-release/responsiveness.json`：静止段约 10 秒、851 次频谱绘制，帧间隔 P99 38.4 ms，最长 129 ms；恢复段最长 42.3 ms；音频队列丢块 0。修复前捕获到过 534 ms 长帧。测试运行会受前台状态和系统调度影响，这些数字不是严格控制变量的性能基准，也不能保证所有驱动环境无停顿。
- 音频设备枚举已移到单独后台任务。窗口移动期间保留上一帧，退出移动后恢复绘图，避免持续进行 OpenGL / D3D 互操作。布局高度计算改由尺寸和可见性变化触发。
- Skia GPU 资源缓存每个上下文最多 32 MiB，当前声谱图像复用，音频队列、分析帧历史、积分直方图和声谱缓存均有上限。
- 加速处理 7200 秒立体声音频约耗时 161 秒，固定间隔完整 GC 后的保留托管内存约 1.3 MiB，没有随输入时长持续增长。

## 尚未完成

没有进行两小时实际墙钟时间的连续全屏运行，也没有实际插拔多个物理显示器的测试。已验证单屏全屏/窗口切换与显示区域恢复逻辑，但这两项仍需更长时间和更多硬件验证。

窗口移动检查使用真实 HWND 位置变化和进入/退出移动消息，没有模拟实际鼠标拖动。它验证了移动期间暂停绘图与退出后的恢复；具体拖动手感还需用户实机体验。

曾尝试系统鼠标注入：沙箱内 SetCursorPos 被阻止；沙箱外可以移动系统鼠标，但 WPF 没有收到对应移动事件。失败报告 `artifacts/input-fix*`、`artifacts/input-native` 不属于通过证据；本次没有宣称真实鼠标拖动验收通过。

## 复现

```powershell
pwsh -File scripts/Test.ps1 -Reference -Desktop
pwsh -File scripts/Publish.ps1
pwsh -File scripts/Test-Package.ps1
pwsh -File scripts/Test-Responsiveness.ps1
pwsh -File scripts/Test-VisualStability.ps1
pwsh -File scripts/Test-Exploration.ps1
pwsh -File scripts/Test-SpectralModes.ps1
dotnet tests/VisualizerPro.Tests/bin/Release/net10.0/VisualizerPro.Tests.dll --stress 7200 artifacts/stress.json
```

详细报告与截图位于 `artifacts`，发布包测试使用独立的数据目录，不覆盖日常设置。
