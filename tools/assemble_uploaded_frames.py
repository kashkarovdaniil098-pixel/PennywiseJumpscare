from pathlib import Path
from PIL import Image, ImageDraw
import json

assets = Path("PennywiseNPC/assets")
out = Path("sprite-preview")
out.mkdir(exist_ok=True)

names = [
    "front_01.png", "front_02.png", "front_03.png", "front_04.png",
    "side_01.png", "side_02.png", "side_03.png", "side_06.png",
    "side_07.png", "side_08.png", "side_09.png", "side_10.png",
    "back_01.png", "back_02.png",
    "balloon_01.png", "balloon_02.png", "balloon_03.png",
    "action_01.png", "action_02.png", "action_03.png", "action_04.png", "action_05.png",
    "front_action_01.png", "front_action_02.png", "front_action_03.png",
    "crouch.png", "face_portrait.png",
]
cols, rows = 6, 5
tile_w, tile_h, pad, label_h = 220, 240, 12, 24
canvas = Image.new("RGBA", (pad + cols*(tile_w+pad), pad + rows*(tile_h+label_h+pad)), (55,55,55,255))
draw = ImageDraw.Draw(canvas)
details = []
for i, name in enumerate(names):
    path = assets / name
    if not path.exists():
        raise FileNotFoundError(path)
    im = Image.open(path).convert("RGBA")
    original = im.size
    im.thumbnail((tile_w-12, tile_h-12), Image.Resampling.LANCZOS)
    x = pad + (i % cols)*(tile_w+pad)
    y = pad + (i // cols)*(tile_h+label_h+pad)
    px = x + (tile_w-im.width)//2
    py = y + (tile_h-im.height)//2
    canvas.alpha_composite(im, (px, py))
    draw.text((x+4, y+tile_h+4), name, fill="white")
    details.append({"file":name, "source_size":original})
canvas.convert("RGB").save(out/"all_27_frames.png")
(out/"all_27_frames.json").write_text(json.dumps({"count":len(names),"columns":cols,"rows":rows,"frames":details}, indent=2))
print(f"OK: assembled {len(names)} uploaded PNG frames into a {cols}x{rows} contact sheet")
