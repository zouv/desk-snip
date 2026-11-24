# DeskSnip 图标

图标定位：**蓝色截图取景框 + 白色图钉**。

- 蓝色渐变圆角底：应用品牌色，也是任务栏/托盘中的主识别色；
- 四角取景框：表示截图、框选与捕获；
- 白色图钉：表示钉图与桌面常驻，斜向放置增加动势。

本目录按 `markleaf/docs/icons` 的目录规范归档原版图标、上一版浅底版、当前毛玻璃版和生成脚本。生产图标由
`src/DeskSnip.App/Assets/DeskSnipIcon.xaml` 经 `scripts/assets.ps1` 生成；毛玻璃版已同步到该 XAML，
原版与浅底版 SVG 保留作历史归档。

## 文件

- `desksnip-original.svg`：原版矢量源，256 × 256 viewBox；由生产 XAML 1:1 转写，无字体、脚本或外部依赖。
- `desksnip-original-1024.png`：原版大尺寸主图与复用源。
- `desksnip-original-{512,256,128,64,48,32,16}.png`：原版常用尺寸。
- `desksnip-original.ico`：原版 Windows 多尺寸图标，含 16、24、32、48、64、128、256 px 七个 32 位 RGBA PNG 帧；24 px 帧仅内嵌于 ICO。
- `desksnip-light.svg`：上一版矢量源（浅底版）；从原版出发，弱化蓝色背景、强化取景框与图钉。
- `desksnip-light-{1024,512,256,128,64,48,32,16}.png`、`desksnip-light.ico`：浅底版的各尺寸输出。
- `desksnip-frosted.svg`：当前生产设计的矢量源（毛玻璃版）；白色玻璃底、细冻霜颗粒、玻璃高光与边缘高光。
- `desksnip-frosted-{1024,512,256,128,64,48,32,16}.png`、`desksnip-frosted.ico`：毛玻璃版的各尺寸输出。
- `desksnip-compare.png`：原版与浅底版在浅色/深色背景下的尺寸对比。
- `desksnip-compare-frosted.png`：浅底版与毛玻璃版的对比。
- `_build.py`：从三个 SVG 重新生成全部 PNG / ICO / 对比图；不会修改 `src/` 下的生产资源。

## 浅底版（上一版）

方向：**让背景退后，让取景框和图钉成为主视觉**。构图、圆角、旋转角都沿用原版。

- 背景由深蓝渐变改为浅蓝渐变（`#E6F2FF` → `#CEE5FF` → `#C2DCFF`），保留一点蓝色以便白色任务栏上仍能看清图标轮廓；
- 四角取景框臂长从 34 加强到 40，线宽从 14 加到 16，颜色改为品牌蓝 `#2A7BF0`；
- 图钉整体放大 4%，同为品牌蓝，保留原版的浅蓝高光带（改为 `#C7E6FF`），不改图钉造型；
- 浅底、双色（浅蓝底 + 品牌蓝前景）减少视觉噪音，小尺寸下取景框与图钉更清楚。

曾作为生产版；现由毛玻璃版取代，SVG 保留作历史归档。

## 毛玻璃版（当前生产设计）

方向：把背景从浅蓝渐变改成有冻霜质感的白色玻璃，前景取景框/图钉不变。

- 白色玻璃底：`#FFFFFF` → `#EFF7FF` → `#D2E7FF` 渐变，叠加蓝色雾光与底部柔和阴影；
- 玻璃高光：斜向白色反光带 + 内侧白/淡蓝双描边，形成玻璃厚度；
- 冻霜颗粒：约 1500 个低透明度白色/淡蓝小点，以几何路径内嵌，不依赖 SVG 滤镜或额外运行时；
- 颗粒在 256/1024 下可见，16–32 px 下自动弱化为柔和纹理。

已把这套几何路径写入生产 XAML，并重新生成 `src/DeskSnip.App/Assets/` 下的 PNG/ICO（2026-11-23）。

## 配色

原版：

- 圆角底：`#459FFE` → `#2A7BF0`（58%）→ `#235DCC` 135° 渐变。
- 前景：`#FFFFFF`。
- 图钉高光带：`#AFDEFF`。

浅底版：

- 圆角底：`#E6F2FF` → `#CEE5FF`（58%）→ `#C2DCFF` 135° 渐变。
- 取景框 / 图钉：`#2A7BF0`。
- 图钉高光带：`#C7E6FF`。

毛玻璃版（当前生产）：

- 玻璃底：`#FFFFFF` → `#EFF7FF`（52%）→ `#D2E7FF`；蓝色雾光 `#7FB8FF` 至 38%。
- 取景框 / 图钉：`#2A7BF0`。
- 图钉高光带：`#C7E6FF`。
- 冻霜颗粒：白色 10–26% / 淡蓝 7–14%。

## 编辑与导出

- 本目录的 `desksnip-frosted.svg` 是当前生产设计的矢量源；`src/DeskSnip.App/Assets/DeskSnipIcon.xaml` 是构建用源，两者需保持一致。
- 生产 XAML 直接内嵌冻霜颗粒路径；改动 `desksnip-frosted.svg` 后需同步 XAML。
- `assets.ps1` 用 `ColorBlend` 转换线性渐变：每个渐变必须从 `Offset="0"` 开始、以 `Offset="1"` 结束，否则渲染会报错。
- 修改矢量后执行：

```bash
python docs/icons/_build.py
```

- 脚本用无头 Microsoft Edge 在 2048 × 2048 渲染，裁掉透明边后按目标尺寸独立缩放，不逐级缩图。
- 需要 Python 3 + Pillow + NumPy，以及本机 Microsoft Edge；找不到 Edge 时用 `DESKSNIP_EDGE` 指向 `msedge.exe`。
- `.png` / `.ico` 是普通二进制文件，不要改成 Git LFS 指针；提交前确认生成结果与 SVG 一致。
