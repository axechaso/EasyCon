# FRLG OCR 来源

EasyCon 基线：`EasyConNS/EasyCon@1aed001c0e2d3a32d211c39bec26546741626bd6`，项目根目录 `LICENSE` 为 GPL-3.0。

数字读取与匹配算法移植自 PokémonAutomation：

- `PokemonAutomation/Arduino-Source@a772133ebc497aed05439f464a6222d3d83e0c10`
- `SerialPrograms/Source/PokemonFRLG/Inference/PokemonFRLG_DigitReader.cpp`
- `SerialPrograms/Source/PokemonFRLG/Inference/PokemonFRLG_TrainerIdReader.cpp`
- `SerialPrograms/Source/CommonTools/ImageMatch/ExactImageMatcher.cpp`
- `SerialPrograms/Source/PokemonFRLG/PokemonFRLG_Settings.cpp`

上述代码采用 MIT License，完整版权与许可证保存在 `licenses/PokemonAutomation-MIT.txt`。

数字模板来自 `PokemonAutomation/Packages@e8cc29cdc9e9c16faf406a1d154d70ad687b375c` 的 `Resources/PokemonFRLG/{Digits,LevelDigits,DialogDigits}/0–9.png`。

公开回放图片来自 `PokemonAutomation/CommandLineTests@46b892bd7f2106a1f34de11aa300b492aee06b83` 的 `PokemonFRLG/TrainerIdReader/nyash_jpn_45345.png` 与 `tom_eng_60895.jpg`。

模板与截图保留原始字节，下载位置、大小和 SHA-256 逐项记录在 `frlg-ocr-resources.lock.json`。图像资源不据此另行宣称具有代码的 MIT 授权；相关游戏画面与商标仍归原权利人。用户本地历史截图仅作本地验证，不进入提交或公共测试包。

移植调整：按采集高度归一化字体尺寸；同一区域采用多阈值一致性；收紧 RMSD 与第二候选差距；严格检查五位数字及 16-bit TID 范围；不接受上游“跳过失败数字块后拼接”的结果。默认区域加少量边距，手动选区保持原样。
