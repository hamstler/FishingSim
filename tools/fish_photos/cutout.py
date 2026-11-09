#!/usr/bin/env python3
"""
Turns a side-view fish photograph on a plain background into a game asset:
  <id>.webp  the fish cut out (RGBA), levelled, head on the left, cropped, 2048 px wide (WebP, a fifth of a PNG)
  <id>.json  the body outline without the fins (top and bottom edge at 128 points along the length),
             used to loft the 3D body so its silhouette matches the photo exactly.

Usage: python3 tools/fish_photos/cutout.py <photo> <out_dir> <id> [--flip] [--threshold T] [--grabcut [--box x0,y0,x1,y1]]
                                          [--isnet model.onnx [--open N]]
Pillow and numpy; --grabcut (busy backgrounds) also needs opencv-python-headless; --isnet (any background, keeps
translucent fins soft) needs onnxruntime and the ISNet "general use" model (isnet-general-use.onnx, as published with
the rembg project); the model is a tool only and is not shipped with the game.
"""
import json
import math
import sys
from collections import deque

import numpy as np
from PIL import Image, ImageFilter

OUT_WIDTH = 2048


def background_colour(rgb):
    h, w, _ = rgb.shape
    border = np.concatenate([rgb[0, :], rgb[-1, :], rgb[:, 0], rgb[:, -1]])
    return np.median(border, axis=0)


def flood_from_border(mask):
    """True where a False pixel connects to the image border (the real background)."""
    h, w = mask.shape
    seen = np.zeros_like(mask, dtype=bool)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if not mask[y, x] and not seen[y, x]:
                seen[y, x] = True
                q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if not mask[y, x] and not seen[y, x]:
                seen[y, x] = True
                q.append((y, x))
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and not mask[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True
                q.append((ny, nx))
    return seen


def largest_component(mask):
    h, w = mask.shape
    label = np.zeros(mask.shape, dtype=np.int32)
    best, best_size, current = 0, 0, 0
    for sy in range(h):
        for sx in range(w):
            if mask[sy, sx] and label[sy, sx] == 0:
                current += 1
                size = 0
                q = deque([(sy, sx)])
                label[sy, sx] = current
                while q:
                    y, x = q.popleft()
                    size += 1
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = y + dy, x + dx
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and label[ny, nx] == 0:
                            label[ny, nx] = current
                            q.append((ny, nx))
                if size > best_size:
                    best, best_size = current, size
    return label == best


def segment(img, threshold):
    """Mask of the fish at a working resolution (plain background assumed)."""
    small = img.copy()
    small.thumbnail((640, 640))
    rgb = np.asarray(small.convert("RGB")).astype(np.float32)
    bg = background_colour(rgb)
    dist = np.sqrt(((rgb - bg) ** 2).sum(axis=2))
    # Soft shadows on a white background are grey and unsaturated: count saturation and darkness, not just distance.
    sat = rgb.max(axis=2) - rgb.min(axis=2)
    score = dist + sat * 0.8
    mask = score > threshold
    mask = np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).filter(ImageFilter.MedianFilter(5))) > 127
    mask = largest_component(mask)
    mask = ~flood_from_border(mask)              # fill holes (silvery highlights, the eye)
    return mask, small.size


def segment_grabcut(img, box_frac=None):
    """
    Mask of the fish on a busy background (stones, grass, a hand, a net) with OpenCV's GrabCut, seeded with a box
    around the fish (the middle of the photo unless given as fractions x0,y0,x1,y1).
    """
    import cv2
    small = img.copy()
    small.thumbnail((900, 900))
    arr = cv2.cvtColor(np.asarray(small.convert("RGB")), cv2.COLOR_RGB2BGR)
    h, w = arr.shape[:2]
    x0, y0, x1, y1 = box_frac or (0.04, 0.08, 0.96, 0.92)
    rect = (int(x0 * w), int(y0 * h), int((x1 - x0) * w), int((y1 - y0) * h))
    gc = np.zeros((h, w), np.uint8)
    bgd, fgd = np.zeros((1, 65), np.float64), np.zeros((1, 65), np.float64)
    cv2.grabCut(arr, gc, rect, bgd, fgd, 8, cv2.GC_INIT_WITH_RECT)
    mask = (gc == cv2.GC_FGD) | (gc == cv2.GC_PR_FGD)
    mask = np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).filter(ImageFilter.MedianFilter(5))) > 127
    mask = largest_component(mask)
    mask = ~flood_from_border(mask)
    return mask, small.size


def segment_isnet(img, model, box_frac=None, opening=5):
    """
    Mask of the fish from the ISNet salient-object model: a binary mask at a working resolution for levelling and
    measuring, and a soft alpha at full resolution so thin, see-through fins keep their soft edge.
    """
    import onnxruntime as ort
    sess = ort.InferenceSession(model, providers=["CPUExecutionProvider"])
    src = img
    if box_frac:
        x0, y0, x1, y1 = box_frac
        src = img.crop((int(x0 * img.width), int(y0 * img.height), int(x1 * img.width), int(y1 * img.height)))
    arr = np.asarray(src.resize((1024, 1024), Image.LANCZOS)).astype(np.float32)
    arr = arr / max(arr.max(), 1.0) - np.array([0.485, 0.456, 0.406], np.float32)
    pred = sess.run(None, {sess.get_inputs()[0].name: arr.transpose(2, 0, 1)[None]})[0][0, 0]
    pred = (pred - pred.min()) / max(pred.max() - pred.min(), 1e-6)
    soft = Image.fromarray((pred * 255).astype(np.uint8)).resize(src.size, Image.LANCZOS)
    if box_frac:
        full = Image.new("L", img.size, 0)
        full.paste(soft, (int(box_frac[0] * img.width), int(box_frac[1] * img.height)))
        soft = full
    small = soft.copy()
    small.thumbnail((640, 640))
    mask = np.asarray(small) > 127
    # A small opening cuts thin things touching the fish (a stem, a line, a twig) loose, so they are dropped with
    # the other stray pieces.
    k = max(3, opening | 1)
    mask = np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).filter(ImageFilter.MinFilter(k)).filter(ImageFilter.MaxFilter(k))) > 127
    mask = largest_component(mask)
    keep = Image.fromarray(mask.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(7)).resize(img.size, Image.BILINEAR)
    whole = ~flood_from_border(mask)
    holes = Image.fromarray((whole & ~mask).astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(3)).resize(img.size, Image.BILINEAR)
    # Stray blobs away from the fish are dropped; holes inside it (silver highlights) are filled.
    soft_arr = np.asarray(soft).astype(np.float32) * (np.asarray(keep) / 255.0)
    filled = np.maximum(soft_arr, np.asarray(holes).astype(np.float32))
    return whole, small.size, Image.fromarray(filled.clip(0, 255).astype(np.uint8))


def level_angle(mask):
    ys, xs = np.nonzero(mask)
    xs = xs - xs.mean()
    ys = ys - ys.mean()
    cov = np.cov(np.stack([xs, ys]))
    vals, vecs = np.linalg.eigh(cov)
    major = vecs[:, np.argmax(vals)]
    return math.degrees(math.atan2(major[1], major[0]))


def column_profile(mask):
    h, w = mask.shape
    top = np.full(w, np.nan)
    bottom = np.full(w, np.nan)
    for x in range(w):
        col = np.nonzero(mask[:, x])[0]
        if len(col):
            top[x], bottom[x] = col.min(), col.max()
    return top, bottom


def body_mask(mask, length_px):
    """The body without its fins: a morphological opening with a disc a few percent of the length wide."""
    k = max(3, int(length_px * 0.045) | 1)
    im = Image.fromarray(mask.astype(np.uint8) * 255)
    opened = im.filter(ImageFilter.MinFilter(k)).filter(ImageFilter.MaxFilter(k))
    return np.asarray(opened) > 127


def smooth_outline(samples):
    """
    The bases of thick fins survive the opening as steps in the edge; a step makes a lip in the lofted body that
    catches the light as a bright line. A running median removes the short steps and a light blur rounds what is
    left, so the back and belly run smoothly (the cut-out card still shows the real edge).
    """
    n = len(samples)
    out = samples.copy()
    for k in range(2):
        col = samples[:, k]
        med = np.array([np.median(col[max(0, i - 4):i + 5]) for i in range(n)])
        pad = np.concatenate([np.full(6, med[0]), med, np.full(6, med[-1])])
        g = np.exp(-0.5 * (np.arange(-6, 7) / 2.5) ** 2)
        out[:, k] = np.convolve(pad, g / g.sum(), mode="valid")
    # Keep the snout and the tail stalk where they were measured, easing into the smoothed edge.
    w = np.clip(np.minimum(np.arange(n), np.arange(n)[::-1]) / 5.0, 0, 1)[:, None]
    return samples * (1 - w) + out * w


def head_on_left(mask):
    """The tail end has a thin stalk before the tail fin; the head end stays thick to the end."""
    top, bottom = column_profile(mask)
    thick = np.nan_to_num(bottom - top)
    w = len(thick)
    left = thick[int(w * 0.08):int(w * 0.3)]
    right = thick[int(w * 0.7):int(w * 0.92)]
    return left.min(initial=1e9) >= right.min(initial=1e9)


def bleed(rgba, steps=32, solid=235):
    """
    Replaces the colour of the see-through and edge pixels with the fish's own colours spread outwards, so texture
    filtering never pulls in the old background (no white halo, no black rim at the snout).
    """
    arr = np.asarray(rgba).astype(np.float32)
    rgb, a = arr[..., :3].copy(), arr[..., 3]
    known = a > solid
    for _ in range(steps):
        acc = np.zeros_like(rgb)
        cnt = np.zeros(a.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            shifted_k = np.roll(np.roll(known, dy, 0), dx, 1)
            shifted_c = np.roll(np.roll(rgb, dy, 0), dx, 1)
            acc += shifted_c * shifted_k[..., None]
            cnt += shifted_k
        grow = (~known) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        known = known | grow
        if known.all():
            break
    # Anything still unknown far from the fish: the mean fish colour.
    if (~known).any():
        rgb[~known] = rgb[known].mean(axis=0)
    out = np.dstack([rgb, a]).clip(0, 255).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def main():
    src, out_dir, sid = sys.argv[1], sys.argv[2], sys.argv[3]
    flip = "--flip" in sys.argv
    threshold = float(sys.argv[sys.argv.index("--threshold") + 1]) if "--threshold" in sys.argv else 38.0
    img = Image.open(src).convert("RGB")
    box = None
    if "--box" in sys.argv:
        box = tuple(float(v) for v in sys.argv[sys.argv.index("--box") + 1].split(","))
    soft = None
    if "--isnet" in sys.argv:
        opening = int(sys.argv[sys.argv.index("--open") + 1]) if "--open" in sys.argv else 5
        mask, size, soft = segment_isnet(img, sys.argv[sys.argv.index("--isnet") + 1], box, opening)
    elif "--grabcut" in sys.argv:
        mask, size = segment_grabcut(img, box)
    else:
        mask, size = segment(img, threshold)

    # Level the fish along its long axis.
    angle = level_angle(mask)
    if angle > 90:
        angle -= 180
    if angle < -90:
        angle += 180
    m_img = soft or Image.fromarray(mask.astype(np.uint8) * 255).resize(img.size, Image.BILINEAR)
    rgba = img.convert("RGBA")
    rgba.putalpha(m_img)
    rgba = rgba.rotate(angle, resample=Image.BICUBIC, expand=True)
    alpha = np.asarray(rgba.split()[3]) > 127
    if not alpha.any():
        raise SystemExit("no fish found")
    ys, xs = np.nonzero(alpha)
    pad = 4
    box = (max(xs.min() - pad, 0), max(ys.min() - pad, 0), min(xs.max() + pad, rgba.width), min(ys.max() + pad, rgba.height))
    rgba = rgba.crop(box)
    alpha = np.asarray(rgba.split()[3]) > 127
    if not head_on_left(alpha) ^ flip:
        rgba = rgba.transpose(Image.FLIP_LEFT_RIGHT)
        alpha = alpha[:, ::-1]

    # Soft, slightly eroded edge so no background halo is left around the fish (the model's own soft edge when it
    # has one).
    if soft is None:
        a = Image.fromarray(alpha.astype(np.uint8) * 255).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(1.2))
        rgba.putalpha(a)
    h = int(round(rgba.height * OUT_WIDTH / rgba.width))
    rgba = rgba.resize((OUT_WIDTH, h), Image.LANCZOS)
    # A soft model edge keeps the real colour of see-through fins; only the faint fringe is replaced.
    rgba = bleed(rgba, solid=235 if soft is None else 96)
    rgba.save(f"{out_dir}/{sid}.webp", quality=92, alpha_quality=100, method=6)

    # Body outline (fins removed) at 128 points, in image fractions.
    small = rgba.copy()
    small.thumbnail((512, 512))
    a_small = np.asarray(small.split()[3]) > 127
    body = body_mask(a_small, a_small.shape[1])
    top, bottom = column_profile(body)
    w = len(top)
    xs = np.nonzero(~np.isnan(top))[0]
    # A broad, dark tail fin can survive the opening: the body then ends at the tail stalk, the narrowest point of
    # its back third, not where the mask ends.
    thick = np.convolve(np.nan_to_num(bottom - top), np.ones(5) / 5, mode="same")
    lo, hi = int(xs.min() + (xs.max() - xs.min()) * 0.6), xs.max() - 2
    if hi > lo:
        seg = thick[lo:hi + 1]
        after = np.maximum.accumulate(seg[::-1])[::-1]      # the thickest point from here to the end
        ratio = seg / np.maximum(after, 1e-6)
        stalk = lo + int(np.argmin(ratio))
        if ratio[stalk - lo] < 0.8:
            xs = xs[xs <= stalk]
    start, end = xs.min() / w, (xs.max() + 1) / w        # where the body begins and ends (the tail fin is beyond)
    samples = []
    for i in range(128):
        x = start + (end - start) * i / 127
        col = min(int(x * w), w - 1)
        if np.isnan(top[col]):
            near = xs[np.argmin(np.abs(xs - col))]
            col = near
        samples.append([float(top[col]) / a_small.shape[0], float(bottom[col]) / a_small.shape[0]])
    samples = [[round(float(t), 4), round(float(b), 4)] for t, b in smooth_outline(np.array(samples))]
    meta = {"aspect": round(rgba.height / rgba.width, 4), "body_start": round(float(start), 4),
            "body_end": round(float(end), 4), "outline": samples}
    json.dump(meta, open(f"{out_dir}/{sid}.json", "w"), indent=0)
    print(f"{sid}: {rgba.width}x{rgba.height}, levelled {angle:.1f} deg, body {start:.2f}-{end:.2f}")


if __name__ == "__main__":
    main()
