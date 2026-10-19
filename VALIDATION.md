# 验证记录

## 2026-10-20 — v1.2.0 发布准备

- v1.2.0 包含自绘窗口标题栏、精简面板按钮和顶部图例、独立强度配色，以及高分辨率瞬时声谱与逐物理像素着色。完整变更和验证见下方两轮记录。
- 发布准备将 App 版本从 1.1.0 更新为 1.2.0，功能代码沿用本次会话已通过的版本：40 项 DSP/历史分析、GPU/软件各 11 组声谱检查、5 组细线稳定性和 12 组桌面回归。未重复长期实机或物理输入验收。
- 正式便携包使用独立目录 `artifacts/releases/v1.2.0/VisualizerPro-win-x64`；ZIP、SHA256SUMS.txt 与发布清单放在 `artifacts/releases/v1.2.0`。旧 v1.1.0 正式包保留。

## 2026-10-20 — 强度声谱清晰度与参考图色阶对齐

- 用户反馈低频块状、画面发糊，要求更贴近先前参考图。本轮确认旧强度声谱复用了 FFT 曲线的频率平滑/显示包络，8192 点 FFT 在 192 kHz 输入下的低频格点间隔约 23.4 Hz；把这些格点铺到对数轴会形成宽色块。上一轮还把参考图 −130～0 dB 的色阶压入可调 80 dB 范围，导致暗部颜色不符。
- 新增独立 SpectrogramAnalyzer / SpectrogramSlice：2048 对数频点、至多每秒 20 次的瞬时历史，不使用 FFT 曲线的平均、平滑和衰减。48 kHz 有效采样率使用 32768 / 8192 点 Hann 窗，200～400 Hz 在功率域过渡；较低采样率相应缩短窗口。高采样率先经过 Blackman 低通 FIR 再抽取，18～20 kHz 平滑过渡至原采样率 FFT，20 kHz 以上保留原 FFT 的频带峰值，避免混叠或丢失超声范围。对应窗口预热后才显示，避免开始时出现横向伪亮条。
- 实时 AnalysisEngine 启用该附加数据，既有 FFT、倍频程、声场、电平、文件整曲分析算法保持原路径；普通 Analyzer / 整曲任务不默认执行附加分析。冻结二维探针和强度三维读取这份瞬时谱；因此可与带显示包络的 FFT 读数及原三维外形不同。
- 新增 IntensitySpectrogramRaster，保存线性功率；时间缩小按面积积分，频率缩小时保留窄峰，放大在功率域插值。按物理像素算完能量后才映射参考颜色，一比一绘制，不再对 RGB 做二次线性模糊。频率分隔线对齐单个物理像素。经典蓝/频段配色保留原 SpectrogramRaster。
- 强度颜色固定对应参考图的 −130～0 dBFS（14 个采样颜色节点保持不变），不受显示斜率、声谱范围或 FFT 上下限重分配。“声谱范围”仍控制跟随配色的色阶和三维高度；元信息、参数提示、F1、README 已说明。没有添加左侧颜色标尺。
- 实际验证：Release 构建/本地 publish 成功，0 警告/0 错误；40 项 DSP/历史分析检查全通过，新增相邻 56/64 Hz 在 192 kHz 输入下可分离、瞬时谱没有显示衰减尾迹且 FFT/电平不变、抽取抗混叠且保留原超声峰、8/48/192 kHz 启动预热、反相立体声与 Mid 抵消。
- 最终便携包 artifacts/spectrogram-detail-package/spectral.json 与 artifacts/spectrogram-detail-software/spectral.json 各 11 组通过，覆盖原图实际二维/三维 RGB、冻结/探针/保存/布局，以及 192 kHz 同信号前后渲染、像素边缘、窄事件缩放与实时绘制。两份 sharpness-metrics.json 的二维强度 3 秒短测：GPU 约 60.8 FPS / 平均单声谱绘图 3.95 ms，软件约 39.9 FPS / 5.76 ms，音频丢块均 0；这是 48 kHz 演示信号实时 UI 短测，不是 192 kHz 实机性能基准（192 kHz 是离线对照输入）。
- artifacts/spectrogram-detail-stability/visual.json 的 5 组既有细线/环形缓冲/声场检查通过，保留 173/317/901 物理像素下单行亮度稳定性；artifacts/spectrogram-detail-integration/integration.json 的 12 组播放、整曲任务、设备恢复、全屏及解码回归通过。所有桌面测试使用隔离数据并退出。
- 已目视检查最终包 sharpness-before.png / sharpness-after.png、二维/三维截图；对照图为同一段合成测试信号，未取得参考图对应音频，不能宣称实际音乐纹理逐像素相同。未做长期实机/物理输入验证。Git diff --check 仍受环境 this operation must be run in a work tree 限制；独立新增行尾空白检查为 0。
- 更新本地开发便携目录和 ZIP、同步说明并验证主程序/程序集/文档哈希；没有更新 GitHub Release 或 artifacts/releases/v1.1.0 历史包。

## 2026-10-20 — 按图清理界面、自绘标题栏与强度声谱

- 删除四个仪表的放大/还原按钮、电平“读数 F1”和顶部频段颜色图例；保留双击放大、Esc 还原、F1 与底部帮助入口。主窗口标题仅为 Visualizer Pro，取消系统标题栏，使用中性灰自绘最小化、最大化/还原、关闭按钮；窗口模式保留顶部空白/Logo 拖动、双击最大化和边缘缩放。补充 WM_GETMINMAXINFO 工作区/最小尺寸约束，避免最大化越过任务栏；全屏仍覆盖整个屏幕。
- 新增独立“声谱色 → 强度热力”，只作用于时间声谱和三维瀑布。用户明确要求直接使用参考图颜色：从参考 JPEG 左侧色阶的文字内部采样 14 个节点（-130 到 0，每 10 dB 一点），以最亮 15% 像素的 RGB 中位数减少黑色抗锯齿影响。渐变从暗绿/青、蓝紫、粉色、米黄至白色，不绘制颜色标尺。所选声谱范围决定颜色覆盖的电平范围；颜色依据实际电平，不叠加显示斜率。三维强度模式去掉原有蓝色轮廓线以避免染色，探针和选中切片仍为蓝色。
- DisplaySettings.SpectrogramIntensityColors 默认 false，旧配置保持原配色。该开关独立保存，切换会重着色既有二维历史并使三维颜色缓存失效；不改变冻结帧、回看位置、几何或分析设置。其他仪表继续使用经典蓝/柔和多彩/标准多彩。
- Release 构建通过，0 警告/0 错误；Publish.ps1 -SkipRestore 成功更新本地便携目录与 ZIP。最终包 artifacts/intensity-chrome-package/spectral.json 与 artifacts/intensity-chrome-software/spectral.json 各 10 组全通过：参考图弱/中/强节点的实际二维和三维 RGB、显示斜率下等电平跨频率同色、FFT 像素不变、冻结历史重着色/探针/保存恢复/旧配置、窗口按钮命中与最大化/还原/最小化、任务栏边界、全屏四边像素，以及 1120×740 与设置展开布局。源程序修正阶段 artifacts/intensity-chrome-source-fixed 同样 10 组通过。
- 最终包 artifacts/intensity-chrome-integration/integration.json 的 12 组既有回归全部通过，包含首次全屏、文件播放/定位/A–B/冻结、整曲分析/取消、设备连接恢复及多格式解码。既有标准多彩三维 3 秒短测 GPU 约 102.2 FPS、软件约 24.5 FPS，丢块均为 0；不是强度模式的受控性能基准。
- 已目视检查最终包二维强度/最大化与软件三维截图，以及源码最小窗口截图。所有桌面数据都在工作区独立 VISUALIZERPRO_DATA_DIR 内，测试窗口均退出，收尾无 VisualizerPro 进程。未重复 DSP、两小时实机长测、物理拖动/缩放或多显示器验证。git diff --check 仍被环境报错 this operation must be run in a work tree 阻断；独立对照 HEAD 的新增行尾空白检查为 0，未将其称作 Git diff 检查通过。
- 本轮仅更新本地开发便携包，版本号仍为 1.1.0；未提交/推送或修改 GitHub Release v1.1.0 及其独立 artifacts/releases/v1.1.0 目录。

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
