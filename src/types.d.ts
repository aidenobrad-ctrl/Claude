declare module '*.css' {
  const text: string;
  export default text;
}
declare module '*.glsl' {
  const text: string;
  export default text;
}

/** True in `--dev` builds. Use for expensive assertions. */
declare const __DEV__: boolean;
declare const __BUILD_TIME__: string;
/** The tile worker's bundled source (empty outside the browser build). */
declare const __TILE_WORKER__: string;
