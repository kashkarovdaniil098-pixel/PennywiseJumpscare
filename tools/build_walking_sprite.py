from pathlib import Path
from PIL import Image, ImageDraw
import json

assets = Path("PennywiseNPC/assets")
out = assets / "sprites_candidate.png"

# Stardew-compatible 4x4 sheet: four animation frames per direction.
# Rows: down, right, up, left. Keep the existing sprites.png untouched.
groups = [
    ("down", ["front_01.png", "front_02.png", "front_03.png", "front_04.png"]),
    ("right", ["side_06.png", "side_07.png", "side_08.png", "side_09.png"]),
    ("up", ["back_01.png", "back_02.png", "back_01.png", "back_02.png"]),
]

FW, FH = 16, 32
sheet = Image.new("RGBA", (FW*4, FH*4), (0,0,0,0))
records = []

def make_frame(name, mirror=False):
    im = Image.open(assets/name).convert("RGBA")
    # Remove fully transparent margins; preserve all visible character pixels.
    alpha = im.getchannel("A")
    bbox = alpha.getbbox()
    if bbox:
        im = im.crop(bbox)
    if mirror:
        im = im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    # Fit inside 16x30, preserve aspect ratio, anchor to the bottom.
    im.thumbnail((FW, FH-2), Image.Resampling.LANCZOS)
    frame = Image.new("RGBA", (FW,FH), (0,0,0,0))
    frame.alpha_composite(im, ((FW-im.width)//2, FH-im.height))
    return frame

for row, (direction, names) in enumerate(groups):
    for col, name in enumerate(names):
        frame = make_frame(name)
        sheet.alpha_composite(frame, (col*FW,row*FH))
        records.append({"direction":direction,"frame":col+1,"source":name,"size":[FW,FH]})

# Left-facing walk is the mirrored right-facing walk.
for col, name in enumerate(groups[1][1]):
    frame = make_frame(name, mirror=True)
    sheet.alpha_composite(frame, (col*FW,3*FH))
    records.append({"direction":"left","frame":col+1,"source":name,"mirrored":True,"size":[FW,FH]})

sheet.save(out)
preview = sheet.resize((FW*8,FH*8), Image.Resampling.NEAREST)
Path("sprite-preview").mkdir(parents=True, exist_ok=True)
preview.save("sprite-preview/sprites_candidate_preview.png")
Path("sprite-preview/sprites_candidate_info.json").write_text(json.dumps({
    "output":str(out),"sheet_size":list(sheet.size),"frame_size":[FW,FH],
    "layout":"4 columns x 4 rows: down, right, up, left","frames":records
},indent=2))
print(f"Created candidate {out}: {sheet.width}x{sheet.height}, 16 frames")
