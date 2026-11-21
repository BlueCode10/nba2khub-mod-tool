"""Minimal reader for NBA 2K14 PC .iff archives (as seen in global.iff).

Layout: header + TOC of records [crc32(name), type, n, n x chunk-offset],
then n ZLIB chunks ('ZLIB', BE ulen, BE clen, 4 pad, zlib data), then a name table.
Texture records (type 0x5C369069): chunk0 = header, chunk1 = pixel data.
"""
import struct, zlib, re

TEX = 0x5C369069


class Iff:
    def __init__(self, path):
        self.path = path
        self.d = bytearray(open(path, 'rb').read())
        self.chunk_pos = [m.start() for m in re.finditer(rb'ZLIB', self.d)
                          if struct.unpack_from('>I', self.d, m.start() + 4)[0] > 0x1000]
        # keep only chained chunks starting at first one
        pos, chain = self.chunk_pos[0], []
        while self.d[pos:pos + 4] == b'ZLIB':
            ulen, clen = struct.unpack_from('>II', self.d, pos + 4)
            chain.append((pos, ulen, clen))
            pos += clen
        self.chain = chain
        self.tail_pos = pos
        self.chunks = [bytearray(zlib.decompress(self.d[p + 16:p + c])) for p, u, c in chain]
        self.toc_start, self.toc_end = 0x20, chain[0][0]
        tail = self.d[self.tail_pos:]
        names = set(m.group().decode('utf-16le') for m in re.finditer(rb'(?:[\x20-\x7e]\x00){3,}', tail))
        self.names = {zlib.crc32(n.encode()) & 0xFFFFFFFF: n for n in names}

    def record(self, name):
        h = struct.pack('<I', zlib.crc32(name.encode()) & 0xFFFFFFFF)
        toc = self.d[self.toc_start:self.toc_end]
        i = toc.find(h)
        while i >= 0:
            t, n = struct.unpack_from('<II', toc, i + 4)
            if 1 <= n <= 3:
                return t, list(struct.unpack_from('<%dI' % n, toc, i + 12))
            i = toc.find(h, i + 1)
        raise KeyError(name)

    def texture(self, name):
        t, (o0, o1) = self.record(name)
        assert t == TEX, hex(t)
        hb = self.chunks[0][o0:o0 + 0xC0]
        fmt = bytes(hb[8:12]).decode()
        w, h = struct.unpack_from('<HH', hb, 0x68)
        bs = 8 if fmt == 'DXT1' else 16
        size = max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * bs
        return fmt, w, h, o1, size

    def rebuild(self):
        """Recompress chunks; patch chunk descriptors (LE offset, clen) and BE tail offset at 0x8."""
        first = self.chain[0][0]
        head = bytearray(self.d[:first])
        body = bytearray()
        pos = first
        for (p, u, c), data in zip(self.chain, self.chunks):
            comp = zlib.compress(bytes(data), 9)
            clen = len(comp) + 16
            desc = head.find(struct.pack('<II', p, c), 0x10, first)
            assert desc > 0, 'chunk descriptor not found'
            struct.pack_into('<II', head, desc, pos, clen)
            body += b'ZLIB' + struct.pack('>II', len(data), clen) + b'\0\0\0\0' + comp
            pos += clen
        assert struct.unpack_from('>I', head, 8)[0] == self.tail_pos
        struct.pack_into('>I', head, 8, pos)
        return head + body + self.d[self.tail_pos:]


def _c565(c):
    r, g, b = (c >> 11) & 31, (c >> 5) & 63, c & 31
    return (r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2)


def decode_dxt1(data, w, h):
    px = bytearray(w * h * 4)
    bw = (w + 3) // 4
    for by in range((h + 3) // 4):
        for bx in range(bw):
            o = (by * bw + bx) * 8
            c0, c1, bits = struct.unpack_from('<HHI', data, o)
            a, b = _c565(c0), _c565(c1)
            if c0 > c1:
                pal = [a + (255,), b + (255,),
                       tuple((2 * x + y) // 3 for x, y in zip(a, b)) + (255,),
                       tuple((x + 2 * y) // 3 for x, y in zip(a, b)) + (255,)]
            else:
                pal = [a + (255,), b + (255,), tuple((x + y) // 2 for x, y in zip(a, b)) + (255,), (0, 0, 0, 0)]
            for j in range(16):
                x, y = bx * 4 + (j & 3), by * 4 + (j >> 2)
                if x < w and y < h:
                    i = (y * w + x) * 4
                    px[i:i + 4] = bytes(pal[(bits >> (2 * j)) & 3])
    return px


def write_png(path, px, w, h):
    raw = b''.join(b'\0' + bytes(px[y * w * 4:(y + 1) * w * 4]) for y in range(h))
    def ch(t, b):
        return struct.pack('>I', len(b)) + t + b + struct.pack('>I', zlib.crc32(t + b) & 0xFFFFFFFF)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + ch(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
                           + ch(b'IDAT', zlib.compress(raw, 9)) + ch(b'IEND', b''))
