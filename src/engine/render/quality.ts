// Graphics quality presets. Auto picks one from the device; dynamic
// resolution then scales within the preset's pixel budget.

export type QualityName = 'low' | 'medium' | 'high' | 'ultra';

export interface Quality {
  name: QualityName;
  /** Max internal render pixels. */
  pixelBudget: number;
  /** Post-processing pipeline (bloom, grade, motion blur). */
  post: boolean;
  bloom: boolean;
  /** MSAA samples for the HDR scene target (0 = none). */
  msaa: number;
  /** Ground-truth ambient occlusion pass. */
  ao: boolean;
  /** Cascaded shadow maps (else one shadow map around the car). */
  csm: boolean;
  shadowSize: number;
  cascades: number;
  /** Real-time reflections on the player's car (cube camera), every N frames, 0 = off. */
  carReflections: number;
  /** Grass blades around the camera. */
  grass: number;
  /** Scenery draw distance multiplier. */
  scenery: number;
  /** Terrain LOD distance multiplier. */
  terrain: number;
  clouds: boolean;
  /** Max particles. */
  particles: number;
}

export const QUALITY: Record<QualityName, Quality> = {
  low: { name: 'low', pixelBudget: 960 * 540, post: false, bloom: false, msaa: 0, ao: false, csm: false, shadowSize: 1024, cascades: 1, carReflections: 0, grass: 0, scenery: 0.5, terrain: 0.7, clouds: false, particles: 250 },
  medium: { name: 'medium', pixelBudget: 1280 * 720, post: true, bloom: true, msaa: 0, ao: false, csm: false, shadowSize: 2048, cascades: 1, carReflections: 0, grass: 0.35, scenery: 0.75, terrain: 0.85, clouds: true, particles: 400 },
  high: { name: 'high', pixelBudget: 1920 * 1080, post: true, bloom: true, msaa: 4, ao: false, csm: true, shadowSize: 2048, cascades: 3, carReflections: 6, grass: 0.75, scenery: 1, terrain: 1, clouds: true, particles: 700 },
  ultra: { name: 'ultra', pixelBudget: 2560 * 1440, post: true, bloom: true, msaa: 4, ao: true, csm: true, shadowSize: 4096, cascades: 4, carReflections: 2, grass: 1, scenery: 1.3, terrain: 1.25, clouds: true, particles: 1000 },
};

export function autoQuality(mobile: boolean, cores: number, memoryGb: number | undefined): QualityName {
  if (mobile) return cores >= 8 && (memoryGb ?? 4) >= 6 ? 'medium' : 'low';
  if (cores <= 4) return 'medium';
  return 'high';
}
