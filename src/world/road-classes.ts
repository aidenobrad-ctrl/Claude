// Road classes: geometry, grades and surfaces.
import { SURFACE, type SurfaceId } from './surfaces';

export interface RoadClass {
  name: string;
  /** Half the paved width, m. */
  halfWidth: number;
  /** Maximum grade (rise / run). */
  maxGrade: number;
  /** Height smoothing window, m. */
  smooth: number;
  surface: SurfaceId;
  lanes: number;
  /** Lane markings: center line style. */
  center: 'none' | 'dashed' | 'double' | 'solid';
  edgeLines: boolean;
  /** Speed limit for ambient traffic, m/s. */
  traffic: number;
}

export const ROAD_CLASSES = {
  highway: { name: 'Highway', halfWidth: 8, maxGrade: 0.06, smooth: 140, surface: SURFACE.asphalt, lanes: 4, center: 'double', edgeLines: true, traffic: 30 },
  main: { name: 'Main road', halfWidth: 4.8, maxGrade: 0.09, smooth: 70, surface: SURFACE.asphalt, lanes: 2, center: 'dashed', edgeLines: true, traffic: 22 },
  pass: { name: 'Mountain pass', halfWidth: 4.2, maxGrade: 0.12, smooth: 44, surface: SURFACE.asphalt, lanes: 2, center: 'solid', edgeLines: true, traffic: 16 },
  street: { name: 'Street', halfWidth: 5.5, maxGrade: 0.08, smooth: 40, surface: SURFACE.asphalt, lanes: 2, center: 'dashed', edgeLines: false, traffic: 14 },
  dirt: { name: 'Dirt track', halfWidth: 3.2, maxGrade: 0.16, smooth: 28, surface: SURFACE.dirt, lanes: 1, center: 'none', edgeLines: false, traffic: 12 },
} satisfies Record<string, RoadClass>;

export type RoadClassId = keyof typeof ROAD_CLASSES;

/** Gravel shoulder beyond the paved half width; on bridges it is deck. */
export const ROAD_SHOULDER = 1.3;
/** Bridge parapet width, m. */
export const PARAPET_W = 0.32;
