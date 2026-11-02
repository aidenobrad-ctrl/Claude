"""Park-wide constants and layout."""
G = 6                      # y of the ground surface layer (grass / paths). Guests stand at G+1.
W, H, D = 640, 128, 720    # x (west->east), y, z (north->south)
CELL = 200
X0, Z0 = 20, 20

HUB = (320, 320)

# Land rectangles (x0, z0, x1, z1) inclusive-exclusive
LANDS = {
    'western': (20, 20, 220, 220),
    'medieval': (220, 20, 420, 220),
    'scifi_n': (420, 20, 620, 220),
    'pirate': (20, 220, 220, 420),
    'hub': (220, 220, 420, 420),
    'scifi_s': (420, 220, 620, 420),
    'tropical': (20, 420, 220, 620),
    'mainstreet': (220, 420, 420, 620),
    'carnival': (420, 420, 620, 620),
    'entrance': (0, 620, 640, 720),
}

LAND_NAMES = {
    'western': 'Dusty Gulch (Wild West)',
    'medieval': 'Everspire Kingdom (Medieval Fantasy)',
    'scifi_n': 'Nova Frontier (Sci-Fi)',
    'scifi_s': 'Nova Frontier (Sci-Fi)',
    'pirate': 'Blackwater Cove (Pirates)',
    'hub': 'Starlight Plaza (Central Hub)',
    'tropical': 'Coral Cay (Tropical Adventure Island)',
    'mainstreet': 'Grand Avenue (Main Street)',
    'carnival': 'Jollyville Carnival (Cartoon)',
    'entrance': 'Grand Entrance',
}


def land_at(x, z):
    for k, (x0, z0, x1, z1) in LANDS.items():
        if x0 <= x < x1 and z0 <= z < z1:
            return k
    return None
