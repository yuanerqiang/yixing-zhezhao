# -*- coding: utf-8 -*-
"""重新生成三张截图：大小 >= 50KB（支付宝要求不能小于50KB），保持清晰。"""
import os
from PIL import Image

SRC = r"C:\Users\yeq_4\Documents\ChatGPT\异性遮罩\alipay-demo-site"
FILES = [
    ("screenshot-1-home.png",    "upload-1-home.jpg"),
    ("screenshot-2-product.png", "upload-2-product.jpg"),
    ("screenshot-3-checkout.png","upload-3-checkout.jpg"),
]
MIN_SIZE = 50 * 1024  # 必须 >= 50KB

def compress(src_path, dst_path):
    img = Image.open(src_path).convert("RGB")
    w, h = img.size
    best = None
    # 从高质量开始，逐步调整直到 >= 50KB
    for quality in (95, 92, 90, 88, 85, 82, 80, 75):
        im = img
        for width in (1280, 1400, 1500, 1600):
            cur = im
            if width > img.size[0]:
                cur = img.resize((width, int(h * width / img.size[0])), Image.LANCZOS)
            cur.save(dst_path, "JPEG", quality=quality, optimize=True)
            size = os.path.getsize(dst_path)
            if size >= MIN_SIZE:
                if best is None or abs(size - 120*1024) < abs(best[0] - 120*1024):
                    best = (size, cur.size, quality)
                break
    if best is None:
        img.save(dst_path, "JPEG", quality=95, optimize=True)
        return os.path.getsize(dst_path), img.size, 95
    return best

for src, dst in FILES:
    src_p = os.path.join(SRC, src)
    dst_p = os.path.join(SRC, dst)
    if os.path.exists(src_p):
        size, dim, q = compress(src_p, dst_p)
        flag = "OK" if size >= MIN_SIZE else "TOO SMALL!"
        print(f"{dst}: {size/1024:.1f} KB, {dim[0]}x{dim[1]}, q{q}  [{flag}]")
    else:
        print(f"MISSING: {src_p}")
