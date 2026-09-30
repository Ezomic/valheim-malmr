"""Draws three candidate Thunderstore icons for Malmr, and a lineup to pick from.

    python tools/icon_candidates.py [out_dir]

Writes a.png, b.png, c.png (256x256) and lineup.png into out_dir, which defaults to
<temp>/malmr-icons so nothing lands in the repo until a pick is made. Needs Pillow only.


THE STYLE BEING MATCHED

The same measured style as Varda's tools/icon_candidates.py, which took it off Stund's and
Kvedja's icons, so the pack's icons read as one set:

    background   vertical linear gradient, (170,168,165) at the top row to (126,125,123)
                 at the bottom, constant across each row, square corners
    body         one flat mass in (65,64,63), no outline and no drop shadow
    accent       exactly one element in (191,133,74), and only ever on the dark body:
                 orange on the pale background loses most of its contrast
    detail       (110,109,107) for the brighter marks, (92,91,89) for the quieter ones
    strokes      6-17 px, always with round ends
    margin       the body sits 34-39 px in from the sides

The helpers down to compose() are Varda's, copied rather than imported so this script does
not reach into another repo. The one accent here is the ore vein, and every design breaks it
across the gaps so it still reads as one line through the whole deposit: that is the mod.
Gaps are 7 px at their narrowest, the least that still shows as a gap at 64.

Drawn and dropped on the way here, so they are not tried again: a heap of chunks (Varda's
rejected cairn, or a brick pyramid), a tall outcrop in slipped slabs (Thralls' standing stone
with its rune), the deposit sliced upright and fanning open (Utangard's palisade and its
bar), and the vein's chunk rising out between two halves (a necktie). Any vein that is one
even-width kinked line rising to the right reads as a stock chart.
"""
import math
import os
import sys
import tempfile
from PIL import Image, ImageChops, ImageDraw

N = 256
S = 4                     # supersample while drawing

BG_TOP = (170, 168, 165)
BG_BOTTOM = (126, 125, 123)
INK = (65, 64, 63)
ACCENT = (191, 133, 74)
LIGHT = (110, 109, 107)
DIM = (92, 91, 89)

GAP = 7


class Layer:
    """One flat colour, drawn as a supersampled coverage mask."""

    def __init__(self, colour):
        self.colour = colour
        self.img = Image.new("L", (N * S, N * S), 0)
        self.d = ImageDraw.Draw(self.img)

    def poly(self, pts, fill=255):
        self.d.polygon([(x * S, y * S) for x, y in pts], fill=fill)

    def circle(self, cx, cy, r, fill=255):
        self.d.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], fill=fill)

    def rect(self, x0, y0, x1, y1, r=0, fill=255):
        self.d.rounded_rectangle([x0 * S, y0 * S, x1 * S, y1 * S], radius=r * S, fill=fill)

    def stroke(self, pts, w, fill=255):
        """A polyline with round caps and joints, the way every stroke in the set ends."""
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            self.d.line([x0 * S, y0 * S, x1 * S, y1 * S], fill=fill, width=round(w * S))
        for x, y in pts:
            self.circle(x, y, w / 2, fill)


def background():
    img = Image.new("RGB", (N, N))
    for y in range(N):
        t = y / (N - 1)
        c = tuple(round(BG_TOP[i] + (BG_BOTTOM[i] - BG_TOP[i]) * t) for i in range(3))
        img.paste(c, (0, y, N, y + 1))
    return img


def compose(layers):
    img = background()
    for layer in layers:
        mask = layer.img.resize((N, N), Image.LANCZOS)
        img.paste(layer.colour, (0, 0, N, N), mask)
    return img


# ------------------------------------------------------------------ new helpers

def rounded(pts, r, steps=10):
    """A polygon with every corner filleted at radius r. Vanilla rock is faceted, so the
    deposits are polygons rather than Varda's pebbles, but a hard corner at 256 sharpens into
    a spike at 64 and nothing else in the set has one."""
    out = []
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i - 1]
        bx, by = pts[i]
        cx, cy = pts[(i + 1) % n]
        u1 = (ax - bx, ay - by)
        u2 = (cx - bx, cy - by)
        l1, l2 = math.hypot(*u1), math.hypot(*u2)
        u1 = (u1[0] / l1, u1[1] / l1)
        u2 = (u2[0] / l2, u2[1] / l2)
        theta = math.acos(max(-1.0, min(1.0, u1[0] * u2[0] + u1[1] * u2[1])))
        if theta < 1e-3 or abs(theta - math.pi) < 1e-3:
            out.append((bx, by))
            continue
        t = min(r / math.tan(theta / 2), l1 / 2, l2 / 2)
        rr = t * math.tan(theta / 2)
        bis = (u1[0] + u2[0], u1[1] + u2[1])
        bl = math.hypot(*bis)
        bis = (bis[0] / bl, bis[1] / bl)
        d = rr / math.sin(theta / 2)
        ox, oy = bx + bis[0] * d, by + bis[1] * d
        p1 = (bx + u1[0] * t, by + u1[1] * t)
        p2 = (bx + u2[0] * t, by + u2[1] * t)
        a1 = math.atan2(p1[1] - oy, p1[0] - ox)
        a2 = math.atan2(p2[1] - oy, p2[0] - ox)
        da = (a2 - a1 + math.pi) % (2 * math.pi) - math.pi
        for j in range(steps + 1):
            a = a1 + da * j / steps
            out.append((ox + rr * math.cos(a), oy + rr * math.sin(a)))
    return out


def layer(colour, img):
    """A Layer over a mask built elsewhere, still drawable."""
    out = Layer(colour)
    out.img = img
    out.d = ImageDraw.Draw(img)
    return out


def blank():
    return Image.new("L", (N * S, N * S), 0)


def mask_of(draw):
    """A throwaway Layer, for building a mask to cut or split with."""
    scratch = Layer((0, 0, 0))
    draw(scratch)
    return scratch.img


def piece(mask, seed):
    """The connected part of `mask` holding `seed`, found by flood fill. Cracks are drawn as
    erased strokes that run out past the rock, so each chunk is its own island."""
    work = mask.point(lambda v: 255 if v >= 128 else 0)
    ImageDraw.floodfill(work, (seed[0] * S, seed[1] * S), 128)
    return work.point(lambda v: 255 if v == 128 else 0)


def shifted(mask, dx, dy):
    out = blank()
    out.paste(mask, (round(dx * S), round(dy * S)))
    return out


def centre(mask):
    x0, y0, x1, y1 = mask.getbbox()
    return (x0 + x1) / 2 / S, (y0 + y1) / 2 / S


def burst(rock, vein, seeds, origin, push):
    """Moves each chunk of a cracked rock `push` px away from `origin`, carrying its stretch
    of the vein with it. Returns the new rock and vein masks."""
    out_rock, out_vein = blank(), blank()
    for seed in seeds:
        chunk = piece(rock, seed)
        cx, cy = centre(chunk)
        dx, dy = cx - origin[0], cy - origin[1]
        k = push / max(1e-6, math.hypot(dx, dy))
        dx, dy = dx * k, dy * k
        out_rock = ImageChops.lighter(out_rock, shifted(chunk, dx, dy))
        out_vein = ImageChops.lighter(out_vein, shifted(ImageChops.multiply(vein, chunk), dx, dy))
    return out_rock, out_vein


def spline(pts, per=24):
    """Catmull-Rom through the control points, so a vein bends instead of kinking. A kinked
    polyline of even width rising left to right reads as a stock chart, not as ore."""
    ext = [pts[0]] + list(pts) + [pts[-1]]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for j in range(per):
            t = j / per
            out.append(tuple(0.5 * (2 * p1[k] + (-p0[k] + p2[k]) * t
                                    + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t * t
                                    + (-p0[k] + 3 * p1[k] - 3 * p2[k] + p3[k]) * t * t * t)
                             for k in range(2)))
    out.append(pts[-1])
    return out


def band(l, pts, widths, fill=255):
    """A vein: a smooth stroke whose width follows `widths` (one per control point), with
    round ends like every other stroke in the set."""
    path = spline(pts)
    per = (len(path) - 1) / (len(pts) - 1)
    for i, (x, y) in enumerate(path):
        f = i / per
        k = min(int(f), len(widths) - 2)
        w = widths[k] + (widths[k + 1] - widths[k]) * (f - k)
        l.circle(x, y, w / 2, fill)
        if i:
            px, py = path[i - 1]
            l.d.line([px * S, py * S, x * S, y * S], fill=fill, width=round(w * S))


def cracked(outline, cracks):
    def draw(l):
        l.poly(outline)
        for c in cracks:
            l.stroke(c, GAP, fill=0)
    return mask_of(draw)


def place(pts, cx, cy, rot):
    r = math.radians(rot)
    c, s = math.cos(r), math.sin(r)
    return [(cx + x * c - y * s, cy + x * s + y * c) for x, y in pts]


# ------------------------------------------------------------------- designs

def design_a():
    """The deposit bursting: one boulder cracked right through into four chunks, each pushed
    out from the middle, carrying its stretch of the vein. The cracks wander and do not meet
    at one point, because straight cracks from one centre read as a pie chart."""
    # drawn a few px inside the usual margin, because the push carries every chunk outward
    outline = rounded([(44, 174), (56, 110), (94, 66), (146, 50), (192, 70), (212, 122),
                       (206, 186), (180, 208), (70, 208)], 16)
    j1, j2 = (120, 124), (134, 166)
    cracks = [
        [(150, 20), (142, 58), (116, 92), j1],
        [j1, (92, 136), (62, 124), (20, 132)],
        [j1, (140, 144), j2, (122, 196), (128, 240)],
        [j2, (168, 156), (196, 168), (240, 160)],
    ]
    rock = cracked(outline, cracks)
    vein = mask_of(lambda l: band(l, [(70, 98), (104, 116), (140, 128), (170, 156), (196, 176)],
                                  [8, 15, 17, 14, 8]))
    seeds = [(80, 100), (170, 100), (90, 180), (180, 196)]
    rock, vein = burst(rock, vein, seeds, (128, 136), 9)
    return compose([layer(INK, rock), layer(ACCENT, vein)])


def design_b():
    """The strike: a pickaxe landing on a low deposit, and the cracks running from where its
    point lands right through the rock and across the vein."""
    mound = rounded([(36, 214), (40, 184), (66, 160), (112, 150), (166, 152), (204, 170),
                     (220, 214)], 14)
    # where the point lands, just clear of the surface: buried in the rock, the point merged
    # with the cracks into one grey knot at 64 px
    tip = (126, 146)
    cracks = [
        [tip, (110, 176), (120, 198), (108, 240)],
        [tip, (154, 174), (174, 194), (206, 202), (240, 206)],
    ]
    rock = cracked(mound, cracks)
    vein = mask_of(lambda l: band(l, [(60, 190), (96, 182), (134, 188), (170, 180), (198, 186)],
                                  [9, 15, 16, 15, 9]))
    vein = ImageChops.multiply(vein, rock)

    # The pickaxe: a curved head, pointed both ends, on a straight haft rising to the right.
    # The whole head stays above the rock: with half of it buried, what is left reads as a
    # scythe blade rather than a pick.
    head_c = (106, 102)
    grip = (214, 48)
    hx, hy = grip[0] - head_c[0], grip[1] - head_c[1]
    hl = math.hypot(hx, hy)
    ux, uy = hx / hl, hy / hl          # along the haft, towards the grip
    px, py = -uy, ux                   # across it: +p points down-right, at the rock
    half, bow = 46, 6

    def head_pts(extra=0.0):
        top, bot = [], []
        steps = 40
        for i in range(steps + 1):
            s = -1 + 2 * i / steps
            w = 3 + 8 * (1 - abs(s) ** 1.4) + extra
            # bows away from the grip, the way a pick's points turn back towards the user
            off = -bow * (1 - s * s)
            cx = head_c[0] + px * half * s + ux * off
            cy = head_c[1] + py * half * s + uy * off
            top.append((cx + ux * w, cy + uy * w))
            bot.append((cx - ux * w, cy - uy * w))
        return top + bot[::-1]

    haft = [(head_c[0] - ux * 4, head_c[1] - uy * 4), grip]
    # the eye the haft goes through, which is what makes the head a pick and not a crescent
    eye = [(head_c[0] + ux * a + px * b, head_c[1] + uy * a + py * b)
           for a, b in ((-11, -13), (20, -11), (20, 11), (-11, 13))]

    def pick(l, fill, grow=0):
        l.poly(head_pts(grow), fill=fill)
        l.stroke(haft, 15 + 2 * grow, fill=fill)
        l.poly(rounded(eye, 5), fill=fill)
        if grow:
            l.stroke(head_pts(0), 2 * grow, fill=fill)

    # Cut a gap round the pick where it enters the rock, then draw it, so the two ink masses
    # stay two shapes instead of merging into one blob.
    ink = layer(INK, rock)
    pick(ink, 0, GAP)
    vein = ImageChops.multiply(vein, ink.img)
    pick(ink, 255)
    return compose([ink, layer(ACCENT, vein)])


def design_c():
    """Spilling out: the deposit's flank has broken away and two chunks are falling out of
    the break, the vein running on out of the rock and bending down through them as one
    line. A third chunk already on the ground was cut: at 64 px it was a speck."""
    main = rounded([(36, 214), (38, 160), (54, 112), (94, 72), (124, 58), (160, 80),
                    (150, 102), (160, 122), (144, 140), (152, 160), (134, 178), (140, 198),
                    (130, 214)], 10)
    ink = layer(INK, mask_of(lambda l: l.poly(main)))
    chunks = [
        ([(-24, -20), (18, -24), (26, 6), (6, 24), (-22, 16)], 186, 118, 14),
        ([(-21, -19), (19, -19), (22, 14), (-4, 22), (-22, 9)], 196, 186, -10),
    ]
    for pts, cx, cy, rot in chunks:
        ink.poly(rounded(place(pts, cx, cy, rot), 5))
    vein = ImageChops.multiply(
        mask_of(lambda l: band(l, [(64, 142), (104, 130), (148, 122), (184, 126), (198, 190)],
                               [8, 14, 16, 15, 10])), ink.img)
    return compose([ink, layer(ACCENT, vein)])


DESIGNS = {"a": design_a, "b": design_b, "c": design_c}


# -------------------------------------------------------------------- lineup

def lineup(icons, out):
    """Each icon at 256 on the top row and at 64, its real Thunderstore list size, below."""
    pad, label = 24, 18
    w = pad + len(icons) * (N + pad)
    h = pad + label + N + pad + 64 + pad
    sheet = Image.new("RGB", (w, h), (32, 34, 38))
    d = ImageDraw.Draw(sheet)
    for i, (name, img) in enumerate(icons):
        x = pad + i * (N + pad)
        d.text((x, pad - 4), name, fill=(220, 220, 220))
        sheet.paste(img, (x, pad + label))
        sheet.paste(img.resize((64, 64), Image.LANCZOS), (x, pad + label + N + pad))
    sheet.save(out)


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(tempfile.gettempdir(), "malmr-icons")
    os.makedirs(out_dir, exist_ok=True)
    made = []
    for key, build in DESIGNS.items():
        img = build()
        img.save(os.path.join(out_dir, key + ".png"))
        made.append((key, img))

    repos = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    refs = []
    for mod in ("stund", "kvedja", "varda"):
        path = os.path.join(repos, mod, "icon.png")
        if os.path.exists(path):
            refs.append((mod, Image.open(path).convert("RGB")))
    lineup(refs + made, os.path.join(out_dir, "lineup.png"))
    print("wrote", out_dir)


if __name__ == "__main__":
    main()
