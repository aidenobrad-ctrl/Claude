"""Renders: top-down overview map and isometric view of the voxel world."""
import numpy as np
from PIL import Image, ImageDraw, ImageFont

from .world import base_name

DYE = {
    'white': (233, 236, 236), 'orange': (240, 118, 19), 'magenta': (189, 68, 179), 'light_blue': (58, 175, 217),
    'yellow': (248, 197, 39), 'lime': (112, 185, 25), 'pink': (237, 141, 172), 'gray': (62, 68, 71),
    'light_gray': (142, 142, 134), 'cyan': (21, 137, 145), 'purple': (121, 42, 172), 'blue': (53, 57, 157),
    'brown': (114, 71, 40), 'green': (84, 109, 27), 'red': (160, 39, 34), 'black': (20, 21, 25),
}
TERRA = {
    'white': (209, 178, 161), 'orange': (161, 83, 37), 'magenta': (149, 88, 108), 'light_blue': (113, 108, 137),
    'yellow': (186, 133, 35), 'lime': (103, 117, 52), 'pink': (161, 78, 78), 'gray': (57, 42, 35),
    'light_gray': (135, 106, 97), 'cyan': (86, 91, 91), 'purple': (118, 70, 86), 'blue': (74, 59, 91),
    'brown': (77, 51, 35), 'green': (76, 83, 42), 'red': (143, 61, 46), 'black': (37, 22, 16),
}
WOOD = {
    'oak': (162, 130, 78), 'spruce': (114, 84, 48), 'birch': (196, 179, 123), 'jungle': (160, 115, 80),
    'acacia': (168, 90, 50), 'dark_oak': (66, 43, 20), 'mangrove': (117, 54, 48), 'cherry': (226, 178, 172),
    'bamboo': (196, 175, 82), 'crimson': (101, 48, 70), 'warped': (43, 104, 99),
}
LOGS = {
    'oak': (109, 85, 50), 'spruce': (58, 37, 16), 'birch': (216, 215, 210), 'jungle': (85, 67, 25),
    'acacia': (103, 96, 86), 'dark_oak': (60, 46, 26), 'mangrove': (84, 66, 36), 'cherry': (54, 33, 44),
}
SPECIFIC = {
    'grass_block': (95, 159, 53), 'dirt': (134, 96, 67), 'coarse_dirt': (119, 85, 59), 'rooted_dirt': (144, 103, 76),
    'dirt_path': (148, 122, 65), 'podzol': (91, 63, 24), 'mud': (60, 57, 60), 'packed_mud': (142, 106, 79),
    'mud_bricks': (137, 103, 79), 'sand': (219, 207, 163), 'red_sand': (190, 102, 33), 'gravel': (131, 127, 126),
    'stone': (125, 125, 125), 'cobblestone': (127, 127, 127), 'mossy_cobblestone': (110, 118, 94),
    'stone_bricks': (122, 121, 122), 'mossy_stone_bricks': (115, 121, 105), 'cracked_stone_bricks': (118, 117, 118),
    'chiseled_stone_bricks': (119, 118, 119), 'smooth_stone': (158, 158, 158), 'andesite': (136, 136, 136),
    'polished_andesite': (132, 134, 133), 'diorite': (188, 188, 188), 'polished_diorite': (192, 193, 194),
    'granite': (149, 103, 85), 'polished_granite': (154, 106, 89), 'deepslate': (80, 80, 82),
    'cobbled_deepslate': (77, 77, 80), 'polished_deepslate': (72, 72, 73), 'deepslate_bricks': (70, 70, 71),
    'deepslate_tiles': (54, 54, 55), 'calcite': (223, 224, 220), 'tuff': (108, 109, 102), 'basalt': (80, 81, 86),
    'blackstone': (42, 36, 41), 'polished_blackstone': (53, 48, 56), 'polished_blackstone_bricks': (48, 42, 49),
    'bricks': (150, 97, 83), 'sandstone': (216, 203, 155), 'smooth_sandstone': (223, 214, 170),
    'cut_sandstone': (217, 206, 159), 'red_sandstone': (186, 99, 29), 'smooth_red_sandstone': (181, 97, 31),
    'quartz_block': (235, 229, 222), 'smooth_quartz': (235, 229, 222), 'quartz_bricks': (234, 229, 221),
    'quartz_pillar': (235, 230, 224), 'chiseled_quartz_block': (231, 226, 218), 'prismarine': (99, 156, 151),
    'prismarine_bricks': (99, 171, 158), 'dark_prismarine': (51, 91, 75), 'sea_lantern': (172, 199, 190),
    'glowstone': (171, 131, 84), 'shroomlight': (240, 146, 70), 'ochre_froglight': (250, 245, 206),
    'verdant_froglight': (229, 244, 228), 'pearlescent_froglight': (245, 240, 239), 'water': (52, 92, 210),
    'lava': (207, 92, 15), 'ice': (145, 183, 253), 'packed_ice': (141, 180, 250), 'blue_ice': (116, 167, 253),
    'snow_block': (249, 254, 254), 'clay': (160, 166, 179), 'terracotta': (152, 94, 67), 'iron_block': (220, 220, 220),
    'gold_block': (246, 208, 61), 'diamond_block': (98, 237, 228), 'emerald_block': (42, 203, 87),
    'lapis_block': (30, 67, 140), 'copper_block': (192, 107, 79), 'cut_copper': (191, 106, 80),
    'oxidized_copper': (82, 162, 132), 'oxidized_cut_copper': (80, 154, 129), 'weathered_copper': (108, 153, 110),
    'exposed_copper': (161, 125, 103), 'hay_block': (166, 136, 38), 'bookshelf': (117, 94, 59),
    'glass': (200, 220, 230), 'glass_pane': (200, 220, 230), 'tinted_glass': (44, 38, 46), 'iron_bars': (136, 139, 135),
    'chain': (60, 65, 80), 'lantern': (106, 91, 83), 'soul_lantern': (71, 99, 114), 'torch': (255, 200, 80),
    'end_rod': (240, 230, 220), 'netherrack': (97, 38, 38), 'nether_bricks': (44, 21, 26),
    'red_nether_bricks': (69, 7, 9), 'magma_block': (142, 63, 31), 'obsidian': (15, 10, 24),
    'crying_obsidian': (32, 10, 60), 'moss_block': (89, 109, 45), 'moss_carpet': (89, 109, 45),
    'barrel': (134, 100, 58), 'composter': (117, 75, 40), 'cauldron': (74, 73, 74), 'cactus': (85, 127, 43),
    'sugar_cane': (148, 192, 101), 'lily_pad': (32, 128, 48), 'dead_bush': (107, 79, 41), 'pumpkin': (198, 118, 24),
    'jack_o_lantern': (214, 152, 52), 'melon': (111, 145, 30), 'bamboo_block': (127, 144, 58),
    'stripped_bamboo_block': (195, 173, 80), 'bamboo_mosaic': (190, 170, 78), 'target': (229, 176, 168),
    'redstone_block': (175, 24, 5), 'beacon': (117, 220, 215), 'purpur_block': (169, 125, 169),
    'purpur_pillar': (171, 129, 171), 'end_stone_bricks': (218, 224, 162), 'end_stone': (219, 222, 158),
    'amethyst_block': (133, 97, 191), 'sculk': (12, 29, 36), 'dripstone_block': (134, 107, 92),
    'slime_block': (111, 192, 91), 'honey_block': (251, 185, 52), 'honeycomb_block': (229, 148, 29),
    'bone_block': (229, 225, 207), 'scaffolding': (170, 132, 73), 'sponge': (195, 192, 74),
    'smooth_basalt': (72, 72, 78), 'cherry_leaves': (229, 172, 194), 'oak_leaves': (60, 120, 40),
    'birch_leaves': (110, 150, 70), 'spruce_leaves': (60, 95, 60), 'jungle_leaves': (50, 140, 30),
    'acacia_leaves': (90, 140, 30), 'dark_oak_leaves': (40, 100, 30), 'azalea_leaves': (95, 125, 45),
    'flowering_azalea_leaves': (120, 120, 80), 'mangrove_leaves': (70, 130, 40), 'cobweb': (220, 220, 220),
    'black_concrete': (8, 10, 15), 'note_block': (88, 58, 40), 'jukebox': (93, 64, 47), 'crafting_table': (120, 73, 42),
    'brown_mushroom_block': (149, 111, 81), 'red_mushroom_block': (200, 46, 45), 'mushroom_stem': (203, 196, 185),
    'nether_wart_block': (114, 2, 2), 'warped_wart_block': (22, 119, 121), 'dried_kelp_block': (50, 58, 38),
    'decorated_pot': (140, 80, 60), 'campfire': (110, 80, 50), 'smoker': (90, 80, 70), 'furnace': (110, 110, 110),
    'anvil': (68, 68, 68), 'bell': (250, 210, 60), 'lightning_rod': (200, 110, 80), 'flower_pot': (124, 68, 53),
    'ladder': (125, 97, 55), 'player_head': (100, 70, 50), 'cake': (240, 220, 210), 'lectern': (170, 135, 80),
    'cartography_table': (100, 80, 60), 'smithing_table': (60, 60, 70), 'loom': (150, 120, 90),
    'chiseled_bookshelf': (130, 100, 60), 'raw_iron_block': (166, 135, 107), 'raw_gold_block': (221, 169, 46),
    'raw_copper_block': (154, 105, 79), 'coal_block': (16, 15, 15), 'sculk_catalyst': (20, 30, 40),
    'cocoa': (150, 90, 40), 'redstone_lamp': (235, 170, 95), 'observer': (100, 100, 100), 'pink_petals': (240, 170, 200), 'torchflower': (220, 120, 60),
}
FLOWER_COL = {'dandelion': (255, 236, 79), 'poppy': (237, 48, 44), 'blue_orchid': (47, 162, 168),
              'allium': (190, 120, 230), 'azure_bluet': (230, 235, 240), 'red_tulip': (230, 50, 40),
              'orange_tulip': (240, 130, 40), 'white_tulip': (240, 240, 240), 'pink_tulip': (240, 170, 200),
              'oxeye_daisy': (240, 240, 230), 'cornflower': (70, 100, 220), 'lily_of_the_valley': (240, 240, 240),
              'sunflower': (250, 210, 40), 'lilac': (200, 140, 200), 'rose_bush': (200, 40, 40), 'peony': (230, 170, 220)}

TRANSPARENT_KEYS = ('glass', 'pane', 'iron_bars', 'leaves', 'water', 'fence', 'torch', 'flower', 'tulip', 'lantern',
                    'chain', 'rod', 'sign', 'banner', 'button', 'carpet', 'rail', 'ladder', 'vine', 'sapling',
                    'cane', 'lily', 'bush', 'fern', 'poppy', 'dandelion', 'orchid', 'allium', 'bluet', 'daisy',
                    'cornflower', 'petals', 'trapdoor', 'door', 'cocoa', 'candle', 'pot', 'head', 'bell', 'wall',
                    'stairs', 'slab', 'campfire')


def block_rgb(state):
    b = base_name(state)
    if b in SPECIFIC:
        return SPECIFIC[b]
    if b in FLOWER_COL:
        return FLOWER_COL[b]
    for c in sorted(DYE, key=len, reverse=True):
        if b.startswith(c + '_'):
            rest = b[len(c) + 1:]
            if rest.startswith('concrete_powder'):
                return tuple(min(255, int(v * 1.1 + 10)) for v in DYE[c])
            if rest.startswith('concrete') or rest.startswith('wool') or rest.startswith('carpet') or rest.startswith('bed') \
                    or rest.startswith('banner') or rest.startswith('wall_banner') or rest.startswith('candle') \
                    or rest.startswith('shulker'):
                return DYE[c]
            if rest.startswith('terracotta') or rest.startswith('glazed'):
                return TERRA[c]
            if 'glass' in rest:
                return tuple(int(v * 0.7 + 255 * 0.3) for v in DYE[c])
            return DYE[c]
    for w in sorted(WOOD, key=len, reverse=True):
        if b.startswith('stripped_' + w):
            return WOOD[w]
        if b.startswith(w + '_'):
            rest = b[len(w) + 1:]
            if rest.startswith(('log', 'wood')):
                return LOGS.get(w, WOOD[w])
            if rest.startswith('leaves'):
                return SPECIFIC.get(b, (60, 120, 40))
            return WOOD[w]
    # stairs/slabs/walls of stone types -> base material
    for suf in ('_stairs', '_slab', '_wall', '_pillar'):
        if b.endswith(suf):
            root = b[: -len(suf)]
            for cand in (root, root + 's', root + '_block', root.replace('brick', 'bricks')):
                if cand in SPECIFIC:
                    return SPECIFIC[cand]
            if root.startswith('smooth_quartz') or root.startswith('quartz'):
                return SPECIFIC['quartz_block']
    if 'potted' in b:
        return (124, 68, 53)
    if 'copper' in b:
        return (180, 110, 80)
    if 'deepslate' in b:
        return (65, 65, 68)
    if 'blackstone' in b:
        return (45, 40, 47)
    if 'brick' in b:
        return (150, 97, 83)
    if 'stone' in b:
        return (125, 125, 125)
    if 'sandstone' in b:
        return (216, 203, 155)
    if 'quartz' in b:
        return (235, 229, 222)
    if 'prismarine' in b:
        return (99, 156, 151)
    if 'iron' in b:
        return (190, 190, 190)
    if 'tulip' in b or 'flower' in b:
        return (220, 80, 120)
    return (150, 150, 150)


def palette_colors(world):
    cols = np.array([block_rgb(s) for s in world.pal], dtype=np.float32)
    cols[0] = (0, 0, 0)
    return cols


def is_transparent(state):
    b = base_name(state)
    if b == 'air':
        return True
    return any(k in b for k in TRANSPARENT_KEYS)


def topdown(world, scale=2, shade=True):
    a = world.a
    H, D, W = a.shape
    nz = a != 0
    rev = nz[::-1]
    has = rev.any(axis=0)
    top = H - 1 - rev.argmax(axis=0)
    top[~has] = 0
    zz, xx = np.mgrid[0:D, 0:W]
    ids = a[top, zz, xx]
    cols = palette_colors(world)
    img = cols[ids]
    img[~has] = (24, 40, 24)
    if shade:
        hgt = top.astype(np.float32)
        # hillshade from the north-west
        gx = np.zeros_like(hgt)
        gz = np.zeros_like(hgt)
        gx[:, 1:] = hgt[:, 1:] - hgt[:, :-1]
        gz[1:, :] = hgt[1:, :] - hgt[:-1, :]
        light = 1.0 + np.clip((gx + gz) * 0.06, -0.35, 0.35)
        height = 0.82 + np.clip((hgt - 6) / 140.0, 0, 0.4)
        img = img * (light * height)[..., None]
    img = np.clip(img, 0, 255).astype(np.uint8)
    im = Image.fromarray(img, 'RGB')
    if scale != 1:
        im = im.resize((W * scale, D * scale), Image.NEAREST)
    return im


def _iso_template(s):
    """Pixel template for one voxel: list of (dx, dy, face) with face 0=top, 1=south(left), 2=east(right)."""
    out = []
    hs = s / 2.0
    for py in range(-int(hs) - 1, s + int(hs) + 2):
        for px in range(-s, s):
            cx, cy = px + 0.5, py + 0.5
            # top diamond centred at (0,0): |x|/s + |y|/hs <= 1
            if abs(cx) / s + abs(cy) / hs <= 1.0:
                out.append((px, py, 0))
                continue
            # side faces: below the diamond's lower edges, height s
            if cx < 0:
                top_edge = hs * (1 - abs(cx) / s)      # lower edge of diamond on the left half
                if top_edge < cy <= top_edge + s and cx >= -s:
                    out.append((px, py, 1))
            else:
                top_edge = hs * (1 - abs(cx) / s)
                if top_edge < cy <= top_edge + s and cx < s:
                    out.append((px, py, 2))
    return out


def isometric(world, x0=0, z0=0, x1=None, z1=None, y0=0, px=2, bg=(196, 220, 238)):
    """Isometric render viewed from the south-east with a z-buffer."""
    a = world.a
    H, D, W = a.shape
    x1 = W if x1 is None else x1
    z1 = D if z1 is None else z1
    sub = a[y0:, z0:z1, x0:x1]
    trans = np.array([is_transparent(s) for s in world.pal], dtype=bool)
    tmask = trans[sub]
    solid = sub != 0
    exp = np.zeros_like(solid)
    exp[:, :, -1] |= solid[:, :, -1]
    exp[:, :, :-1] |= solid[:, :, :-1] & tmask[:, :, 1:]
    exp[-1, :, :] |= solid[-1, :, :]
    exp[:-1, :, :] |= solid[:-1, :, :] & tmask[1:, :, :]
    exp[:, -1, :] |= solid[:, -1, :]
    exp[:, :-1, :] |= solid[:, :-1, :] & tmask[:, 1:, :]
    ys, zs, xs = np.nonzero(exp)
    del exp, tmask, solid
    ids = sub[ys, zs, xs]
    cols = palette_colors(world)
    s = px
    hs = s // 2 if s > 1 else 1
    sx = (xs.astype(np.int32) - zs.astype(np.int32)) * s
    sy = (xs.astype(np.int32) + zs.astype(np.int32)) * hs - ys.astype(np.int32) * s
    tpl = _iso_template(s)
    mnx = min(t[0] for t in tpl)
    mny = min(t[1] for t in tpl)
    sx = sx - sx.min() - mnx
    sy = sy - sy.min() - mny
    Wimg = int(sx.max()) + 2 * s + 2
    Himg = int(sy.max()) + 2 * s + 2
    depth = (xs.astype(np.int32) + zs.astype(np.int32) + ys.astype(np.int32))
    zbuf = np.full(Wimg * Himg, -1, dtype=np.int32)
    for dx, dy, f in tpl:
        pix = (sy + dy) * Wimg + (sx + dx)
        np.maximum.at(zbuf, pix, depth)
    img = np.empty((Himg * Wimg, 3), dtype=np.float32)
    img[:] = bg
    shade = {0: 1.0, 1: 0.80, 2: 0.64}
    base = cols[ids]
    for dx, dy, f in tpl:
        pix = (sy + dy) * Wimg + (sx + dx)
        m = zbuf[pix] == depth
        img[pix[m]] = base[m] * shade[f]
    img = np.clip(img, 0, 255).astype(np.uint8).reshape(Himg, Wimg, 3)
    return Image.fromarray(img, 'RGB')


def _font(size):
    for f in ('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',
              '/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf'):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def annotate(im, labels, scale=2, title=None, legend=None):
    """Park map: land names, numbered attraction markers, legend panel, compass and scale bar."""
    W0, H0 = im.size
    panel = 560
    out = Image.new('RGB', (W0 + panel, H0 + 90), (245, 240, 228))
    out.paste(im, (0, 90))
    d = ImageDraw.Draw(out)
    big, mid, small, tiny = _font(40), _font(22), _font(17), _font(14)
    d.text((24, 22), title or 'WONDERHAVEN  -  Park Map', font=big, fill=(40, 30, 90))
    attractions = [l for l in labels if l[3] == 'attraction']
    others = [l for l in labels if l[3] == 'small']
    lands = [l for l in labels if l[3] == 'land']
    for (x, z, text, kind) in others:
        X, Z = min(max(x * scale, 60), W0 - 60), z * scale + 90
        bb = d.textbbox((X, Z), text, font=tiny, anchor='mm')
        d.rectangle([bb[0] - 3, bb[1] - 2, bb[2] + 3, bb[3] + 2], fill=(255, 255, 240))
        d.text((X, Z), text, font=tiny, fill=(30, 30, 30), anchor='mm')
    for (x, z, text, kind) in lands:
        X, Z = min(max(x * scale, 150), W0 - 150), z * scale + 90
        bb = d.textbbox((X, Z), text, font=mid, anchor='mm')
        d.rectangle([bb[0] - 6, bb[1] - 4, bb[2] + 6, bb[3] + 4], fill=(40, 30, 90), outline=(255, 215, 80), width=2)
        d.text((X, Z), text, font=mid, fill=(255, 235, 140), anchor='mm')
    for i, (x, z, text, kind) in enumerate(attractions, 1):
        X, Z = x * scale, z * scale + 90
        r = 14
        d.ellipse([X - r, Z - r, X + r, Z + r], fill=(200, 30, 40), outline=(255, 255, 255), width=3)
        d.text((X, Z), str(i), font=small, fill=(255, 255, 255), anchor='mm')
    # legend panel
    px = W0 + 20
    d.text((px, 110), 'ATTRACTIONS & LANDMARKS', font=mid, fill=(40, 30, 90))
    y = 150
    for i, (x, z, text, kind) in enumerate(attractions, 1):
        d.ellipse([px, y - 2, px + 24, y + 22], fill=(200, 30, 40))
        d.text((px + 12, y + 10), str(i), font=tiny, fill=(255, 255, 255), anchor='mm')
        d.text((px + 34, y), text, font=small, fill=(30, 30, 30))
        y += 30
    if legend:
        y += 20
        for line in legend:
            d.text((px, y), line, font=small, fill=(60, 60, 60))
            y += 26
    # compass + scale bar
    cx, cy = W0 - 70, 160
    d.ellipse([cx - 36, cy - 36, cx + 36, cy + 36], fill=(255, 255, 255), outline=(40, 30, 90), width=3)
    d.polygon([(cx, cy - 30), (cx - 10, cy + 6), (cx + 10, cy + 6)], fill=(200, 30, 40))
    d.text((cx, cy + 20), 'N', font=small, fill=(40, 30, 90), anchor='mm')
    sx, sy = 30, H0 + 60
    d.rectangle([sx - 8, sy - 14, sx + 100 * scale + 110, sy + 20], fill=(255, 255, 255))
    d.rectangle([sx, sy, sx + 100 * scale, sy + 8], fill=(40, 30, 90))
    d.text((sx + 100 * scale + 8, sy + 4), '100 blocks', font=tiny, fill=(30, 30, 30), anchor='lm')
    return out
