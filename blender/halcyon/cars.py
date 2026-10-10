"""The car roster's looks: dimensions match src/vehicles/cars.ts (the physics),
styling is free. Fictional makes and models only."""
from .carbody import CarShape

CARS = {
    'halden-aster-gt': {
        'name': 'Halden Aster GT',
        'paint': 0xC7262E,
        'metallic': 0.55,
        'rim': 0x6E737A,
        'caliper': 0xF2B01E,
        'spokes': 10,
        'exhausts': 4,
        'shape': CarShape(
            length=4.38, width=1.86, height=1.30, wheelbase=2.47, track_f=1.59, track_r=1.60,
            tire_r=0.335, tire_w_f=0.255, tire_w_r=0.275, clearance=0.12,
            front_overhang=0.88,
        ),
    },
}
