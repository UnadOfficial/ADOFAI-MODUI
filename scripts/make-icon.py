#!/usr/bin/env python3
"""生成应用图标 assets/app.ico。

纯 Pillow 绘制，不依赖任何外部素材：
  * Fluent 风格的圆角方块 + 蓝紫渐变背景
  * 白色拼图块（Mod 的通用符号）
输出为多尺寸 ICO（16/20/24/32/40/48/64/128/256），供 exe 图标、安装程序与快捷方式使用。

用法：
    python3 scripts/make-icon.py
"""

from __future__ import annotations

import os

from PIL import Image, ImageDraw

# 超采样画布尺寸；最终各尺寸均由它 LANCZOS 缩放而来，以获得平滑边缘。
CANVAS = 1024

MARGIN = 0.055  # 圆角方块相对画布的外边距
CORNER = 0.235  # 圆角半径（相对方块边长）

GRAD_TOP = (0x5B, 0x8D, 0xEF)  # Fluent 蓝
GRAD_BOTTOM = (0x6D, 0x3F, 0xD9)  # 紫

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def _lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))  # type: ignore[return-value]


def _background() -> Image.Image:
    """圆角方块 + 垂直渐变。"""
    stripe = Image.new("RGB", (1, CANVAS))
    px = stripe.load()
    for y in range(CANVAS):
        px[0, y] = _lerp(GRAD_TOP, GRAD_BOTTOM, y / (CANVAS - 1))
    grad = stripe.resize((CANVAS, CANVAS), Image.BILINEAR)

    inset = round(CANVAS * MARGIN)
    side = CANVAS - 2 * inset
    mask = Image.new("L", (CANVAS, CANVAS), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (inset, inset, inset + side, inset + side),
        radius=round(side * CORNER),
        fill=255,
    )

    out = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out


def _puzzle_mask() -> Image.Image:
    """白色拼图块的 alpha 掩码：上方凸起（榫头）+ 右侧凹口（卯眼）。"""
    mask = Image.new("L", (CANVAS, CANVAS), 0)
    d = ImageDraw.Draw(mask)

    def pt(u: float, v: float) -> tuple[float, float]:
        return (u * CANVAS, v * CANVAS)

    body = (*pt(0.205, 0.300), *pt(0.795, 0.905))
    d.rounded_rectangle(body, radius=round(0.035 * CANVAS), fill=255)

    knob_r = 0.130 * CANVAS
    kx, ky = pt(0.500, 0.300)
    d.ellipse((kx - knob_r, ky - knob_r, kx + knob_r, ky + knob_r), fill=255)

    notch_r = 0.118 * CANVAS
    nx, ny = pt(0.795, 0.603)
    d.ellipse((nx - notch_r, ny - notch_r, nx + notch_r, ny + notch_r), fill=0)

    return mask


def make_master() -> Image.Image:
    icon = _background()

    # 白色块面：自上而下极轻微的灰阶，避免大尺寸下显得死板。
    face = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    shade = Image.new("RGB", (1, CANVAS))
    sp = shade.load()
    for y in range(CANVAS):
        sp[0, y] = _lerp((255, 255, 255), (0xE3, 0xE7, 0xF0), y / (CANVAS - 1))
    face.paste(shade.resize((CANVAS, CANVAS), Image.BILINEAR), (0, 0), _puzzle_mask())

    icon.alpha_composite(face)
    return icon


def main() -> None:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = os.path.join(root, "assets")
    os.makedirs(out_dir, exist_ok=True)

    master = make_master()
    frames = [master.resize((s, s), Image.LANCZOS) for s in SIZES]

    ico = os.path.join(out_dir, "app.ico")
    # bitmap_format="bmp"：强制所有尺寸用传统 DIB 条目而非 PNG 压缩条目，
    # 以保证 MSBuild 的 ApplicationIcon 与 rc.exe 都能正确解析。
    frames[-1].save(
        ico,
        format="ICO",
        sizes=[(s, s) for s in SIZES],
        bitmap_format="bmp",
    )

    # 顺带导出 PNG，便于在 README / 商店页面复用。
    master.resize((512, 512), Image.LANCZOS).save(os.path.join(out_dir, "app.png"), format="PNG")

    print(f"已生成 {ico}（{', '.join(f'{s}x{s}' for s in SIZES)}）")


if __name__ == "__main__":
    main()
