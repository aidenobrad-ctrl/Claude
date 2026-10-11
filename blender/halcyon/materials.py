"""Car materials (Principled BSDF). Names start with Halcyon_ so the Unity
side can map each to its own URP material (car paint with clear coat etc.);
see unity/Assets/Halcyon/Scripts/Driving/CarRig.cs. Names in use:

Paint, Accent (second paint colour), Glass, Trim (satin black plastic),
Gloss (gloss black), Chrome, Grille (dark mesh / intake interior), Gap
(panel gaps and the walls of every opening), Cladding (textured black),
Carbon, LightFront, LightRear, LightRearBar, DRL, Plate, Rubber, Rim,
Brake, Caliper, Dark (inside the rims), Interior, Fabric (soft tops), Mirror.
"""
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


def principled(name, color, metallic=0.0, roughness=0.5, coat=0.0, coat_rough=0.03, emission=None, strength=0.0, specular=0.5, transmission=0.0, alpha=1.0):
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
    if transmission:
        i = _inp(b, 'Transmission Weight', 'Transmission')
        if i:
            i.default_value = transmission
    if alpha < 1:
        b.inputs['Alpha'].default_value = alpha
    m.diffuse_color = color
    return m


def car_materials(spec):
    paint = srgb(spec['paint'])
    accent = srgb(spec.get('accent', 0x111111))
    met = spec.get('metallic', 0.55)
    rough = spec.get('paint_rough', 0.32)
    return {
        'Paint': principled('Halcyon_Paint', paint, metallic=met, roughness=rough, coat=1.0, coat_rough=0.02),
        'Accent': principled('Halcyon_Accent', accent, metallic=spec.get('accent_metallic', 0.3), roughness=0.3, coat=1.0, coat_rough=0.02),
        'Glass': principled('Halcyon_Glass', (0.10, 0.12, 0.13, 1), roughness=0.0, specular=0.6, transmission=1.0),
        'Trim': principled('Halcyon_Trim', (0.018, 0.019, 0.021, 1), roughness=0.55),
        'Gloss': principled('Halcyon_Gloss', (0.006, 0.006, 0.007, 1), roughness=0.08, coat=1.0, coat_rough=0.01),
        'Chrome': principled('Halcyon_Chrome', (0.85, 0.86, 0.88, 1), metallic=1.0, roughness=0.05),
        'Grille': principled('Halcyon_Grille', (0.012, 0.012, 0.013, 1), roughness=0.6),
        'Gap': principled('Halcyon_Gap', (0.004, 0.004, 0.004, 1), roughness=0.8),
        'Cladding': principled('Halcyon_Cladding', (0.03, 0.03, 0.032, 1), roughness=0.8),
        'Carbon': principled('Halcyon_Carbon', (0.02, 0.021, 0.024, 1), metallic=0.2, roughness=0.25, coat=1.0, coat_rough=0.02),
        'LightFront': principled('Halcyon_LightFront', (0.75, 0.78, 0.82, 1), metallic=0.8, roughness=0.12, emission=(0.85, 0.9, 1.0, 1), strength=1.2),
        'DRL': principled('Halcyon_DRL', (0.95, 0.97, 1.0, 1), roughness=0.2, emission=(0.9, 0.95, 1.0, 1), strength=12.0),
        'LightRear': principled('Halcyon_LightRear', (0.16, 0.004, 0.004, 1), roughness=0.06, emission=(1.0, 0.02, 0.01, 1), strength=0.7),
        'LightRearBar': principled('Halcyon_LightRearBar', (0.6, 0.02, 0.02, 1), roughness=0.1, emission=(1.0, 0.03, 0.02, 1), strength=6.0),
        'Plate': principled('Halcyon_Plate', (0.82, 0.82, 0.78, 1), roughness=0.35),
        'Rubber': principled('Halcyon_Rubber', (0.022, 0.022, 0.024, 1), roughness=0.88, specular=0.3),
        'Rim': principled('Halcyon_Rim', srgb(spec.get('rim', 0x9a9fa6)), metallic=spec.get('rim_metallic', 1.0), roughness=spec.get('rim_rough', 0.24)),
        'Brake': principled('Halcyon_Brake', (0.22, 0.22, 0.23, 1), metallic=1.0, roughness=0.42),
        'Caliper': principled('Halcyon_Caliper', srgb(spec.get('caliper', 0xd92b1c)), metallic=0.1, roughness=0.35, coat=0.6),
        'Dark': principled('Halcyon_Dark', (0.01, 0.01, 0.011, 1), roughness=0.7),
        'Interior': principled('Halcyon_Interior', srgb(spec.get('interior', 0x1a1a1c)), roughness=0.7),
        'Fabric': principled('Halcyon_Fabric', (0.02, 0.02, 0.022, 1), roughness=0.95, specular=0.2),
        'Mirror': principled('Halcyon_Mirror', (0.9, 0.92, 0.94, 1), metallic=1.0, roughness=0.02),
    }
