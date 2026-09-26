"""
Turns a street panorama (an equirectangular HDR, e.g. Poly Haven's CC0 "Pretville Street") into the pictures the
street scene is built from, for each light of the day:

  sky_<light>.jpg    the whole panorama, tone-mapped: drawn on the dome
  floor_<light>.jpg  the ground seen from above, a square of 2*FLOOR metres around the spot the photo was taken from:
                     drawn on the floor the cars stand on, so their shadows fall on it

Both are projections from the same point, CAPTURE_HEIGHT metres above the ground where the camera stood, so the
floor and the dome meet without a seam. StreetSceneBuilder uses the same mapping for the dome's UVs:

  direction d (x, y, z), y up  ->  u = 0.5 - atan2(d.x, d.z) / (2 pi),  v = 0.5 - asin(d.y) / pi

Also writes light.json: where the sun is in the panorama, for the renderer's key light.

  python prepare_street.py <panorama.hdr> <out folder> [capture height m] [floor half-size m]

Needs numpy and opencv-python.
"""
import json
import math
import os
import sys

import cv2
import numpy as np

SKY_SIZE = (4096, 2048)
FLOOR_SIZE = 4096

# Middle grey of the day picture, before the curve: the panorama's median lands here
DAY_MIDDLE = 0.22


def tone(linear, exposure):
    """A soft shoulder, then gamma: highlights roll off instead of clipping"""
    x = np.maximum(linear * exposure, 0)
    mapped = 1 - np.exp(-x)
    return np.clip(mapped, 0, 1) ** (1 / 2.2)


def directions_floor(size, half, height):
    """World direction from the camera to each pixel of the floor square; row 0 is +z, column 0 is -x"""
    coords = (np.arange(size) + 0.5) / size * 2 * half - half
    x = coords[None, :].repeat(size, 0)
    z = -coords[:, None].repeat(size, 1)
    y = np.full_like(x, -height)
    return x, y, z


def to_uv(x, y, z):
    length = np.sqrt(x * x + y * y + z * z)
    u = 0.5 - np.arctan2(x, z) / (2 * np.pi)
    v = 0.5 - np.arcsin(np.clip(y / length, -1, 1)) / np.pi
    return u % 1.0, v


def sample(pano, u, v):
    h, w = pano.shape[:2]
    # One column wrapped round, so the seam behind the camera blends like the rest
    wrapped = np.concatenate([pano, pano[:, :1]], axis=1)
    map_x = (u * w - 0.5).astype(np.float32)
    map_y = (v * h - 0.5).astype(np.float32)
    return cv2.remap(wrapped, map_x, map_y, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)


def sky_mask(pano):
    """1 where the panorama is sky (above the horizon, bright and blue or blazing), feathered"""
    h = pano.shape[0]
    b, g, r = pano[..., 0], pano[..., 1], pano[..., 2]
    lum = 0.0722 * b + 0.7152 * g + 0.2126 * r
    median = np.median(lum)
    rows = np.arange(h)[:, None]
    above = rows < h * 0.5
    sky = above & (((b > r * 1.12) & (lum > median * 2.0)) | (lum > median * 12))
    mask = sky.astype(np.float32)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    return cv2.GaussianBlur(mask, (0, 0), h / 1600)


def sky_gradient(h, w, top, horizon):
    """A plain sky, top colour overhead to horizon colour at the horizon (BGR, linear)"""
    t = np.clip((np.arange(h) / (h * 0.5)), 0, 1)[:, None, None] ** 1.6
    top = np.array(top, np.float32)[None, None, :]
    horizon = np.array(horizon, np.float32)[None, None, :]
    return np.broadcast_to(top * (1 - t) + horizon * t, (h, w, 3)).astype(np.float32)


def grade(pano, mask, light, middle):
    """The day as shot; dusk and night made from it: dimmer, tinted, the sky swapped for one of that hour"""
    if light == "day":
        return pano
    h, w = pano.shape[:2]
    m = mask[..., None]
    if light == "dusk":
        # BGR: warm light, a sky going orange at the horizon
        lit = pano * np.array([0.55, 0.78, 1.05], np.float32) * 0.42
        sky = sky_gradient(h, w, [0.55, 0.28, 0.22], [0.35, 0.75, 1.6]) * middle * 6
        return lit * (1 - m) + sky * m
    # night: the street in blue moonlight, a deep sky
    lit = pano * np.array([1.25, 0.8, 0.55], np.float32) * 0.09
    sky = sky_gradient(h, w, [0.05, 0.02, 0.01], [0.28, 0.16, 0.11]) * middle * 1.1
    return lit * (1 - m) + sky * m


def sun_direction(pano):
    lum = cv2.GaussianBlur(pano.mean(axis=2), (0, 0), 8)
    y, x = np.unravel_index(np.argmax(lum), lum.shape)
    h, w = lum.shape
    u, v = (x + 0.5) / w, (y + 0.5) / h
    azimuth = (0.5 - u) * 2 * math.pi
    elevation = (0.5 - v) * math.pi
    return [math.sin(azimuth) * math.cos(elevation), math.sin(elevation), math.cos(azimuth) * math.cos(elevation)]


def main():
    source, out = sys.argv[1], sys.argv[2]
    height = float(sys.argv[3]) if len(sys.argv) > 3 else 1.7
    half = float(sys.argv[4]) if len(sys.argv) > 4 else 20.0
    os.makedirs(out, exist_ok=True)

    pano = cv2.imread(source, cv2.IMREAD_UNCHANGED).astype(np.float32)
    lum = pano.mean(axis=2)
    exposure = -math.log(1 - DAY_MIDDLE ** 2.2) / float(np.median(lum))
    mask = sky_mask(pano)
    middle = float(np.median(lum))

    fx, fy, fz = directions_floor(FLOOR_SIZE, half, height)
    fu, fv = to_uv(fx, fy, fz)

    for light in ("day", "dusk", "night"):
        graded = grade(pano, mask, light, middle)
        sky = cv2.resize(graded, SKY_SIZE, interpolation=cv2.INTER_AREA)
        cv2.imwrite(os.path.join(out, f"sky_{light}.jpg"), (tone(sky, exposure) * 255 + 0.5).astype(np.uint8),
                    [cv2.IMWRITE_JPEG_QUALITY, 90])
        floor = sample(graded, fu, fv)
        cv2.imwrite(os.path.join(out, f"floor_{light}.jpg"), (tone(floor, exposure) * 255 + 0.5).astype(np.uint8),
                    [cv2.IMWRITE_JPEG_QUALITY, 90])
        print(f"{light}: done")

    with open(os.path.join(out, "light.json"), "w") as f:
        json.dump({"captureHeight": height, "floorHalfSize": half, "sun": sun_direction(pano)}, f, indent=2)


if __name__ == "__main__":
    main()
