import sys, math
from PIL import Image, ImageDraw
out, cell, cols = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
files = sys.argv[4:]
rows = math.ceil(len(files)/cols)
sheet = Image.new("RGB", (cols*cell, rows*cell), (20,20,20))
d = ImageDraw.Draw(sheet)
for i,f in enumerate(files):
    im = Image.open(f).convert("RGB").resize((cell,cell), Image.LANCZOS)
    x,y = (i%cols)*cell, (i//cols)*cell
    sheet.paste(im,(x,y))
    d.text((x+4,y+4), f.split("/")[-1].rsplit(".",1)[0], fill=(255,60,60))
sheet.save(out, quality=88)
