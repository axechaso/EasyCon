# FRLG OCR 170a 增强测试版 · TID 第一阶段

本文件保留 r1 历史说明。当前 r4 日文场景版请阅读 [FRLG-JPN-170a.md](FRLG-JPN-170a.md)。

本版基于 EasyCon `1aed001c0e2d3a32d211c39bec26546741626bd6`。GUI、CLI 与截图回放工具使用同一套 FRLG 数字管线。版本显示为 `1.7.0-alpha.frlg-tid.1`，配置与缓存存放在程序旁的独立 `FRLG-OCR-Data` 目录。请解压到可写目录，例如 D 盘；不读取或覆盖原版用户配置。

## 开始使用

1. 解压整个测试包，双击 `EasyCon-FRLG-OCR.exe`。
2. 点击主窗口编辑区上方的 **FRLG OCR**。
3. 点击 **打开截图**；或在主窗口连接采集卡后，点击 **冻结采集画面**。
4. 选择 **日版 · 训练家卡 TID** 或 **英文 · 训练家卡 TID**。
5. 用鼠标框选五位数字，边缘留少量空白。不要框入 `IDNo.`。支持反向拖动、坐标微调和重新框选；默认区域按钮只是起点。
6. 点击 **读取选区**。完整结果保留前导零，例如 `02104`；不可靠结果显示“未识别”。界面读取的是冻结的快照，换画面后需要重新冻结。
7. 用 **保存区域 / 载入区域** 保存场景、分辨率与坐标；用 **复制 OCR 调用** 将选区用于独立 ECS 测试。窗口缩放不会改变原图像素坐标。保存的区域只允许载入同分辨率画面。

示例调用：

```text
$识别文本 = OCR(1288, 124, 331, 88, "FRLG_JPN_TID")
PRINT $识别文本
```

坐标仅是 1920×1080 默认 Switch 画面示例。手动框选结果优先，识别器不会换裁剪框。日英 TID 使用同一数字模板，场景默认区域不同。

`examples/FRLG-TID-170a-独立测试.ecs` 每轮读取三次，至少两次完整五位一致才确认；失败继续有界测试。将脚本顶部的场景和四个坐标改为手动选区即可。脚本不发送按键。

## 识别行为

- 采用 PokémonAutomation 的两次 5×5 高斯模糊、RGB 二值化、四连通区域分割、合并块分割、原图字形收紧、亮度归一化和 RMSD 字模比较。
- 数字资源固定为上游三套 `0–9`，并嵌入程序集。本阶段 TID 实际使用 `DialogDigits`；另外两套为后续等级与能力值预留。TID 不加载 Paddle 或 Tesseract，也不需要任何 `traineddata`。
- 以 1080 高度归一化字体尺度，在同一个选区尝试 175、190、205 三个阈值。至少两个阈值完整识别且所有成功候选一致才接受。
- 必须完整五位、数值位于 0–65535；任何不合格数字均使该候选失败，不跳过数字、不补零。匹配误差与第二候选差距均有门槛，并检查截断、重叠及异常排列。
- `OCR()` 成功返回五位字符串，失败返回空字符串。`OCR_CONF()` 的此场景值是模板区分质量分数，不是统计正确率。普通 `jpn`、`eng`、`FRLG_EN_ALL` 模型名仍走原有 OCR 缓存。
- 多阈值是一张图片的多种处理结果，不等于跨画面确认；自动流程仍须多次读取与失败重试。

## 第一阶段范围

本版已接日英训练家卡 TID 和手动框选测试。名称、性格、摘要等级、能力值、野生等级及 Paddle/Tesseract 文字双后端尚未接入。V3 继续保留为历史诊断稿，现有乱数、抓捕、狩猎、孵蛋、Seed 和帧轴流程没有合并改动。

离线截图与界面测试不代表采集卡实机验收，也不代表任意画面条件下都不会误识别。推荐先用独立测试入口读取当前训练家卡。

## 回放与开发

测试包包含 `FrlgOcrReplay.exe`，无需采集卡即可回放：

```powershell
.\FrlgOcrReplay.exe .\samples\nyash_jpn_45345.png FRLG_JPN_TID --expected 45345 --output .\replay-jpn
.\FrlgOcrReplay.exe .\samples\tom_eng_60895.jpg FRLG_EN_TID --expected 60895 --repeat 500 --output .\replay-eng
.\FrlgOcrReplay.exe .\screenshot.png FRLG_JPN_TID --roi 1288,124,331,88 --output .\replay-manual
```

回放导出选区、归一化图、模糊图、阈值图、逐位数字图及 JSON 评分。默认常规 OCR 调用不写调试图片，防止长跑不断占磁盘。

源码构建需要 .NET 10 SDK；Windows 测试包自带 .NET 运行时。数字管线直接复用基线自带 EzCv/OpenCV 原生库。

```powershell
python tools/FrlgOcrReplay/fetch_resources.py --verify
dotnet build EasyCon2.slnx -c Release
dotnet test EasyCon2.slnx -c Release --no-build
dotnet format EasyCon2.slnx --verify-no-changes --no-restore --severity error
pwsh -File tools/FrlgOcrReplay/publish.ps1
```

资源提交与 SHA-256 见 `frlg-ocr-resources.lock.json`。来源说明与代码授权见 `FRLG-OCR-SOURCES.md`。

基线自带部分换行、缩进与 using 顺序问题，单独作格式整理。格式检查采用 error 级别，仍检查换行、缩进和 using；基线已有的异步生命周期 analyzer 警告保留。当前自动修复器对其中 `VSTHRD100/103` 会生成无效的空条件取消表达式或改变 `Dispose()` 签名，因此这些运行逻辑保持基线原样。编译和单元测试仍完整执行。
