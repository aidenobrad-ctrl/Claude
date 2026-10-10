import css from './ui/styles.css';
import { Game } from './game';
import { installDebugApi } from './debug';

const style = document.createElement('style');
style.textContent = css;
document.head.appendChild(style);

const params = new URLSearchParams(location.search);
const canvas = document.getElementById('view') as HTMLCanvasElement;
const ui = document.getElementById('ui') as HTMLElement;

function hasWebGL2(): boolean {
  try {
    return !!document.createElement('canvas').getContext('webgl2');
  } catch {
    return false;
  }
}

const boot = document.getElementById('boot');
if (!hasWebGL2()) {
  if (boot) {
    boot.classList.add('boot-error');
    boot.textContent = 'Halcyon Roads needs WebGL 2. Try an up-to-date Chrome, Safari, Firefox or Edge, and check that hardware acceleration is on.';
  }
} else {
  const game = new Game({
    canvas,
    ui,
    test: params.has('test'),
    seed: Number(params.get('seed') ?? 1) || 1,
    quality: (['low', 'medium', 'high', 'ultra'] as const).find((q) => q === params.get('q')),
    time: (['morning', 'noon', 'golden', 'sunset', 'night'] as const).find((t) => t === params.get('time')),
  });
  installDebugApi(game);
  game.start();
}
