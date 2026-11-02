"""Generic architecture: roofs, walls, windows, furnished interiors and small service buildings.

All functions taking a Builder `b` work in local coordinates: front wall at lz=0 facing local north,
x to the right (0..width-1), z backward (0..depth-1), ly=0 is the first floor level (ground + 1).
"""
import math
import random

from .const import G
from .geom import disc, circle, ring
from .world import Builder, DVEC, OPP, hash2


# =============================================================================================
# Styles
# =============================================================================================
def style(**kw):
    s = dict(wall='bricks', base=None, pillar='stone_bricks', trim='smooth_stone', floor='spruce_planks',
             ceiling='spruce_planks', window='glass_pane', roof_stairs='dark_oak_stairs', roof_slab='dark_oak_slab',
             roof_block='dark_oak_planks', gable=None, door='dark_oak', awning=None, sign_wood='dark_oak',
             light='lantern[hanging=true]', carpet='red_carpet', counter='stripped_oak_wood[axis=y]',
             counter_top='smooth_stone_slab[type=bottom]', shelf=('bookshelf', 'barrel[facing=up]'),
             wall_light='lantern[hanging=false]', interior_wall=None, sign_color='black', sign_glow=False)
    s.update(kw)
    if s['base'] is None:
        s['base'] = s['wall']
    if s['gable'] is None:
        s['gable'] = s['wall']
    return s


# =============================================================================================
# Roofs (local coordinates)
# =============================================================================================
def gable_roof(b, x0, x1, z0, z1, y, st, ridge='x', ov=1, steep=False, gable=None, full=None, slab=None,
               fill_gable=True):
    """Gable roof over wall rectangle [x0..x1]x[z0..z1] with the first stair row at height y."""
    full = full or st.replace('_stairs', '_planks') if 'oak' in st or 'spruce' in st else (full or 'stone_bricks')
    step = 2 if steep else 1
    if ridge == 'x':
        lo, hi = z0 - ov, z1 + ov
        k = 0
        while lo + k < hi - k:
            yy = y + k * step
            for x in range(x0 - ov, x1 + ov + 1):
                b.set(x, yy + step - 1, lo + k, f'{st}[facing=south,half=bottom]')
                b.set(x, yy + step - 1, hi - k, f'{st}[facing=north,half=bottom]')
                if steep:
                    b.set(x, yy, lo + k, full)
                    b.set(x, yy, hi - k, full)
            if fill_gable and gable:
                for z in range(max(z0, lo + k + 1), min(z1, hi - k - 1) + 1):
                    for dy in range(step):
                        b.set(x0, yy + dy, z, gable)
                        b.set(x1, yy + dy, z, gable)
            k += 1
        if lo + k == hi - k:
            yy = y + k * step
            for x in range(x0 - ov, x1 + ov + 1):
                if steep:
                    b.set(x, yy, lo + k, full)
                b.set(x, yy + step - 1, lo + k, slab or full)
            if fill_gable and gable and steep:
                b.set(x0, yy, lo + k, gable)
                b.set(x1, yy, lo + k, gable)
        return y + k * step + 1
    else:
        lo, hi = x0 - ov, x1 + ov
        k = 0
        while lo + k < hi - k:
            yy = y + k * step
            for z in range(z0 - ov, z1 + ov + 1):
                b.set(lo + k, yy + step - 1, z, f'{st}[facing=east,half=bottom]')
                b.set(hi - k, yy + step - 1, z, f'{st}[facing=west,half=bottom]')
                if steep:
                    b.set(lo + k, yy, z, full)
                    b.set(hi - k, yy, z, full)
            if fill_gable and gable:
                for x in range(max(x0, lo + k + 1), min(x1, hi - k - 1) + 1):
                    for dy in range(step):
                        b.set(x, yy + dy, z0, gable)
                        b.set(x, yy + dy, z1, gable)
            k += 1
        if lo + k == hi - k:
            yy = y + k * step
            for z in range(z0 - ov, z1 + ov + 1):
                if steep:
                    b.set(lo + k, yy, z, full)
                b.set(lo + k, yy + step - 1, z, slab or full)
            if fill_gable and gable and steep:
                b.set(lo + k, yy, z0, gable)
                b.set(lo + k, yy, z1, gable)
        return y + k * step + 1


def hip_roof(b, x0, x1, z0, z1, y, st, ov=1, steep=False, top=None, full=None):
    """Hip roof: concentric rings of stairs facing inward."""
    full = full or 'dark_oak_planks'
    top = top or full
    step = 2 if steep else 1
    k = 0
    while True:
        ax0, ax1, az0, az1 = x0 - ov + k, x1 + ov - k, z0 - ov + k, z1 + ov - k
        yy = y + k * step
        if ax1 - ax0 < 1 or az1 - az0 < 1:
            for x in range(ax0, ax1 + 1):
                for z in range(az0, az1 + 1):
                    if steep:
                        b.set(x, yy, z, full)
                    b.set(x, yy + step - 1, z, top)
            return yy + step
        for x in range(ax0, ax1 + 1):
            b.set(x, yy + step - 1, az0, f'{st}[facing=south,half=bottom]')
            b.set(x, yy + step - 1, az1, f'{st}[facing=north,half=bottom]')
            if steep:
                b.set(x, yy, az0, full)
                b.set(x, yy, az1, full)
        for z in range(az0 + 1, az1):
            b.set(ax0, yy + step - 1, z, f'{st}[facing=east,half=bottom]')
            b.set(ax1, yy + step - 1, z, f'{st}[facing=west,half=bottom]')
            if steep:
                b.set(ax0, yy, z, full)
                b.set(ax1, yy, z, full)
        k += 1
        if ax1 - ax0 <= 2 or az1 - az0 <= 2:
            ax0, ax1, az0, az1 = x0 - ov + k, x1 + ov - k, z0 - ov + k, z1 + ov - k
            yy = y + k * step
            for x in range(ax0, ax1 + 1):
                for z in range(az0, az1 + 1):
                    if steep:
                        b.set(x, yy, z, full)
                    b.set(x, yy + step - 1, z, top)
            return yy + step


def flat_roof(b, x0, x1, z0, z1, y, block, parapet=None, cornice=None, parapet_h=1):
    b.fill(x0, y, z0, x1, y, z1, block)
    if parapet:
        for x in range(x0, x1 + 1):
            for h in range(parapet_h):
                b.set(x, y + 1 + h, z0, parapet)
                b.set(x, y + 1 + h, z1, parapet)
        for z in range(z0, z1 + 1):
            for h in range(parapet_h):
                b.set(x0, y + 1 + h, z, parapet)
                b.set(x1, y + 1 + h, z, parapet)
    if cornice:
        for x in range(x0 - 1, x1 + 2):
            b.set(x, y, z0 - 1, f'{cornice}[facing=south,half=top]')
            b.set(x, y, z1 + 1, f'{cornice}[facing=north,half=top]')
        for z in range(z0, z1 + 1):
            b.set(x0 - 1, y, z, f'{cornice}[facing=east,half=top]')
            b.set(x1 + 1, y, z, f'{cornice}[facing=west,half=top]')


def cone_roof(w, cx, cz, y0, r, h, st, full, finial=None, cut_inner=True):
    """Conical roof in world coords, center (cx,cz) may be .5; stairs on the outer edge of each layer."""
    for i in range(h):
        ri = r * (1 - i / h)
        rn = r * (1 - (i + 1) / h)
        if ri < 0.5:
            break
        for x in range(int(math.floor(cx - ri)) - 1, int(math.ceil(cx + ri)) + 2):
            for z in range(int(math.floor(cz - ri)) - 1, int(math.ceil(cz + ri)) + 2):
                dx, dz = x - cx, z - cz
                d = math.sqrt(dx * dx + dz * dz)
                if d > ri + 0.25:
                    continue
                if cut_inner and d < rn - 1.2:
                    continue
                if d > ri - 1.0 and d > 0.7:
                    if abs(dx) >= abs(dz):
                        f = 'west' if dx > 0 else 'east'
                    else:
                        f = 'north' if dz > 0 else 'south'
                    w.set(x, y0 + i, z, f'{st}[facing={f},half=bottom]')
                else:
                    w.set(x, y0 + i, z, full)
    top = y0 + h
    if finial:
        w.set(int(round(cx)), top - 1, int(round(cz)), full)
        for k, blk in enumerate(finial):
            w.set(int(round(cx)), top + k, int(round(cz)), blk)
    return top


def dome(w, cx, cy, cz, r, block, ry=None, thick=1.0, top_only=True, ring_block=None, ring_every=None):
    """Hemispherical (or ellipsoidal) dome shell."""
    ry = ry or r
    R = int(math.ceil(r)) + 1
    RY = int(math.ceil(ry)) + 1
    for x in range(-R, R + 1):
        for z in range(-R, R + 1):
            for y in range(0 if top_only else -RY, RY + 1):
                d = math.sqrt((x / r) ** 2 + (z / r) ** 2 + (y / ry) ** 2)
                if 1 - thick / max(r, ry) < d <= 1.0:
                    blk = block
                    if ring_block and ring_every and (y % ring_every == 0):
                        blk = ring_block
                    w.set(int(cx + x), int(cy + y), int(cz + z), blk)


# =============================================================================================
# Walls & openings
# =============================================================================================
def box_walls(b, x0, x1, z0, z1, y0, y1, wall, pillar=None):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            b.set(x, y, z0, wall)
            b.set(x, y, z1, wall)
        for z in range(z0, z1 + 1):
            b.set(x0, y, z, wall)
            b.set(x1, y, z, wall)
        if pillar:
            for (x, z) in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):
                b.set(x, y, z, pillar)


def windows_row(b, x0, x1, z, y0, h, pane, spacing=3, skip=()):
    """Windows in a wall running along local x at fixed z (front/back)."""
    for x in range(x0, x1 + 1):
        if (x - x0) % spacing == spacing // 2 and x not in skip:
            for y in range(y0, y0 + h):
                b.set(x, y, z, pane)


def windows_col(b, z0, z1, x, y0, h, pane, spacing=3, skip=()):
    for z in range(z0, z1 + 1):
        if (z - z0) % spacing == spacing // 2 and z not in skip:
            for y in range(y0, y0 + h):
                b.set(x, y, z, pane)


# =============================================================================================
# Interiors
# =============================================================================================
def hang_lights(b, x0, x1, z0, z1, y_ceiling, light='lantern[hanging=true]', spacing=4):
    for x in range(x0 + 1, x1, spacing):
        for z in range(z0 + 1, z1, spacing):
            if b.is_air(x, y_ceiling - 1, z) and not b.is_air(x, y_ceiling, z):
                b.set(x, y_ceiling - 1, z, light)


def interior_shop(b, W, Dp, y, h, st, rnd, goods=None):
    """Retail interior: perimeter shelving, back counter, center displays."""
    x0, x1, z0, z1 = 1, W - 2, 1, Dp - 2
    b.fill(x0, y - 1, z0, x1, y - 1, z1, st['floor'])
    # rug
    if W >= 7 and Dp >= 7:
        b.fill(x0 + 2, y, z0 + 2, x1 - 2, y, z1 - 3, st['carpet'])
    shelves = list(goods or st['shelf'])
    # side shelving (leave the front 2 cells free for windows/door)
    for z in range(z0 + 2, z1):
        for x in (x0, x1):
            blk = shelves[(z + x) % len(shelves)]
            b.set(x, y, z, blk)
            if h >= 4:
                b.set(x, y + 1, z, shelves[(z + x + 1) % len(shelves)])
                if rnd.random() < 0.5:
                    b.set(x, y + 2, z, ['flower_pot', 'decorated_pot', 'lantern', 'candle[candles=3,lit=true]'][rnd.randint(0, 3)])
    # back counter
    zc = z1 - 2
    if zc > z0 + 2:
        for x in range(x0 + 2, x1 - 1):
            b.set(x, y, zc, st['counter'])
            b.set(x, y + 1, zc, st['counter_top'])
        b.set(x0 + 2, y + 1, zc, 'lectern[facing=north]' if False else st['counter_top'])
        mid = (x0 + x1) // 2
        b.set(mid, y + 1, zc, 'cartography_table' if rnd.random() < 0.5 else 'smithing_table')
        # back wall shelves behind counter
        for x in range(x0 + 1, x1):
            b.set(x, y, z1, shelves[x % len(shelves)])
            b.set(x, y + 1, z1, 'chiseled_bookshelf[facing=north]' if x % 3 == 0 else shelves[(x + 1) % len(shelves)])
    # center display tables
    for x in range(x0 + 3, x1 - 2, 3):
        for z in range(z0 + 3, zc - 1, 3):
            b.set(x, y, z, 'oak_fence' if rnd.random() < 0.5 else 'barrel[facing=up]')
            top = rnd.choice(['oak_pressure_plate', 'white_carpet', 'potted_cactus', 'cake', 'decorated_pot',
                              'player_head', 'candle[candles=2,lit=true]', 'flower_pot'])
            b.set(x, y + 1, z, top)
    hang_lights(b, x0, x1, z0, z1, y + h, st['light'], 3)


def interior_restaurant(b, W, Dp, y, h, st, rnd, wood='spruce'):
    x0, x1, z0, z1 = 1, W - 2, 1, Dp - 2
    b.fill(x0, y - 1, z0, x1, y - 1, z1, st['floor'])
    # kitchen at back
    kz = z1 - 3 if Dp >= 10 else z1 - 1
    if Dp >= 10:
        for x in range(x0, x1 + 1):
            b.set(x, y, kz, st['counter'])
            b.set(x, y + 1, kz, st['counter_top'])
        b.set((x0 + x1) // 2, y, kz, 'air')
        b.set((x0 + x1) // 2, y + 1, kz, 'air')
        appliances = ['smoker[facing=north,lit=true]', 'furnace[facing=north,lit=false]', 'cauldron',
                      'barrel[facing=north]', 'crafting_table', 'blast_furnace[facing=north,lit=false]',
                      'campfire[lit=false,facing=north,signal_fire=false]']
        for i, x in enumerate(range(x0, x1 + 1)):
            b.set(x, y, z1, appliances[i % len(appliances)])
            if i % 2 == 0:
                b.set(x, y + 1, z1, 'barrel[facing=north]' if i % 4 else 'bookshelf')
    # dining tables
    for x in range(x0 + 2, x1 - 1, 4):
        for z in range(z0 + 2, kz - 1, 4):
            b.set(x, y, z, f'{wood}_fence')
            b.set(x, y + 1, z, rnd.choice(['white_carpet', 'red_carpet', 'oak_pressure_plate', 'candle[candles=1,lit=true]', 'flower_pot']))
            for d in ('north', 'south', 'east', 'west'):
                dx, dz = DVEC[d]
                if x0 <= x + dx <= x1 and z0 <= z + dz < kz:
                    b.set(x + dx, y, z + dz, f'{wood}_stairs[facing={d},half=bottom]')
    hang_lights(b, x0, x1, z0, z1, y + h, st['light'], 4)


def interior_office(b, W, Dp, y, h, st, rnd, beds=False):
    x0, x1, z0, z1 = 1, W - 2, 1, Dp - 2
    b.fill(x0, y - 1, z0, x1, y - 1, z1, st['floor'])
    if W >= 6 and Dp >= 6:
        b.fill(x0 + 1, y, z0 + 1, x1 - 1, y, z1 - 1, st['carpet'])
    for x in range(x0, x1 + 1, 2):
        b.set(x, y, z1, 'bookshelf')
        b.set(x, y + 1, z1, 'bookshelf' if rnd.random() < 0.6 else 'barrel[facing=north]')
    # desks
    for x in range(x0 + 1, x1 - 1, 4):
        b.set(x, y, z0 + 2, 'spruce_stairs[facing=south,half=top]')
        b.set(x + 1, y, z0 + 2, 'spruce_stairs[facing=south,half=top]')
        b.set(x, y + 1, z0 + 2, 'lantern[hanging=false]' if x % 2 else 'flower_pot')
        b.set(x, y, z0 + 3, 'dark_oak_stairs[facing=south,half=bottom]')
    if beds and Dp >= 6:
        from .world import bed
        bx, bz = b.xz(x1, z1 - 2)
        bed(b.w, bx, b.oy + y, bz, b.d('west') if False else b.d('north'), rnd.choice(['red', 'blue', 'green', 'cyan']))
    hang_lights(b, x0, x1, z0, z1, y + h, st['light'], 4)


def interior_restroom(b, W, Dp, y, h, st):
    """Two-sided restroom split down the middle (left/right of the door axis)."""
    x0, x1, z0, z1 = 1, W - 2, 1, Dp - 2
    b.fill(x0, y - 1, z0, x1, y - 1, z1, 'smooth_quartz')
    mid = W // 2
    for yy in range(y, y + h):
        for z in range(z0, z1 + 1):
            b.set(mid, yy, z, 'white_terracotta')
    for side in (0, 1):
        sx0, sx1 = (x0, mid - 1) if side == 0 else (mid + 1, x1)
        # stalls along back wall
        for x in range(sx0, sx1 + 1, 2):
            b.set(x, y, z1, 'quartz_stairs[facing=north,half=bottom]' if False else 'white_concrete')
            b.set(x, y + 1, z1, 'white_terracotta')
        # sinks along side wall
        sx = sx0 if side == 0 else sx1
        for z in range(z0 + 1, z1 - 1, 2):
            b.set(sx, y, z, 'cauldron')
            b.set(sx, y + 1, z, 'glass_pane' if False else 'light_gray_stained_glass')
        # stall partitions with doors
        for x in range(sx0 + 1, sx1 + 1, 2):
            if z1 - 1 > z0:
                pass
    hang_lights(b, x0, x1, z0, z1, y + h, 'sea_lantern' if False else st['light'], 3)


# =============================================================================================
# Complete buildings
# =============================================================================================
def building(w, x0, z0, width, depth, facing, st, floors=2, fh=5, roof='gable_x', name=None, kind='shop',
             y=G + 1, seed=0, door_double=False, awning=True, sign_lines=None, roof_overhang=1,
             window_spacing=3, jetty=False, false_front=0, porch=0, chimney=False, goods=None):
    """General multi-storey building. Returns the Builder used."""
    rnd = random.Random(seed or (x0 * 7919 + z0 * 104729))
    b = Builder.for_rect(w, x0, z0, width, depth, facing, y)
    Wd, Dp = width, depth
    top = floors * fh
    # clear volume
    b.clear(-porch - 2, 0, -porch - 2, Wd + 1, top + max(Wd, Dp) // 2 + 4 + false_front, Dp + 1)
    b.clear(0, -1, 0, Wd - 1, -1, Dp - 1)
    b.fill(0, -1, 0, Wd - 1, -1, Dp - 1, st['floor'])
    # walls
    for f in range(floors):
        yb = f * fh
        wall = st['base'] if f == 0 else st['wall']
        box_walls(b, 0, Wd - 1, 0, Dp - 1, yb, yb + fh - 1, wall, st['pillar'])
        # ceiling/floor slab + exterior trim band
        b.fill(1, yb + fh - 1, 1, Wd - 2, yb + fh - 1, Dp - 2, st['ceiling'])
        for x in range(0, Wd):
            b.set(x, yb + fh - 1, 0, st['trim'])
            b.set(x, yb + fh - 1, Dp - 1, st['trim'])
        for z in range(0, Dp):
            b.set(0, yb + fh - 1, z, st['trim'])
            b.set(Wd - 1, yb + fh - 1, z, st['trim'])
        b.fill(1, yb, 1, Wd - 2, yb + fh - 2, Dp - 2, 'air')
        # windows
        wy, wh = yb + 1, max(1, fh - 3)
        if f == 0:
            # display windows on the front
            dx0 = Wd // 2 - (1 if door_double else 0)
            for x in range(1, Wd - 1):
                if x in (dx0 - 1, dx0, dx0 + 1, dx0 + 2) if door_double else x in (dx0 - 1, dx0, dx0 + 1):
                    continue
                if Wd >= 7 and (x == 1 or x == Wd - 2) and fh >= 5:
                    continue
                for yy in range(yb + 1, yb + fh - 2):
                    b.set(x, yy, 0, st['window'])
        else:
            windows_row(b, 1, Wd - 2, 0, wy, wh, st['window'], window_spacing)
            if jetty:
                pass
        windows_row(b, 1, Wd - 2, Dp - 1, wy, wh, st['window'], window_spacing)
        windows_col(b, 1, Dp - 2, 0, wy, wh, st['window'], window_spacing)
        windows_col(b, 1, Dp - 2, Wd - 1, wy, wh, st['window'], window_spacing)
        # interior
        ih = fh - 1
        if f == 0:
            if kind == 'shop':
                interior_shop(b, Wd, Dp, yb, ih, st, rnd, goods)
            elif kind == 'restaurant':
                interior_restaurant(b, Wd, Dp, yb, ih, st, rnd)
            elif kind == 'office':
                interior_office(b, Wd, Dp, yb, ih, st, rnd)
            elif kind == 'restroom':
                interior_restroom(b, Wd, Dp, yb, ih, st)
            else:
                b.fill(1, yb - 1, 1, Wd - 2, yb - 1, Dp - 2, st['floor'])
                hang_lights(b, 1, Wd - 2, 1, Dp - 2, yb + ih, st['light'], 4)
        else:
            if kind == 'restaurant' and f == 1:
                interior_restaurant(b, Wd, Dp, yb, ih, st, rnd)
            else:
                interior_office(b, Wd, Dp, yb, ih, st, rnd, beds=(f == floors - 1 and rnd.random() < 0.5))
    # ladders between floors in the back-left corner
    if floors > 1:
        for f in range(floors - 1):
            yb = f * fh
            for yy in range(yb, yb + fh):
                b.set(1, yy, Dp - 2, 'ladder[facing=east]')
    # door
    dx0 = Wd // 2 - (1 if door_double else 0)
    b.clear(dx0, 0, 0, dx0 + (1 if door_double else 0), 1, 1)
    if door_double:
        b.double_door(dx0, 0, 0, 'south', st['door'])
    else:
        b.door(dx0, 0, 0, 'south', st['door'])
    b.set(dx0, 2, 0, st['trim']) if fh > 4 else None
    # exterior lights flanking door
    for sx in ((dx0 - 1, dx0 + 2) if door_double else (dx0 - 1, dx0 + 1)):
        if 0 < sx < Wd - 1 and b.is_air(sx, 2, -1):
            b.set(sx, 2, -1, st['wall_light'].replace('[hanging=false]', '[hanging=false]'))
            b.set(sx, 1, -1, 'air')
    # awning
    if awning and st.get('awning'):
        cols = st['awning']
        for x in range(0, Wd):
            c = cols[x % len(cols)]
            b.set(x, fh - 2, -1, f'{c}_wool' if not c.endswith(('_wool', '_concrete', '_terracotta')) else c)
            b.set(x, fh - 3, -2, f'{c}_wool' if not c.endswith(('_wool', '_concrete', '_terracotta')) else c)
            b.set(x, fh - 4, -2, 'air')
        for x in (0, Wd - 1):
            pass
    # sign above the door
    lines = sign_lines or ([name] if name else None)
    if lines:
        sy = fh - 2 if not (awning and st.get('awning')) else fh - 1
        if len(lines) == 1:
            lines = ['', lines[0], '', '']
        if b.is_air(dx0, sy, -1) or True:
            b.sign(dx0, sy, -1, lines, wood=st['sign_wood'], facing='north', color=st['sign_color'],
                   glow=st['sign_glow'])
            if door_double:
                b.sign(dx0 + 1, sy, -1, lines, wood=st['sign_wood'], facing='north', color=st['sign_color'],
                       glow=st['sign_glow'])
    # false front facade (western style)
    if false_front:
        for x in range(0, Wd):
            for yy in range(top, top + false_front):
                b.set(x, yy, 0, st['wall'])
            b.set(x, top + false_front, 0, st['trim'])
        for yy in range(top, top + false_front + 1):
            b.set(0, yy, 0, st['pillar'])
            b.set(Wd - 1, yy, 0, st['pillar'])
    # porch (covered boardwalk in front)
    if porch:
        for x in range(-1 if Wd > 4 else 0, Wd + (1 if Wd > 4 else 0)):
            for z in range(-porch, 0):
                b.set(x, -1, z, st.get('porch_floor', 'spruce_planks'))
                b.set(x, fh - 1, z, st.get('porch_roof', 'spruce_slab[type=bottom]'))
        for x in (-1, Wd):
            if Wd > 4:
                for yy in range(0, fh - 1):
                    b.set(x, yy, -porch, st.get('porch_post', 'spruce_fence'))
        for x in range(1, Wd - 1, 4):
            for yy in range(0, fh - 1):
                b.set(x, yy, -porch, st.get('porch_post', 'spruce_fence'))
        for x in range(-1, Wd + 1):
            if b.is_air(x, 0, -porch):
                b.set(x, 0, -porch, st.get('porch_rail', 'spruce_fence'))
        b.clear(dx0, 0, -porch, dx0 + (1 if door_double else 0), 1, -porch)
    # roof
    ry = top
    if roof == 'gable_x':
        gable_roof(b, 0, Wd - 1, 0, Dp - 1, ry, st['roof_stairs'], 'x', roof_overhang, gable=st['gable'],
                   full=st['roof_block'], slab=st['roof_slab'] + '[type=bottom]')
    elif roof == 'gable_z':
        gable_roof(b, 0, Wd - 1, 0, Dp - 1, ry, st['roof_stairs'], 'z', roof_overhang, gable=st['gable'],
                   full=st['roof_block'], slab=st['roof_slab'] + '[type=bottom]')
    elif roof == 'steep_x':
        gable_roof(b, 0, Wd - 1, 0, Dp - 1, ry, st['roof_stairs'], 'x', roof_overhang, steep=True,
                   gable=st['gable'], full=st['roof_block'], slab=st['roof_slab'] + '[type=bottom]')
    elif roof == 'steep_z':
        gable_roof(b, 0, Wd - 1, 0, Dp - 1, ry, st['roof_stairs'], 'z', roof_overhang, steep=True,
                   gable=st['gable'], full=st['roof_block'], slab=st['roof_slab'] + '[type=bottom]')
    elif roof == 'hip':
        hip_roof(b, 0, Wd - 1, 0, Dp - 1, ry, st['roof_stairs'], roof_overhang, full=st['roof_block'],
                 top=st['roof_slab'] + '[type=bottom]')
    elif roof == 'steep_hip':
        hip_roof(b, 0, Wd - 1, 0, Dp - 1, ry, st['roof_stairs'], roof_overhang, steep=True, full=st['roof_block'],
                 top=st['roof_slab'] + '[type=bottom]')
    elif roof == 'flat':
        flat_roof(b, 0, Wd - 1, 0, Dp - 1, ry, st['roof_block'], parapet=st.get('parapet'),
                  cornice=st.get('cornice'))
    elif roof == 'low_gable_behind':
        gable_roof(b, 0, Wd - 1, 1, Dp - 1, ry, st['roof_stairs'], 'z', 0, gable=st['gable'],
                   full=st['roof_block'], slab=st['roof_slab'] + '[type=bottom]')
    if chimney:
        cx = Wd - 3
        span = Dp if roof.endswith('_x') else Wd
        rh = (span // 2) * (2 if roof.startswith('steep') else 1) + 2
        for yy in range(top - 2, top + rh):
            b.set(cx, yy, Dp - 3, 'bricks')
        b.set(cx, top + rh, Dp - 3, 'campfire[lit=true,signal_fire=false,facing=north]')
    return b


def restroom(w, x0, z0, facing, st, y=G + 1, roof='hip'):
    """9 x 7 restroom block with separate men's / women's sides."""
    Wd, Dp, fh = 11, 8, 5
    b = Builder.for_rect(w, x0, z0, Wd, Dp, facing, y)
    b.clear(-2, 0, -2, Wd + 1, fh + 8, Dp + 1)
    b.fill(0, -1, 0, Wd - 1, -1, Dp - 1, 'smooth_stone')
    box_walls(b, 0, Wd - 1, 0, Dp - 1, 0, fh - 1, st['wall'], st['pillar'])
    b.fill(1, fh - 1, 1, Wd - 2, fh - 1, Dp - 2, st['ceiling'])
    for x in range(Wd):
        b.set(x, fh - 1, 0, st['trim'])
        b.set(x, fh - 1, Dp - 1, st['trim'])
    for z in range(Dp):
        b.set(0, fh - 1, z, st['trim'])
        b.set(Wd - 1, fh - 1, z, st['trim'])
    b.fill(1, 0, 1, Wd - 2, fh - 2, Dp - 2, 'air')
    b.fill(1, -1, 1, Wd - 2, -1, Dp - 2, 'smooth_quartz')
    mid = Wd // 2
    for yy in range(0, fh - 1):
        for z in range(1, Dp - 1):
            b.set(mid, yy, z, 'white_terracotta')
    # doors near the middle on each side
    b.door(mid - 2, 0, 0, 'south', st['door'], 'right')
    b.door(mid + 2, 0, 0, 'south', st['door'], 'left')
    b.sign(mid - 2, 2, -1, ['', 'RESTROOM', '(MEN)', ''], wood=st['sign_wood'], facing='north')
    b.sign(mid + 2, 2, -1, ['', 'RESTROOM', '(WOMEN)', ''], wood=st['sign_wood'], facing='north')
    b.sign(mid, 3, -1, ['', 'RESTROOMS', '& BABY CARE', ''], wood=st['sign_wood'], facing='north')
    for side in (-1, 1):
        # stalls: 2 stalls along the back, each 2 wide with a door
        xs = range(1, mid) if side < 0 else range(mid + 1, Wd - 1)
        xs = list(xs)
        for x in xs:
            b.set(x, 0, Dp - 2, 'quartz_slab[type=bottom]' if x % 2 == 0 else 'air')
        # stall partition line at z = Dp-3 with doors
        for i, x in enumerate(xs):
            for yy in range(0, 3):
                b.set(x, yy, Dp - 3, 'white_concrete' if i % 2 == 0 else 'air')
        for i, x in enumerate(xs):
            if i % 2 == 1:
                b.door(x, 0, Dp - 3, 'south', 'birch', 'left')
        # toilets (white bowl look)
        for i, x in enumerate(xs):
            if i % 2 == 1:
                b.set(x, 0, Dp - 2, 'quartz_stairs[facing=south,half=bottom]')
        # sinks with mirrors on the outer side wall
        sx = 1 if side < 0 else Wd - 2
        for z in (2, 3):
            b.set(sx, 0, z, 'cauldron')
            b.set(sx, 1, z, 'tripwire_hook[facing=east,attached=false,powered=false]' if side < 0 else 'tripwire_hook[facing=west,attached=false,powered=false]')
        b.set(sx, 2, 2, 'light_gray_stained_glass_pane')
        b.set(sx, 2, 3, 'light_gray_stained_glass_pane')
        # sink wall needs to be solid behind hooks: walls are at x=0 / Wd-1 so hooks attach to them
    for x in (2, Wd - 3):
        b.set(x, fh - 2, 2, 'lantern[hanging=true]')
    # windows high up
    for x in (2, 3, Wd - 4, Wd - 3):
        b.set(x, 3, Dp - 1, 'white_stained_glass_pane')
    if roof == 'hip':
        hip_roof(b, 0, Wd - 1, 0, Dp - 1, fh, st['roof_stairs'], 1, full=st['roof_block'],
                 top=st['roof_slab'] + '[type=bottom]')
    else:
        flat_roof(b, 0, Wd - 1, 0, Dp - 1, fh, st['roof_block'], parapet=st.get('parapet'))
    # drinking fountain outside
    b.set(Wd - 1 + 1, 0, 1, 'air')
    return b


def info_booth(w, cx, cz, st, y=G + 1, title='GUEST INFO'):
    """Small 5x5 kiosk with counters on all sides and a hip roof."""
    b = Builder(w, cx - 2, y, cz - 2, 0)
    b.clear(-1, 0, -1, 5, 8, 5)
    b.fill(0, -1, 0, 4, -1, 4, st['floor'])
    for (x, z) in ((0, 0), (4, 0), (0, 4), (4, 4)):
        for yy in range(0, 4):
            b.set(x, yy, z, st['pillar'])
    for x in range(1, 4):
        for z in (0, 4):
            b.set(x, 0, z, st['counter'])
            b.set(x, 1, z, st['counter_top'])
    for z in range(1, 4):
        for x in (0, 4):
            b.set(x, 0, z, st['counter'])
            b.set(x, 1, z, st['counter_top'])
    b.set(0, 0, 2, 'air')
    b.set(0, 1, 2, 'air')  # staff entry gap on west side
    b.fill(0, 4, 0, 4, 4, 4, st['ceiling'])
    b.set(2, 0, 2, 'cartography_table')
    b.set(2, 1, 2, 'lectern[facing=north,has_book=false,powered=false]' if False else 'air')
    b.set(2, 3, 2, st['light'])
    hip_roof(b, 0, 4, 0, 4, 5, st['roof_stairs'], 1, full=st['roof_block'], top=st['roof_slab'] + '[type=bottom]')
    for d, (sx, sz) in (('north', (2, -1)), ('south', (2, 5)), ('east', (5, 2))):
        b.sign(sx, 3, sz, ['', title, 'MAPS & HELP', ''], wood=st['sign_wood'], facing=d)
    b.set(2, 9, 2, 'lightning_rod[facing=up]') if False else None
    w.label(cx, cz, 'i', 'info')
    return b


def food_kiosk(w, x0, z0, facing, title, colors=('red', 'white'), wood='oak', y=G + 1, items=None,
               subtitle='', wall='white_concrete'):
    """5 wide x 4 deep food stand with striped awning and service counter at the front."""
    Wd, Dp = 6, 5
    b = Builder.for_rect(w, x0, z0, Wd, Dp, facing, y)
    b.clear(-1, 0, -2, Wd, 7, Dp)
    b.fill(0, -1, 0, Wd - 1, -1, Dp - 1, f'{wood}_planks')
    box_walls(b, 0, Wd - 1, 0, Dp - 1, 0, 3, wall, f'stripped_{wood}_log[axis=y]' if wood not in ('bamboo', 'crimson', 'warped') else 'bamboo_block[axis=y]')
    b.fill(1, 0, 1, Wd - 2, 3, Dp - 2, 'air')
    # service window on the front
    for x in range(1, Wd - 1):
        b.set(x, 0, 0, f'{wood}_planks')
        b.set(x, 1, 0, f'{wood}_slab[type=bottom]')
        b.set(x, 2, 0, 'air')
    # roof
    for x in range(-1, Wd + 1):
        for z in range(-2, Dp + 1):
            c = colors[(x + 10) % len(colors)]
            b.set(x, 4, z, f'{c}_wool' if z >= 0 else 'air')
        c = colors[(x + 10) % len(colors)]
        b.set(x, 4, -1, f'{c}_wool')
        b.set(x, 3, -2, f'{c}_carpet' if False else f'{c}_wool')
    b.fill(0, 5, 0, Wd - 1, 5, Dp - 1, f'{wood}_slab[type=bottom]')
    # back equipment
    items = items or ['smoker[facing=north,lit=true]', 'barrel[facing=north]', 'cauldron', 'crafting_table']
    for i, x in enumerate(range(1, Wd - 1)):
        b.set(x, 0, Dp - 2, items[i % len(items)])
    b.set(2, 3, 2, 'lantern[hanging=true]')
    b.set(Wd - 2, 3, 2, 'lantern[hanging=true]')
    # door at back
    b.door(Wd - 2, 0, Dp - 1, 'south', wood if wood not in ('bamboo',) else 'bamboo')
    # big sign on top front
    b.sign(Wd // 2, 5, -1, ['', title, subtitle, ''], wood=wood if wood not in ('crimson',) else 'oak', facing='north',
           glow=True, color='yellow')
    b.sign(Wd // 2 - 1, 5, -1, ['', title, subtitle, ''], wood=wood if wood not in ('crimson',) else 'oak', facing='north',
           glow=True, color='yellow')
    # goodies on the counter
    b.set(1, 2, 0, 'air')
    b.set(Wd - 2, 2, 0, 'air')
    b.set(2, 2, 0, 'air')
    return b


def staff_door_sign(w, x, y, z, facing):
    w.sign(x, y, z, ['CAST MEMBERS', 'ONLY', 'No guest', 'access'], wood='spruce', facing=facing)
