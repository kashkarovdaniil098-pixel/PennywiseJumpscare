from pathlib import Path
from PIL import Image, ImageDraw
import json

source = Path("PennywiseNPC/assets/file_0000000062f4821096252aeb27f4faa2.png")
out = Path("sprite-preview")
out.mkdir(exist_ok=True)

im = Image.open(source).convert("RGBA")
w, h = im.size
cols, rows = 3, 9
if w % cols or h % rows:
    raise SystemExit(f"Sheet {w}x{h} is not evenly divisible into {cols}x{rows} cells")

cw, ch = w // cols, h // rows
canvas = Image.new("RGB", (cols* (cw*4+12)+12, rows*(ch*4+32)+12), "#333333")
draw = ImageDraw.Draw(canvas)
for i in range(rows*cols):
    r, c = divmod(i, cols)
    frame = im.crop((c*cw, r*ch, (c+1)*cw, (r+1)*ch))
    bg = Image.new("RGBA", frame.size, (90,90,90,255))
    bg.alpha_composite(frame)
    tile = bg.convert("RGB").resize((cw*4,ch*4), Image.Resampling.NEAREST)
    x, y = 12+c*(cw*4+12), 12+r*(ch*4+32)
    canvas.paste(tile,(x,y))
    draw.text((x,y+ch*4+4),f"Frame {i+1:02d}",fill="white")
canvas.save(out/"sprite_grid_preview.png")
(out/"sprite_grid_info.json").write_text(json.dumps({
 "source_size":[w,h],"grid":[cols,rows],"frame_size":[cw,ch],"frame_count":27
},indent=2))
print(f"OK: {w}x{h}; 3x9; each frame {cw}x{ch}")
