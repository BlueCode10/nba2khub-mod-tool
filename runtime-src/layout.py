"""Physical layout of textures in a 2K14 PC png####.iff (chunk1).

The texture records describe each texture as one contiguous mip chain (their chunk1 offsets are spaced
that way), but the bytes are actually stored like this, starting at the first texture's offset:

  1. every texture's small levels (32x32 and below, down to 1x1), packed one texture after another
  2. the larger levels grouped by mip index, smallest group first (e.g. all 64s, then all 128s, ...);
     inside a group, textures follow record order
  3. no padding anywhere; blocks are standard DXT1/DXT5 (A8R8G8B8 for format 0x15)

Verified on png0005, png1183, png1307 and png1609: the model ends exactly at the end of chunk1 and every
level matches its downsampled top level.
"""
import struct
import numpy as np

TEX = 0x5C369069
TAIL_MAX = 32


def textures(f):
    """[{name, fmt, w, h, o1}] for every texture record, in chunk1 order."""
    toc = f.d[f.toc_start:f.toc_end]; out = []; i = 0
    while i + 20 <= len(toc):
        h, t, n = struct.unpack_from('<III', toc, i)
        if t == TEX and n == 2:
            o0, o1 = struct.unpack_from('<II', toc, i + 12)
            hb = f.chunks[0][o0:o0 + 0xC0]
            fourcc = bytes(hb[8:12]); w, hh = struct.unpack_from('<HH', hb, 0x68)
            if fourcc in (b'DXT1', b'DXT5'):
                fmt = fourcc.decode()
            elif fourcc[0] == 0x15:
                fmt = 'ARGB'
            else:
                raise ValueError('unknown texture format %r in %s' % (fourcc, f.names.get(h, hex(h))))
            if w != hh:
                raise ValueError('non-square texture %s %dx%d' % (f.names.get(h, hex(h)), w, hh))
            out.append(dict(name=f.names.get(h, hex(h)), fmt=fmt, w=w, h=hh, o1=o1)); i += 20
        else:
            i += 4
    out.sort(key=lambda t: t['o1'])
    return out


def level_size(fmt, d):
    if fmt == 'ARGB':
        return d * d * 4
    return max(1, d // 4) ** 2 * (8 if fmt == 'DXT1' else 16)


def positions(f):
    """{(texture name, level dimension): chunk1 offset}; raises if the model doesn't fit this file."""
    texs = textures(f)
    if not texs:
        raise ValueError('no textures found')
    pos = {}
    if is_free(f):
        # no VRAM block table (RED MC style, see free_layout): each texture is one standard mip chain at its o1
        for t in texs:
            off = t['o1']; d = t['w']
            while d >= 1:
                pos[(t['name'], d)] = off; off += level_size(t['fmt'], d); d //= 2
            if off > len(f.chunks[1]):
                raise ValueError('texture %s runs past the end of the chunk' % t['name'])
        return pos
    off = texs[0]['o1']
    for t in texs:
        d = min(t['w'], TAIL_MAX)
        while d >= 1:
            pos[(t['name'], d)] = off; off += level_size(t['fmt'], d); d //= 2
    nlev = max(int(np.log2(t['w'])) for t in texs) + 1
    for L in range(nlev - 1, -1, -1):
        for t in texs:
            d = t['w'] >> L
            if d > TAIL_MAX:
                pos[(t['name'], d)] = off; off += level_size(t['fmt'], d)
    # textures end the chunk; a transplanted mesh's buffers may be appended after them (see meshio.py)
    tail_ok = off < len(f.chunks[1]) and not any(f.chunks[1][off:min(len(f.chunks[1]), off + 16)])
    if off != len(f.chunks[1]) and not tail_ok:
        raise ValueError('texture layout model does not fit this file (ends %#x, chunk is %#x)' % (off, len(f.chunks[1])))
    return pos


def write_levels(f, name, fmt, levels):
    """levels: list of raw standard-order level bytes, largest first (full chain down to 1x1)."""
    pos = positions(f)
    if fmt == 'ARGB':
        d = int(round(np.sqrt(len(levels[0]) // 4)))
    else:
        d = int(round(np.sqrt(len(levels[0]) // (16 if fmt == 'DXT5' else 8)))) * 4
    for data in levels:
        o = pos[(name, d)]
        assert len(data) == level_size(fmt, d), (name, d, len(data))
        f.chunks[1][o:o + len(data)] = data
        d //= 2


def read_level(f, name, fmt, d):
    o = positions(f)[(name, d)]
    return bytes(f.chunks[1][o:o + level_size(fmt, d)])


# ---------------------------------------------------------------- resizing textures
# Each texture's header (chunk0, 0xC0 bytes at the record's o0) holds its size (u16 w, h at 0x68) and its mip count
# (bits 7.. of the u16 at 0x66). The header VRAM block table (see meshio.vram_blocks) lists, after the mesh block,
# every texture's tail (levels <= 32, flag 0x9f) in record order, then every larger level grouped by its index
# from the top (flag 0x80 + index), smallest group first; an entry is (size / 4 << 12 | flag, virtual offset), where
# virtual offsets lay each texture out as one contiguous chain (16-byte aligned starts, record order) and header
# 0x4C is the end of the last chain. The bytes follow the same order with no padding (positions() above).

def _chain(fmt, w):
    """[(level index, dim, bytes)] largest first, tail levels merged into one ('tail', 0, bytes) entry."""
    out = []; d = w; L = 0; tail = 0
    while d >= 1:
        if d > TAIL_MAX:
            out.append((L, d, level_size(fmt, d)))
        else:
            tail += level_size(fmt, d)
        d //= 2; L += 1
    return out, tail


def relayout(f, sizes):
    """Give the named textures new square sizes ({name: w}). Other textures keep their pixels; resized ones are
    zeroed (write them afterwards). The VRAM block table is rebuilt; its entry count cannot change (the header has
    no room for more entries), so trade levels between textures to keep it equal."""
    import meshio
    blk = meshio.vram_blocks(f)
    texs = textures(f)
    old = {(t['name'], d): bytes(f.chunks[1][o:o + level_size(t['fmt'], d)]) for (t2, d), o in positions(f).items()
           for t in texs if t['name'] == t2}
    # chunk0 headers / record fields, by name
    recs = {}
    toc = f.d; i = f.toc_start
    while i + 20 <= f.toc_end:
        h, ty, n = struct.unpack_from('<III', toc, i)
        if ty == TEX and n == 2:
            recs[f.names.get(h, hex(h))] = i; i += 20
        else:
            i += 4
    for t in texs:
        w = sizes.get(t['name'], t['w'])
        o0 = struct.unpack_from('<I', f.d, recs[t['name']] + 12)[0]
        nm = int(np.log2(w)) + 1
        m = struct.unpack_from('<H', f.chunks[0], o0 + 0x66)[0]
        struct.pack_into('<H', f.chunks[0], o0 + 0x66, (m & 0x7F) | (nm << 7))
        struct.pack_into('<HH', f.chunks[0], o0 + 0x68, w, w)
        t['new_w'] = w
    # virtual chains
    v = texs[0]['o1']; tails = []; levels = []
    for t in texs:
        v = (v + 15) & ~15; t['new_o1'] = v
        big, tail = _chain(t['fmt'], t['new_w'])
        o = v
        for L, d, n in big:
            levels.append((L, o, n)); o += n
        tails.append((o, tail)); o += tail
        v = o
        struct.pack_into('<I', f.d, recs[t['name']] + 16, t['new_o1'])
    struct.pack_into('<I', f.d, 0x4C, v)
    ents = [(tail, 0x9F, o) for o, tail in tails]
    for L in range(max(L for L, _, _ in levels), -1, -1):
        ents += [(n, 0x80 + L, o) for L2, o, n in levels if L2 == L]
    if len(ents) + 1 != len(blk['entries']):
        raise ValueError('texture sizes need %d VRAM blocks, the header has room for %d' % (len(ents) + 1, len(blk['entries'])))
    for e, (n, flag, o) in zip(blk['entries'][1:], ents):
        assert n % 4 == 0 and n // 4 < (1 << 20), 'block too large'
        struct.pack_into('<II', f.d, e, (n // 4) << 12 | flag, o)
    # physical bytes
    start = texs[0]['o1']; mesh_part = bytes(f.chunks[1][:blk['mesh_end']])
    total = sum(n for n, _, _ in ents)
    f.chunks[1][:] = mesh_part + bytes(total)
    for t in texs:
        t['w'] = t['new_w']
    pos = positions(f)
    for (name, d), o in pos.items():
        if name not in sizes and (name, d) in old:
            f.chunks[1][o:o + len(old[(name, d)])] = old[(name, d)]
    return pos


# ---------------------------------------------------------------- no VRAM block table (RED MC style)
# The header record 0x411536D5 describes chunk1: [hash, hash, align, ulen (= header 0x4C), type, file offset,
# compressed length, ...]. Stock files use type 5 followed by [5, count, (code, offset) x count] (the VRAM block
# table above, at most 24 entries, so every texture that grows needs another to shrink). Files saved by RED MC
# (e.g. Sample Cyberfaces/LeBron James/png1013.iff: face_color, face_normal and chest_color 2048, works in game)
# use type 6 followed by a single 0, like chunk0's record: no block table, every texture stored as one standard
# mip chain at its record offset (16-byte aligned starts), no size limit.
C1_REC = 0x411536D5


def _c1_rec(f):
    i = f.d.find(struct.pack('<II', C1_REC, C1_REC), 0x10, f.chain[0][0])
    if i < 0:
        raise ValueError('chunk1 record not found')
    return i


def is_free(f):
    return struct.unpack_from('<I', f.d, _c1_rec(f) + 16)[0] == 6


def free_layout(f, sizes):
    """Drop the VRAM block table (type 5 -> 6 record, as RED MC writes) and store every texture as one contiguous
    mip chain after the mesh data, giving the named textures new square sizes ({name: w}; their pixels are zeroed,
    write them afterwards; the others keep theirs). The mesh part of chunk1 (before the first texture) is kept in
    place, so no mesh offset moves."""
    texs = textures(f)
    old_pos = positions(f)
    old = {k: bytes(f.chunks[1][o:o + level_size(next(t['fmt'] for t in texs if t['name'] == k[0]), k[1])])
           for k, o in old_pos.items()}
    mesh_end = texs[0]['o1']
    recs = {}
    i = f.toc_start
    while i + 20 <= f.toc_end:
        h, ty, n = struct.unpack_from('<III', f.d, i)
        if ty == TEX and n == 2:
            recs[f.names.get(h, hex(h))] = i; i += 20
        else:
            i += 4
    body = bytearray(f.chunks[1][:mesh_end])
    for t in texs:
        w = sizes.get(t['name'], t['w'])
        o0 = struct.unpack_from('<I', f.d, recs[t['name']] + 12)[0]
        m = struct.unpack_from('<H', f.chunks[0], o0 + 0x66)[0]
        struct.pack_into('<H', f.chunks[0], o0 + 0x66, (m & 0x7F) | ((int(np.log2(w)) + 1) << 7))
        struct.pack_into('<HH', f.chunks[0], o0 + 0x68, w, w)
        body += b'\0' * (((len(body) + 15) & ~15) - len(body))
        struct.pack_into('<I', f.d, recs[t['name']] + 16, len(body))
        d = w
        while d >= 1:
            n = level_size(t['fmt'], d)
            body += old[(t['name'], d)] if w == t['w'] else bytes(n)
            d //= 2
    f.chunks[1][:] = body
    # header: chunk1 record type 5 + [5, count, entries] -> type 6 + [0]
    r = _c1_rec(f)
    if struct.unpack_from('<I', f.d, r + 16)[0] == 5:
        count = struct.unpack_from('<I', f.d, r + 32)[0]
        cut0, cut1 = r + 32, r + 36 + 8 * count
        struct.pack_into('<I', f.d, r + 16, 6); struct.pack_into('<I', f.d, r + 28, 0)
        n = cut1 - cut0
        # self-relative pointer at 0x1C (to the record-pointer list after the table) and absolute file offsets
        p = struct.unpack_from('<I', f.d, 0x1C)[0]
        if 0x1C + (p & ~1) >= cut1:
            struct.pack_into('<I', f.d, 0x1C, p - n)
        del f.d[cut0:cut1]
        first = f.chain[0][0]
        for (pos, u, c) in f.chain:                      # chunk descriptors hold the chunks' file offsets
            k = f.d.find(struct.pack('<II', pos, c), 0x10, first - n)
            assert k > 0, 'chunk descriptor not found'
            struct.pack_into('<I', f.d, k, pos - n)
        f.chain = [(pos - n, u, c) for pos, u, c in f.chain]
        f.tail_pos -= n; f.toc_end -= n
        assert struct.unpack_from('<I', f.d, 0x04)[0] == first
        struct.pack_into('<I', f.d, 0x04, first - n)
        struct.pack_into('>I', f.d, 0x08, f.tail_pos)
    struct.pack_into('<I', f.d, 0x4C if r == 0x40 else r + 12, len(f.chunks[1]))
    return positions(f)
