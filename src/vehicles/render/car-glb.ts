// The cars' Blender models in the web build: the same cars as the Unity
// build (blender/build_car.py), one LOD each, packed by tools/pack-cars.mjs
// and embedded. A model is decoded once and cloned into each car view, where
// it replaces the parametric body: the body hangs under the view's root and
// the wheels and callipers move into the view's steering and spinning groups,
// so CarView poses them from the physics as before.
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/examples/jsm/libs/meshopt_decoder.module.js';
import { CAR_MODELS } from '../models';
import type { CarModel } from './car-model';
import { carMaterials } from './car-style';
import type { VehicleParams } from '../params';

const cache = new Map<string, Promise<THREE.Object3D | null>>();
let loader: GLTFLoader | null = null;

/** Whether the build has a Blender model of this car. */
export function hasCarModel(id: string): boolean {
  return Object.prototype.hasOwnProperty.call(CAR_MODELS, id);
}

/** The car's model, decoded once and shared (clone it); null when the build has none or it failed to load. */
export function loadCarModel(id: string): Promise<THREE.Object3D | null> {
  const known = cache.get(id);
  if (known) return known;
  const entry = hasCarModel(id) ? CAR_MODELS[id] : null;
  const p = entry
    ? (async (): Promise<THREE.Object3D | null> => {
        await MeshoptDecoder.ready;
        loader ??= new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
        const bytes = entry.glb;
        const buf = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
        const gltf = await loader.parseAsync(buf, '');
        return gltf.scene;
      })().catch((e: unknown) => {
        console.warn(`Halcyon: the ${id} model did not load; the drawn body stays`, e);
        return null;
      })
    : Promise.resolve(null);
  cache.set(id, p);
  return p;
}

/**
 * Dress a car view in its Blender model. The model faces +z with its left at
 * +x and its origin on the ground at mid-wheelbase; the view faces -z with
 * its left at -x and the centre of mass at its origin.
 */
export function applyCarModel(model: CarModel, source: THREE.Object3D, specId: string, params: VehicleParams, paint?: number): void {
  const entry = CAR_MODELS[specId];
  const glb = source.clone(true);
  const mats = carMaterials(entry.style, model, paint);
  glb.traverse((o) => {
    const mesh = o as THREE.Mesh;
    if (!mesh.isMesh) return;
    const pick = (m: THREE.Material): THREE.Material => mats(m.name);
    mesh.material = Array.isArray(mesh.material) ? mesh.material.map(pick) : pick(mesh.material);
    const list = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
    // Glass casts no shadow (the cabin would go dark); everything else does.
    mesh.castShadow = !list.every((m) => m.transparent);
    mesh.receiveShadow = true;
  });
  const holder = new THREE.Group();
  holder.name = 'blender-model';
  holder.rotation.y = Math.PI;
  holder.position.set(0, model.groundY, (params.b - params.a) / 2);
  holder.add(glb);
  holder.updateMatrix();
  /** A node's transform in the view's root frame (whatever the car's pose). */
  const rel = (o: THREE.Object3D): THREE.Matrix4 => {
    const chain: THREE.Object3D[] = [];
    for (let p: THREE.Object3D | null = o; p && p !== holder; p = p.parent) chain.push(p);
    const m = holder.matrix.clone();
    for (let i = chain.length - 1; i >= 0; i--) {
      chain[i].updateMatrix();
      m.multiply(chain[i].matrix);
    }
    return m;
  };
  const toHub = new THREE.Matrix4();
  const move = (o: THREE.Object3D, into: THREE.Object3D): void => {
    const m = new THREE.Matrix4().multiplyMatrices(toHub, rel(o));
    o.removeFromParent();
    m.decompose(o.position, o.quaternion, o.scale);
    into.add(o);
  };
  for (let i = 0; i < 4; i++) {
    const wp = params.wheels[i];
    const tag = (wp.front ? 'F' : 'R') + (wp.left ? 'L' : 'R');
    const wheel = glb.getObjectByName(`Wheel_${tag}`);
    if (!wheel) continue;
    const parts = model.wheels[i];
    const hub = new THREE.Vector3().setFromMatrixPosition(rel(wheel));
    toHub.makeTranslation(-hub.x, -hub.y, -hub.z);
    // The model's wheel and calliper take the places of the drawn ones.
    for (const c of parts.spin.children) c.visible = false;
    for (const c of parts.pivot.children) if (c !== parts.spin) c.visible = false;
    for (const c of [...wheel.children]) move(c, parts.spin);
    const caliper = glb.getObjectByName(`Caliper_${tag}`);
    if (caliper) move(caliper, parts.pivot);
  }
  model.body.visible = false;
  model.root.add(holder);
}
