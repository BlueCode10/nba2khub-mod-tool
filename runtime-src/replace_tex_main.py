"""Replace one texture in a 2K14 cyberface .iff with an image, growing the slot if the image is bigger.

Used by the 3DM MOD Tool's "Replace" so it behaves like RED MC: importing a 1024 image into a 512 slot
makes the slot 1024. The tool identifies the texture it selected by (format, current size, ordinal) --
the Nth texture of that format+size in file order -- which matches the tool's own order for the
standard face/chest/normal/occlusion/hair textures.

    python replace_tex.py <in.iff> <out.iff> <fmt> <cur_size> <ordinal> <image> [target_size]
      fmt         DXT1 | DXT5 | ARGB
      cur_size    the slot's current square size (e.g. 512)
      ordinal     0-based index among textures of that fmt+size, in file order
      image       the replacement image (png/bmp/jpg/dds)
      target_size square size to store it at (default = the image's width)

Nothing is changed in place: the result is written to <out.iff>. Only the one texture is resized;
every other texture keeps its pixels.
"""
import os, sys

from PIL import Image
import layout, dds, dxt
from iff2k import Iff


def load_image(path):
    ext = os.path.splitext(path)[1].lower()
    if ext == '.dds':
        d = open(path, 'rb').read()
        import struct
        h = struct.unpack_from('<I', d, 12)[0]; w = struct.unpack_from('<I', d, 16)[0]
        fourcc = d[84:88]
        fmt = 'DXT1' if fourcc == b'DXT1' else 'DXT5' if fourcc == b'DXT5' else 'ARGB'
        return dds.decode(fmt, w, h, d[128:]).convert('RGBA')
    return Image.open(path).convert('RGBA')


def replace(src, out, fmt, cur_size, ordinal, image_path, target_size=None, log=print):
    f = Iff(src)
    texs = sorted(layout.textures(f), key=lambda t: t['o1'])
    same = [t for t in texs if t['fmt'] == fmt and t['w'] == cur_size]
    if ordinal >= len(same):
        raise SystemExit('this texture is not one that can be resized here '
                         '(no #%d %s %d texture; the file has %d).' % (ordinal + 1, fmt, cur_size, len(same)))
    tex = same[ordinal]
    name = tex['name']

    img = load_image(image_path)
    size = int(target_size) if target_size else img.width
    img = img.resize((size, size), Image.LANCZOS)

    if size != tex['w']:
        layout.free_layout(f, {name: size})
        log('grew %s  %d -> %d' % (name, tex['w'], size))
    else:
        # same size: still re-encode into the current layout
        if not layout.is_free(f):
            layout.free_layout(f, {})   # normalise to block-table-free so writes always fit
    layout.write_levels(f, name, fmt, dxt.encode_chain(fmt, img))
    open(out, 'wb').write(f.rebuild())
    log('replaced %s (%s %d) from %s' % (name, fmt, size, os.path.basename(image_path)))
    return name


if __name__ == '__main__':
    if len(sys.argv) < 7:
        print(__doc__); raise SystemExit(2)
    src, out, fmt, cur, ordn, image = sys.argv[1:7]
    target = sys.argv[7] if len(sys.argv) > 7 else None
    replace(src, out, fmt, int(cur), int(ordn), image, target)
