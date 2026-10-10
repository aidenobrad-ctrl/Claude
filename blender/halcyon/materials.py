"""Car materials (Principled BSDF). Names start with Halcyon_ so the Unity
side can map each to its own URP material (car paint with clear coat etc.)."""
import bpy


def srgb(hex_color):
    def lin(c):
        c = c / 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    return (lin((hex_color >> 16) & 255), lin((hex_color >> 8) & 255), lin(hex_color & 255), 1.0)


def _inp(bsdf, *names):
    for n in names:
        if n in bsdf.inputs:
            return bsdf.inputs[n]
    return None


def principled(name, color, metallic=0.0, roughness=0.5, coat=0.0, coat_rough=0.03, emission=None, strength=0.0, specular=0.5):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = color
    b.inputs['Metallic'].default_value = metallic
    b.inputs['Roughness'].default_value = roughness
    if coat:
        i = _inp(b, 'Coat Weight', 'Clearcoat')
        if i:
            i.default_value = coat
        i = _inp(b, 'Coat Roughness', 'Clearcoat Roughness')
        if i:
            i.default_value = coat_rough
    if emission is not None:
        i = _inp(b, 'Emission Color', 'Emission')
        if i:
            i.default_value = emission
        i = _inp(b, 'Emission Strength')
        if i:
            i.default_value = strength
    i = _inp(b, 'Specular IOR Level', 'Specular')
    if i:
        i.default_value = specular
    m.diffuse_color = color
    return m


def car_materials(spec):
    paint = srgb(spec['paint'])
    return {
        'Paint': principled('Halcyon_Paint', paint, metallic=spec.get('metallic', 0.55), roughness=0.32, coat=1.0, coat_rough=0.02),
        'Glass': principled('Halcyon_Glass', (0.012, 0.016, 0.02, 1), metallic=0.0, roughness=0.02, coat=1.0, coat_rough=0.0, specular=0.6),
        'Trim': principled('Halcyon_Trim', (0.018, 0.019, 0.021, 1), roughness=0.45),
        'LightFront': principled('Halcyon_LightFront', (0.9, 0.92, 0.95, 1), roughness=0.05, emission=(0.85, 0.9, 1.0, 1), strength=2.5),
        'LightRear': principled('Halcyon_LightRear', (0.16, 0.004, 0.004, 1), roughness=0.06, emission=(1.0, 0.02, 0.01, 1), strength=0.7),
        'Rubber': principled('Halcyon_Rubber', (0.022, 0.022, 0.024, 1), roughness=0.88, specular=0.3),
        'Rim': principled('Halcyon_Rim', srgb(spec.get('rim', 0x9a9fa6)), metallic=1.0, roughness=0.24),
        'Brake': principled('Halcyon_Brake', (0.22, 0.22, 0.23, 1), metallic=1.0, roughness=0.42),
        'Caliper': principled('Halcyon_Caliper', srgb(spec.get('caliper', 0xd92b1c)), metallic=0.1, roughness=0.35, coat=0.6),
        'Chrome': principled('Halcyon_Chrome', (0.85, 0.86, 0.88, 1), metallic=1.0, roughness=0.06),
    }
