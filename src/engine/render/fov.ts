/**
 * Vertical FOV that keeps at least `minHorizontal` degrees across the screen,
 * so tall phone screens in portrait still see the road around the car.
 */
export function fovForAspect(vertical: number, minHorizontal: number, aspect: number): number {
  const hFromV = (2 * Math.atan(Math.tan((vertical * Math.PI) / 360) * aspect) * 180) / Math.PI;
  if (hFromV >= minHorizontal) return vertical;
  const v = (2 * Math.atan(Math.tan((minHorizontal * Math.PI) / 360) / aspect) * 180) / Math.PI;
  return Math.min(v, 100);
}
