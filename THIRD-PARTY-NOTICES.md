# 第三方组件与素材说明

本仓库包含或使用以下第三方组件 / 素材。各组件的版权归原作者所有。

## NLayer

- 位置：`NLayer/`
- 用途：纯 C# MP3 解码，用于音频波形与音量包络。
- 许可证：MIT。原文见 `NLayer/LICENSE`。

## Tesseract / Leptonica

- 位置：`libs/`
- 用途：可选的屏幕 OCR 识别。
- 许可证：Tesseract 为 Apache License 2.0；Leptonica 为 BSD 类许可证。具体以 `libs/放置说明.txt` 与上游项目说明为准。
- 说明：不启用 OCR 时，可以删除 `libs/`、`tessdata/`、`tessdata_best/`。

## Tesseract 语言数据

- 位置：`tessdata/`、`tessdata_best/`
- 用途：OCR 的英文 / 简体中文识别。
- 说明：语言数据来自 Tesseract 生态，发布 / 分发前请确认上游数据文件的许可证与再分发要求。

## Intel PresentMon

- 位置：本仓库不附带 `PresentMon.exe`。
- 用途：可选的游戏帧率读取。把 `PresentMon.exe` 放到程序目录后会自动启用。
- 许可证：MIT。许可证原文见 `third_party/PresentMon-LICENSE.txt`。

## 音频素材

- 位置：`音频/`
- 本仓库**不附带任何音频文件**。
- 用户自行放入 `音频/` 的提示音 / 背景音乐，版权归用户或原作者所有，**不包含在本项目任何开源许可证中**。公开分发前请确认授权。

## 字体

- 位置：`Fonts/`
- 说明：仓库只保留空目录占位。用户导入的字体文件由用户自行确认授权，不应提交到公共仓库。
