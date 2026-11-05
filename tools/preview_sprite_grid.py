from pathlib import Path
from PIL import Image, ImageDraw
import json
source = Path("PennywiseNPC/assets/file_0000000062f4821096252aeb27f4faa2.png")
out = Path("sprite-preview")
out.mkdir(exist_ok=True)
im = Image.open(source).convert("RGBA")
w, h = im.size
# 27 portrait frames: 9 columns x 3 rows.
cols, rows = 9, 3
xs = [round(i*w/cols) for i in range(cols+1)]
ys = [round(i*h/rows) for i in range(rows+1)]
frames = [im.crop((xs[c],ys[r],xs[c+1],ys[r+1])) for r in range(rows) for c in range(cols)]
scale, pad, label_h = 2, 10, 24
cell_w = max(f.width for f in frames)*scale
cell_h = max(f.height for f in frames)*scale
canvas = Image.new("RGB",(pad+cols*(cell_w+pad),pad+rows*(cell_h+label_h+pad)),"#444444")
draw = ImageDraw.Draw(canvas)
for i,frame in enumerate(frames):
 r,c=divmod(i,cols)
 bg=Image.new("RGBA",frame.size,(90,90,90,255)); bg.alpha_composite(frame)
 tile=bg.convert("RGB").resize((frame.width*scale,frame.height*scale),Image.Resampling.NEAREST)
 x,y=pad+c*(cell_w+pad),pad+r*(cell_h+label_h+pad)
 canvas.paste(tile,(x,y)); draw.text((x,y+cell_h+3),f"Frame {i+1:02d}",fill="white")
canvas.save(out/"sprite_grid_preview.png")
(out/"sprite_grid_info.json").write_text(json.dumps({"source_size":[w,h],"grid":[cols,rows],"frame_count":len(frames),"frame_sizes":[[f.width,f.height] for f in frames]},indent=2))
print(f"OK: {w}x{h}; grid {cols}x{rows}; cell {frames[0].width}x{frames[0].height}")
