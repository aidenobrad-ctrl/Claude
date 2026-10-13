// A car's colours and finishes (blender/build_car.py writes them for Unity's
// Resources/Cars/<id>.style.json; tools/pack-cars.mjs copies them into the
// web build), and the game's materials for the Blender models' material
// slots, chosen by name as the Unity build does (CarVisual.AssignMaterials).
import * as THREE from 'three';
import type { CarModel } from './car-model';

export interface CarStyle {
  paint: string;
  accent: string;
  rim: string;
  caliper: string;
  interior: string;
  metallic: number;
  paintRoughness: number;
  accentMetallic: number;
  rimMetallic: number;
  rimRoughness: number;
}

/**
 * The materials for one car view, by the Blender slot names (Halcyon_Paint,
 * Halcyon_Glass...). Paint and the lights are the view's own, so CarView can
 * repaint the car and switch its lights; the rest are made for this view.
 */
export function carMaterials(style: CarStyle, model: CarModel, paint?: number): (name: string) => THREE.Material {
  const P = model.paint;
  P.color.set(paint ?? style.paint);
  // Metallic paint under a clear coat; the base is never fully metal (it reads as chrome).
  P.metalness = Math.min(0.7, style.metallic);
  P.roughness = style.paintRoughness;
  P.clearcoat = 1;
  P.clearcoatRoughness = 0.03;
  const std = (color: THREE.ColorRepresentation, roughness: number, metalness = 0): THREE.MeshStandardMaterial => new THREE.MeshStandardMaterial({ color, roughness, metalness });
  const coat = (color: THREE.ColorRepresentation, roughness: number, metalness = 0): THREE.MeshPhysicalMaterial => new THREE.MeshPhysicalMaterial({ color, roughness, metalness, clearcoat: 1, clearcoatRoughness: 0.03 });
  const accent = coat(style.accent, 0.3, style.accentMetallic);
  // Tinted glass: the cabin shows through, the sky reflects off the coat.
  const glass = new THREE.MeshPhysicalMaterial({ color: 0x0b1116, roughness: 0.04, metalness: 0, clearcoat: 1, clearcoatRoughness: 0.02, transparent: true, opacity: 0.68, depthWrite: false, envMapIntensity: 1.5 });
  const gloss = coat(0x050506, 0.08);
  const chrome = std(0xdde1e6, 0.06, 1);
  const grille = std(0x0b0b0c, 0.6);
  const gap = std(0x030303, 0.85);
  const cladding = std(0x18181a, 0.82);
  const carbon = coat(0x101114, 0.25, 0.2);
  const drl = new THREE.MeshStandardMaterial({ color: 0xf2f6ff, emissive: 0xe6eeff, emissiveIntensity: 2.2, roughness: 0.2 });
  const plate = std(0xd6d6ce, 0.4);
  const trim = std(0x101113, 0.5, 0.2);
  const rubber = std(0x101011, 0.88);
  const rim = std(style.rim, style.rimRoughness, style.rimMetallic);
  const brake = std(0x5d5f63, 0.42, 1);
  const caliper = coat(style.caliper, 0.35, 0.1);
  const dark = std(0x060607, 0.7);
  const interior = std(style.interior, 0.7);
  const fabric = std(0x0c0c0d, 0.95);
  const mirror = std(0xeaedf1, 0.02, 1);
  const byName: [string, THREE.Material][] = [
    ['Paint', P],
    ['Accent', accent],
    ['Glass', glass],
    ['LightFront', model.headLights],
    ['DRL', drl],
    ['LightRearBar', model.brakeLights],
    ['LightRear', model.brakeLights],
    ['Gloss', gloss],
    ['Chrome', chrome],
    ['Grille', grille],
    ['Gap', gap],
    ['Cladding', cladding],
    ['Carbon', carbon],
    ['Plate', plate],
    ['Trim', trim],
    ['Rubber', rubber],
    ['Rim', rim],
    ['Brake', brake],
    ['Caliper', caliper],
    ['Dark', dark],
    ['Interior', interior],
    ['Fabric', fabric],
    ['Mirror', mirror],
  ];
  return (name) => {
    for (const [k, m] of byName) if (name.includes(k)) return m;
    return trim;
  };
}
