"""Minimal DDS wrap/unwrap so Pillow can decode/encode raw DXT1/DXT5 block data."""
import io, struct
from PIL import Image


def wrap(fmt, w, h, data, mips=1):
    hdr = bytearray(128)
    hdr[0:4] = b'DDS '
    struct.pack_into('<7I', hdr, 4, 124, 0x1 | 0x2 | 0x4 | 0x1000 | 0x80000 | (0x20000 if mips > 1 else 0),
                     h, w, len(data), 0, mips)
    struct.pack_into('<II4s', hdr, 76, 32, 0x4, fmt.encode())
    struct.pack_into('<I', hdr, 108, 0x1000 | (0x400008 if mips > 1 else 0))
    return bytes(hdr) + data


def decode(fmt, w, h, data):
    bs = 8 if fmt == 'DXT1' else 16
    size = max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * bs
    im = Image.open(io.BytesIO(wrap(fmt, w, h, bytes(data[:size]))))
    im.load()
    return im.convert('RGBA')


def encode(fmt, im):
    """Encode one level with Pillow's DDS writer; returns raw block bytes."""
    buf = io.BytesIO()
    im.save(buf, 'DDS', pixel_format=fmt)
    b = buf.getvalue()
    off = 128 + (20 if b[84:88] == b'DX10' else 0)
    return b[off:]


def encode_chain(fmt, im, nbytes):
    """Encode a full mip chain (box-downsampled) until nbytes are produced."""
    out = bytearray()
    cur = im
    while len(out) < nbytes:
        out += encode(fmt, cur)
        if cur.size == (1, 1):
            break
        cur = cur.resize((max(1, cur.size[0] // 2), max(1, cur.size[1] // 2)), Image.BOX)
    return bytes(out[:nbytes])
