declare module '*.css' {
  const text: string;
  export default text;
}
declare module '*.glsl' {
  const text: string;
  export default text;
}
/** A binary asset (a car model), embedded in the build. */
declare module '*.glb' {
  const data: Uint8Array;
  export default data;
}

/** True in `--dev` builds. Use for expensive assertions. */
declare const __DEV__: boolean;
declare const __BUILD_TIME__: string;
/** The tile worker's bundled source (empty outside the browser build). */
declare const __TILE_WORKER__: string;
/** The audio worker's bundled source (empty outside the browser build). */
declare const __AUDIO_WORKER__: string;
