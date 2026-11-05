from pathlib import Path
from PIL import Image, ImageDraw
import json

source = Path("PennywiseNPC/assets/file_0000000062f4821096252aeb27f4faa2.png")
out = Path("sprite-preview")
out.mkdir(exist_ok=True)

im = Image.open(source).convert("RGBA")
w, h = im.size
cols, rows = 3, 9

# Use rounded grid boundaries so dimensions like 1024px can be split
# into 9 near-equal rows (113/114px) without rejecting the sheet.
xs = [round(i * w / cols) for i in range(cols + 1)]
ys = [round(i * h / rows) for i in range(rows + 1)]
frames = []
for r in range(rows):
    for c in range(cols):
        frames.append(im.crop((xs[c], ys[r], xs[c + 1], ys[r + 1])))

scale = 4
pad, label_h = 12, 28
cell_w = max(frame.width for frame in frames) * scale
cell_h = max(frame.height for frame in frames) * scale
canvas = Image.new("RGB", (pad + cols * (cell_w + pad), pad + rows * (cell_h + label_h + pad)), "#333333")
draw = ImageDraw.Draw(canvas)

for i, frame in enumerate(frames):
    r, c = divmod(i, cols)
    bg = Image.new("RGBA", frame.size, (90, 90, 90, 255))
    bg.alpha_composite(frame)
    tile = bg.convert("RGB").resize((frame.width * scale, frame.height * scale), Image.Resampling.NEAREST)
    x = pad + c * (cell_w + pad)
    y = pad + r * (cell_h + label_h + pad)
    canvas.paste(tile, (x, y))
    draw.text((x, y + cell_h + 4), f"Frame {i + 1:02d} ({frame.width}x{frame.height})", fill="white")

canvas.save(out / "sprite_grid_preview.png")
(out / "sprite_grid_info.json").write_text(json.dumps({
    "source_size": [w, h],
    "grid": [cols, rows],
    "frame_count": len(frames),
    "frame_sizes": [[f.width, f.height] for f in frames],
    "x_boundaries": xs,
    "y_boundaries": ys
}, indent=2))
print(f"OK: {w}x{h}; 3x9; frame sizes range {min(f.width for f in frames)}x{min(f.height for f in frames)} to {max(f.width for f in frames)}x{max(f.height for f in frames)}")
