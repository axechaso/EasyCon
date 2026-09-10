# FRLG OCR 170a 日文增强版 · r2

基于 EasyCon `1aed001c0e2d3a32d211c39bec26546741626bd6`，程序版本 `1.7.0-alpha.frlg-jpn.2`。本包支持日文名称、性格、摘要等级、六项能力值、野生等级，保留已实测可用的日英 TID 管线。

## 使用

1. 将整个包解压到可写目录，启动 `EasyCon-FRLG-OCR.exe`。模型已包含在 `models/frlg`，无需另外安装 Paddle、Python 或 .NET。
2. 点击主窗口编辑区上方的 **FRLG OCR**。
3. **打开截图**，或在主窗口连接视频源后点击 **冻结采集画面**。
4. 选择场景，用鼠标框选对应文字或数字。**默认区域**提供 Switch 标准布局下的起点；坐标始终对应原图像素，窗口缩放不会改变坐标。
5. 点击 **读取选区**。文字场景首次加载模型较慢；读取的是当前快照，换画面后需要重新冻结。
6. 用 **保存区域 / 载入区域** 保存场景、分辨率、候选名称和坐标。区域只能载入同分辨率画面。
7. 用 **复制 OCR 调用** 把四个坐标和场景填入 `examples/FRLG-JPN-170a-场景测试.ecs`。该入口不发送按键，每轮三读、两次非空一致才确认，五轮后停止。

成功返回字符串；失败返回空字符串。数字也按字符串返回，TID 保留前导零。`OCR_CONF()` 是后端质量分数，不是统计正确率。

## 场景表

| 场景参数 | 选择区域 | 成功输出 |
| --- | --- | --- |
| `FRLG_JPN_NAME` | 野生战斗左上种族名称，不含性别和等级 | 日文标准种族名称 |
| `FRLG_JPN_SUMMARY_NAME` | 摘要第一页右侧「なまえ」，避开左上昵称 | 日文标准种族名称 |
| `FRLG_JPN_NATURE` | 摘要第一页下方性格一行，可包含「なせいかく」 | 25 项之一，如 `のうてんき` |
| `FRLG_JPN_LEVEL` | 摘要左上紫色栏，只框等级数字 | 2–100 |
| `FRLG_JPN_WILD_LEVEL` | 野生战斗左上栏，只框等级数字，不含 Lv | 2–100 |
| `FRLG_JPN_HP` | 摘要第二页 HP，支持「当前/最大」或只框最大值 | 最大 HP，1–714 |
| `FRLG_JPN_ATTACK` | 摘要第二页攻击数值 | 1–614 |
| `FRLG_JPN_DEFENSE` | 防御数值 | 1–614 |
| `FRLG_JPN_SP_ATTACK` | 特攻数值 | 1–614 |
| `FRLG_JPN_SP_DEFENSE` | 特防数值 | 1–614 |
| `FRLG_JPN_SPEED` | 速度数值 | 1–614 |
| `FRLG_JPN_TID` | 日版训练家卡五位 TID，不含 IDNo. | `00000`–`65535` |
| `FRLG_EN_TID` | 英文训练家卡五位 TID | `00000`–`65535` |

文字和数字边缘留少量空白。野生等级默认框是两位数字的示例位置；一位或三位等级、不同缩放/布局要手动重框。HP 通过斜杠和两侧数值检查读取最大值，不截取末尾两/三位猜结果。

## 名称候选

默认使用上游全量日文词典（超过 1000 个标准名称），包含 FRLG 的宝可梦及上游 OCR 误读别名。维护词典和场景，不维护逐个物种的图片标签。

已知目标时在窗口填 `ミニリュウ|ハクリュー`，或直接传：

```text
$名称 = OCR(297, 129, 362, 66, "FRLG_JPN_NAME:ミニリュウ|ハクリュー")
PRINT $名称
```

支持 `dratini|dragonair` 这样的上游 slug，输出仍为日文标准名。不存在的候选报失败；实际完整名称在目标集合外时返回失败，不强行改成目标。ハクリュー是哈克龙，カイリュー是快龙。示例坐标仅对应 1920×1080 标准布局，实际以手动框选为准。

## 识别管线

- 日文文字：统一字体高度、白边、两次 5×5 高斯模糊、多阈值二值化、收紧文字区域、PaddleOCR PP-OCRv5 Chinese/Japanese 模型、CTC 解码、字符归一化、上游误读表及词典匹配。
- Paddle 至少两个阈值给出高置信完整一致名称时接受。低置信、模糊匹配或候选冲突时，调用 `jpn.traineddata` Tesseract 取得第二意见；仍不一致则返回空字符串。性格匹配 25 项标准词及「せいかく」形式。
- 数字：模糊、连通区域分割、合并块分割、字形收紧和模板比较。TID/野生等级用 DialogDigits，摘要等级用 LevelDigits 并处理白字紫底，六项能力用 Digits。
- 数字须通过字模误差、候选差距、布局、数值范围检查；至少两组阈值完整一致且无冲突才接受。数字路径不初始化文字模型。
- 模型按调用者生命周期缓存，内部使用 CPU 并行，外部调用加锁。关闭窗口或脚本引擎时释放。常规 OCR 不写调试图片。

多阈值仍是一张画面。自动流程须保留跨时刻确认与失败重试。

## 独立安装与验证范围

配置与缓存在程序旁的 `FRLG-OCR-Data`。原版安装、r1 TID 包和既有 Seed、帧轴、抓捕、野生、狩猎及孵蛋流程不被覆盖。本轮提供后端和测试入口，没有把旧 V3 诊断稿并入正式 ECS。

日版摘要截图、程序与界面回归见《验证记录》。本轮未运行 Switch 按键流程。上游没有日版野生战斗测试截图，野生数字模板以公开战斗数字图验证；当前日版实机坐标与文字仍须确认。全量词典不意味着已逐个物种测过实拍准确率。英文除已完成 TID 外，本轮未扩展。

## 回放与开发

```powershell
.\FrlgOcrReplay.exe --list-scenes
.\FrlgOcrReplay.exe .\samples\Page1\bulbasaur_1_jpn.png FRLG_JPN_SUMMARY_NAME --expected フシギダネ --output .\replay-name
.\FrlgOcrReplay.exe .\samples\Page1\deoxys_1_jpn.png FRLG_JPN_NATURE --expected しんちょう --output .\replay-nature
.\FrlgOcrReplay.exe .\samples\Page2\deoxys_1_jpn.png FRLG_JPN_HP --expected 70 --output .\replay-hp
.\FrlgOcrReplay.exe .\capture.png FRLG_JPN_NAME --roi 297,129,362,66 --output .\replay-manual
```

报告包含原始文字、词典候选、置信信息或逐位字模分数。`--output` 导出处理图片，`--repeat` 测量重复执行；静态回放不当作多张独立样本。

源码需要 .NET 10 SDK。大模型不提交进 Git，由锁定 URL 与 SHA-256 下载；发布包包含模型。

```powershell
python tools/FrlgOcrReplay/fetch_resources.py
python tools/FrlgOcrReplay/fetch_resources.py --verify
dotnet test EasyCon2.slnx -c Release
dotnet format EasyCon2.slnx --verify-no-changes --no-restore --severity error
pwsh -File tools/FrlgOcrReplay/publish.ps1
```

来源和授权见 `licenses/FRLG-OCR-SOURCES.md` 与 `licenses/frlg-ocr-resources.lock.json`。基线异步生命周期 analyzer 警告保持原样；格式检查使用 error 级别，避免已有自动修复器改变异步方法签名。
