"""Vectorised DXT1 / DXT5 (BC1 / BC3) encoder tuned for smooth, dark skin.

Pillow's DXT writer gives many 4x4 blocks a single flat 5:6:5 colour; on dark skin one 5-bit step is
~13% of the value, so neighbouring blocks show as visible squares in game. Here every block gets:
  * a principal-axis colour line, refined by least squares on its index weights (2 rounds)
  * for low-contrast blocks, endpoints that straddle the mean (one 5:6:5 step below / above per
    channel), so the 1/3 and 2/3 interpolants reproduce in-between shades
  * the best of those candidates by squared error
Alpha (DXT5): min/max endpoints, 8-value interpolation.
"""
import numpy as np

W565 = np.array([31.0, 63.0, 31.0])


def _blocks(img):
    h, w = img.shape[:2]
    H, Wd = (h + 3) // 4 * 4, (w + 3) // 4 * 4
    if (H, Wd) != (h, w):
        img = np.pad(img, ((0, H - h), (0, Wd - w), (0, 0)), mode='edge')
    return img.reshape(H // 4, 4, Wd // 4, 4, -1).transpose(0, 2, 1, 3, 4).reshape(-1, 16, img.shape[2])


def _q(c):          # float RGB 0..255 -> integer 565 components
    return np.clip(np.round(c / 255.0 * W565), 0, W565)


def _deq(q):        # 565 components -> 0..255 floats (as the GPU expands them)
    r = q[..., 0]; g = q[..., 1]; b = q[..., 2]
    return np.stack([(r * 8 + np.floor(r / 4)), (g * 4 + np.floor(g / 16)), (b * 8 + np.floor(b / 4))], -1)


def _pack(q):
    return (q[..., 0].astype(np.uint32) << 11) | (q[..., 1].astype(np.uint32) << 5) | q[..., 2].astype(np.uint32)


def _palette(e0, e1):
    a, b = _deq(e0), _deq(e1)
    return np.stack([a, b, (2 * a + b) / 3, (a + 2 * b) / 3], 1)     # (n, 4, 3)


def _assign(px, pal):
    d = ((px[:, :, None, :] - pal[:, None, :, :]) ** 2).sum(-1)       # (n, 16, 4)
    idx = d.argmin(-1)
    return idx, np.take_along_axis(d, idx[..., None], -1)[..., 0].sum(1)


TW = np.array([0.0, 1.0, 1 / 3, 2 / 3])            # weight of e1 for indices 0..3


def _lsq(px, idx):
    t = TW[idx]; a = 1 - t                           # px ~ a*e0 + t*e1
    aa, at, tt = (a * a).sum(1), (a * t).sum(1), (t * t).sum(1)
    ap, tp = (a[..., None] * px).sum(1), (t[..., None] * px).sum(1)
    det = aa * tt - at * at
    ok = np.abs(det) > 1e-6
    det = np.where(ok, det, 1)
    e0 = (tt[:, None] * ap - at[:, None] * tp) / det[:, None]
    e1 = (aa[:, None] * tp - at[:, None] * ap) / det[:, None]
    return np.clip(e0, 0, 255), np.clip(e1, 0, 255), ok


def encode_colour(px):
    """px: (n, 16, 3) float. returns (n, 8) uint8 BC1 colour blocks (4-colour mode)."""
    n = len(px)
    mean = px.mean(1)
    cen = px - mean[:, None]
    cov = np.einsum('nki,nkj->nij', cen, cen)
    v = np.ones((n, 3)) / np.sqrt(3)
    for _ in range(8):
        v = np.einsum('nij,nj->ni', cov, v); v /= np.linalg.norm(v, axis=1, keepdims=True) + 1e-12
    proj = (cen * v[:, None]).sum(-1)
    lo = mean + v * proj.min(1)[:, None]; hi = mean + v * proj.max(1)[:, None]
    cands = [(_q(hi), _q(lo))]
    e0q, e1q = cands[0]
    for _ in range(2):                                # least-squares refinement on the index weights
        idx, _ = _assign(px, _palette(e0q, e1q))
        a, b, ok = _lsq(px, idx)
        e0q = np.where(ok[:, None], _q(a), e0q); e1q = np.where(ok[:, None], _q(b), e1q)
        cands.append((e0q, e1q))
    step = 255.0 / W565                               # straddle the mean: one step down / up per channel
    fl = np.clip(np.floor(mean / 255.0 * W565), 0, W565); ce = np.clip(fl + 1, 0, W565)
    cands.append((ce, fl)); cands.append((_q(mean + step / 2), _q(mean - step / 2)))
    best_e0, best_e1 = cands[0]; best_idx, best_err = _assign(px, _palette(*cands[0]))
    for e0, e1 in cands[1:]:
        idx, err = _assign(px, _palette(e0, e1))
        m = err < best_err
        best_e0 = np.where(m[:, None], e0, best_e0); best_e1 = np.where(m[:, None], e1, best_e1)
        best_idx = np.where(m[:, None], idx, best_idx); best_err = np.where(m, err, best_err)
    c0, c1 = _pack(best_e0), _pack(best_e1)
    # BC1 4-colour mode needs c0 > c1: swap endpoints (and indices 0<->1, 2<->3) where needed
    sw = c0 < c1
    c0, c1 = np.where(sw, c1, c0), np.where(sw, c0, c1)
    best_idx = np.where(sw[:, None], best_idx ^ 1, best_idx)
    eq = c0 == c1                                     # flat block: all index 0
    best_idx[eq] = 0
    bits = (best_idx.astype(np.uint32) << (2 * np.arange(16, dtype=np.uint32))).sum(1).astype(np.uint32)
    out = np.zeros((n, 8), np.uint8)
    out[:, 0:2] = c0.astype('<u2').view(np.uint8).reshape(n, 2)
    out[:, 2:4] = c1.astype('<u2').view(np.uint8).reshape(n, 2)
    out[:, 4:8] = bits.astype('<u4').view(np.uint8).reshape(n, 4)
    return out


def encode_alpha(al):
    """al: (n, 16) float 0..255 -> (n, 8) uint8 BC3 alpha blocks."""
    n = len(al)
    a0 = np.clip(np.round(al.max(1)), 0, 255); a1 = np.clip(np.round(al.min(1)), 0, 255)
    pal = np.stack([a0, a1] + [((7 - k) * a0 + k * a1) / 7 for k in range(1, 7)], 1)   # a0 > a1 mode
    idx = np.abs(al[:, :, None] - pal[:, None, :]).argmin(-1)
    idx[a0 == a1] = 0
    bits = (idx.astype(np.uint64) << (3 * np.arange(16, dtype=np.uint64))).sum(1).astype(np.uint64)
    out = np.zeros((n, 8), np.uint8)
    out[:, 0] = a0; out[:, 1] = a1
    b = bits.astype('<u8').view(np.uint8).reshape(n, 8)
    out[:, 2:8] = b[:, 0:6]
    return out


def encode(fmt, rgba):
    """rgba: (h, w, 4) uint8/float array -> raw block bytes (standard order).
    For ARGB (D3D A8R8G8B8) the output is uncompressed pixels in B,G,R,A byte order."""
    if fmt == 'ARGB':
        a = np.clip(np.round(np.asarray(rgba, np.float64)), 0, 255).astype(np.uint8)
        if a.shape[2] == 3:
            a = np.concatenate([a, np.full(a.shape[:2] + (1,), 255, np.uint8)], 2)
        return a[..., [2, 1, 0, 3]].tobytes()      # RGBA -> BGRA
    a = np.asarray(rgba, np.float64)
    B = _blocks(a)
    col = encode_colour(B[:, :, :3])
    if fmt == 'DXT1':
        return col.tobytes()
    return np.concatenate([encode_alpha(B[:, :, 3]), col], 1).tobytes()


def encode_chain(fmt, im):
    """PIL RGBA image -> list of level bytes, full chain down to 1x1 (box-filtered)."""
    from PIL import Image
    levels = []; cur = im
    while True:
        levels.append(encode(fmt, np.asarray(cur.convert('RGBA'))))
        if cur.size[0] == 1:
            break
        cur = cur.resize((max(1, cur.size[0] // 2), max(1, cur.size[1] // 2)), Image.BOX)
    return levels
