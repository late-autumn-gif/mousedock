import { createPet } from './body.js';
import { selectPose, recoveryHeight } from './figure.js';
import { createWhaleMotion } from './whale-host.js';

const silent = { setAttribute() {} };
const effects = { innerHTML: '' };
const gestures = ['nod', 'shake', 'turn', 'wave', 'bow', 'spin', 'shiver', 'flap', 'dance', 'hop'];
let body, bounds, frame, allowRest, nextGesture, gestureIndex;
let carried, verticalOffset, recovering;
let faceData;
let whale;

export async function initialize(model) { whale = await createWhaleMotion(JSON.parse(model)); }

export function reset(width, height, floor, scale, start, rest) {
  bounds = { W: width, H: height, floorY: floor, S: scale };
  allowRest = rest;
  carried = false;
  recovering = false;
  effects.innerHTML = '';
  whale.reset();
  verticalOffset = 0;
  nextGesture = 20;
  gestureIndex = Math.floor(Math.random() * gestures.length);
  body = createPet({ petG: silent, shadowEl: silent, fxG: effects }, {
    bounds: () => bounds,
    startX: start,
    roam: 'off',
    sfx: {},
    onEvent(kind) {
      if (kind === 'mode' && body?.pet.mode === 'sit' && !allowRest) body.pet.willSleep = false;
    },
    figure: {
      extent: [8, 29, 248, 256],
      hits: [[128, 98, 64], [128, 174, 70], [128, 217, 34]],
      draw(_group, face, sample) { faceData = face; frame = sample; },
    },
  });
  body.holdRoam(4.8);
  body.render();
}

export function advance(seconds, automatic, blink, frequency, canTravel, speed) {
  // Keep running the upstream simulation even when autonomous choices are disabled.
  // Its stock idle choices also include walks, so reject stationary walks before drawing.
  const roaming = automatic ? 'free' : 'off';
  if (body.roam !== roaming) body.setRoam(roaming);
  const before = body.pet.mode;
  if (automatic && body.time >= nextGesture && before === 'idle' && !body.pressing &&
      !body.pet.pulse && body.time >= body.pet.exprUntil) {
    body.act(gestures[gestureIndex++ % gestures.length]);
    body.holdRoam(4);
    nextGesture = body.time + (22 + Math.random() * 14) / frequency;
  }
  // Bounded fixed substeps keep throws and springs stable after a slow UI frame.
  const nativeTiming = body.pressing || carried || recovering;
  seconds *= nativeTiming ? 1 : speed;
  while (seconds > 0) {
    const step = Math.min(seconds, 1 / 60);
    body.step(step);
    seconds -= step;
    if ((!canTravel || !automatic) && ['walk', 'run'].includes(body.pet.mode) && body.pet.walkId === 0)
      body.stopWalk(0);
  }
  if (automatic && before !== body.pet.mode && body.pet.mode === 'idle')
    body.holdRoam((4 + Math.random() * 3) / frequency);
  if (carried && ['land', 'dizzy'].includes(body.pet.mode)) recovering = true;
  if (recovering && body.pet.mode === 'idle' && body.time >= body.pet.exprUntil) recovering = false;
  carried = body.pet.mode === 'drag' || (carried && body.pet.mode === 'air');
  if (verticalOffset && !carried && !recovering) {
    bounds.floorY -= verticalOffset;
    verticalOffset = 0;
    body.resize();
  }
  body.render();
  const foot = body.toStage(128, 256);
  const { kx, ky, rot } = body.pet.xf;
  const nativeReaction = carried || recovering;
  const pose = selectPose(blink ? frame : { ...frame, blink: 0 }, body.pet.phase / Math.PI);
  const dust = nativeReaction ? Array.from(effects.innerHTML.matchAll(
    /class="p-dust"[^>]+cx="([^"]+)" cy="([^"]+)" r="([^"]+)" opacity="([^"]+)"/g),
    ([, x, y, r, opacity]) => ({ X: +x, Y: +y - bounds.floorY, Radius: +r, Opacity: +opacity })) : [];
  const rig = whale.draw(faceData, frame);
  const spin = frame.gesture?.kind === 'spin'
    ? 1 - .8 * Math.abs(Math.sin(2 * Math.PI * frame.gesture.k)) : 1;
  const glance = frame.mode === 'look' ? Math.max(-4, Math.min(4, frame.look[0] / 8)) : 0;
  return JSON.stringify({
    X: foot.x, Y: foot.y - bounds.floorY, Width: kx * spin / bounds.S,
    Height: ky * recoveryHeight(frame, pose) / bounds.S,
    Angle: rot + glance, Pose: pose, Mode: frame.mode, Gesture: frame.gesture?.kind, Carried: carried,
    NativeReaction: nativeReaction, Face: frame.face, ModeTime: frame.modeT, Time: frame.t, Dust: dust,
    Sprites: whale.sprites, Rig: rig,
  });
}

export function pointer(kind, x, y, time) {
  const point = { x, y: y + verticalOffset, t: time };
  switch (kind) {
    case 'down': return body.pointerDown(point);
    case 'move': body.pointerMove(point); break;
    case 'up':
      if (body.pet.mode === 'drag') {
        carried = true;
        // Make room for a high release and the maximum upward throw before the kit's ceiling.
        // Changing the vertical origin leaves the visible foot position and landing floor fixed.
        const extra = Math.max(0, 250 * bounds.S + 450 - body.toStage(128, 256).y);
        verticalOffset += extra;
        bounds.floorY += extra;
        body.pet.dy += extra;
        body.resize();
        body.render();
        point.y += extra;
      }
      body.pointerUp(point);
      break;
    case 'leave': body.pointerLeave(); break;
    case 'drop':
      if (body.pet.mode === 'drag') {
        carried = true;
        body.dropAt(point);
      }
      else body.pointerUp(point);
      body.pointerLeave();
      break;
  }
  return false;
}

export function act(word) { body.doWord(word); }
