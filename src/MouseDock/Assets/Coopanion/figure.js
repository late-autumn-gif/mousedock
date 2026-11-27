const svg = "http://www.w3.org/2000/svg";
const pose = {
  stand: 0, blink: 1, greet: 2, lifted: 3, sit: 4, sleep: 5,
  stride: 6, look: 8, pat: 9, startle: 10,
};

const expressions = {
  happy: pose.greet, love: pose.pat, shy: pose.pat, flustered: pose.pat,
  surprised: pose.startle, scared: pose.startle, confused: pose.startle,
  dizzy: pose.startle, waking: pose.look, listening: pose.look,
  smug: pose.look, pout: pose.look, angry: pose.look, thinking: pose.look,
  worried: pose.look, determined: pose.look, sad: pose.blink, cry: pose.blink,
  sleepy: pose.blink, squeeze: pose.blink, excited: pose.greet,
};

// The shipped Coopanion kit drives timing, input, grounding, throws, gaze and effects.
// This adapter selects the artwork for each phase and adds the sprite's local accents.
export function selectPose(frame, stridePhase = null) {
  const { mode, modeT, face, gesture } = frame;
  const blink = frame.blink > .4 || frame.eyeClose > .55 || face === "wink";
  if (mode === "drag" || (mode === "air" && face !== "surprised")) return pose.lifted;
  if (mode === "air") return pose.startle;
  if (mode === "walk" || mode === "run") {
    const rate = mode === "run" ? 11 : 7;
    return pose.stride + Math.floor(stridePhase ?? modeT * rate) % 2;
  }
  if (mode === "sleep") return pose.sleep;
  if (mode === "wake") return face === "surprised" && modeT < .42 ? pose.startle : pose.look;
  if (mode === "crouch") return pose.look;
  if (mode === "land") return expressions[face] ?? pose.look;
  if (mode === "dizzy") {
    if (face === "dizzy") return pose.sit;
    return frame.sit >= .5 ? pose.sleep : pose.blink;
  }
  if (mode === "dance") return Math.floor(modeT * 6) % 4 === 0 ? pose.stride : pose.greet;
  if (gesture?.kind === "wave" || gesture?.kind === "flap") return pose.greet;
  if (gesture?.kind === "bow") return pose.blink;
  if (gesture?.kind === "nod" || gesture?.kind === "shake") return pose.look;
  if (mode === "sit" || frame.sit > .55) {
    return blink || ["love", "shy", "sleepy"].includes(face) ? pose.sleep : pose.sit;
  }
  if (blink) return pose.blink;
  return expressions[face] ?? (mode === "look" ? pose.look : pose.stand);
}

export function recoveryHeight(frame, cell) {
  if (frame.mode !== "dizzy") return 1;
  const seated = cell === pose.sit || cell === pose.sleep;
  // Match the apparent height when switching atlases. The kit drives the rise through frame.sit.
  return Math.pow(236 / 282, frame.sit - (seated ? 1 : 0));
}

export async function createMouseDockBody(_base, { asset, loadImage, kit, host }) {
  const url = asset("poses.png");
  const atlas = await loadImage(url);
  if (atlas.naturalWidth !== 1280 || atlas.naturalHeight !== 960) {
    throw new Error("MouseDock pose atlas must be 1280 × 960 pixels.");
  }
  const doc = host.root.ownerDocument;
  const viewport = doc.createElementNS(svg, "svg");
  viewport.setAttribute("width", "256");
  viewport.setAttribute("height", "256");
  viewport.setAttribute("y", "13.6");
  viewport.setAttribute("overflow", "hidden");
  const image = doc.createElementNS(svg, "image");
  image.setAttribute("href", String(url));
  image.setAttribute("width", "1280");
  image.setAttribute("height", "960");
  viewport.appendChild(image);

  let current = -1;
  let previousTransform = "";
  const reverseStride = url.pathname.includes("/mousedock-rikka/");
  const reducedMotion = globalThis.matchMedia?.("(prefers-reduced-motion: reduce)");
  const figure = {
    extent: [8, 29, 248, 256],
    hits: [[128, 98, 64], [128, 174, 70], [128, 217, 34]],
    draw(group, _face, frame) {
      if (viewport.parentNode !== group) group.appendChild(viewport);
      const next = selectPose(frame);
      if (next !== current) {
        current = next;
        viewport.setAttribute("viewBox", (next % 4 * 320) + " " + (Math.floor(next / 4) * 320) + " 320 320");
      }

      const stride = next === pose.stride || next === pose.stride + 1;
      let width = stride && reverseStride ? -1 : 1;
      const height = reducedMotion?.matches ? 1 : recoveryHeight(frame, next);
      let angle = 0;
      if (!reducedMotion?.matches) {
        const gesture = frame.gesture;
        if (gesture?.kind === "spin") width *= 1 - .8 * Math.abs(Math.sin(2 * Math.PI * gesture.k));
        if (frame.mode === "look") angle = Math.max(-4, Math.min(4, (frame.look?.[0] ?? 0) / 8));
        if (frame.face === "shy") angle -= 3;
        if (frame.face === "confused") angle += 5;
      }
      const transform = width === 1 && height === 1 && angle === 0 ? "" :
        `translate(128 256) rotate(${angle.toFixed(1)}) scale(${width.toFixed(2)} ${height.toFixed(3)}) translate(-128 -256)`;
      if (transform !== previousTransform) {
        previousTransform = transform;
        viewport.setAttribute("transform", transform);
      }
    },
  };
  return kit.createBody(host, { figure });
}
