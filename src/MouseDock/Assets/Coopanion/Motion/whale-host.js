import { createWhaleFigure } from './whale-figure.js';

// WPF owns drawing. These sinks let the original figure advance its springs,
// emit its texture operations and submit its deformer state without a browser.
const noop = () => {};
function element() {
  const children = new Set();
  return {
    style: {},
    setAttribute: noop,
    contains: child => children.has(child),
    append: (...nodes) => nodes.forEach(child => children.add(child)),
    appendChild: child => children.add(child),
    getScreenCTM: () => null,
  };
}

export async function createWhaleMotion(model) {
  let rigFrame, faceSprites = [];
  function canvas() {
    let angle = 0, scale = 1;
    const stack = [], sprites = [];
    const context = new Proxy({
      save() { stack.push([angle, scale]); },
      restore() { [angle, scale] = stack.pop(); },
      setTransform() { angle = 0; scale = 1; },
      clearRect() { sprites.length = 0; },
      rotate(value) { angle += value * 180 / Math.PI; },
      scale(value) { scale *= value; },
      createRadialGradient() { return { addColorStop: noop }; },
      drawImage(image) {
        if (image?.name && /^(dizzy|drag)_(eye[LR]|mouth)$/.test(image.name))
          sprites.push({ Name: image.name, Angle: angle, Scale: scale });
      },
    }, { get(target, key) { return target[key] ?? noop; } });
    return { ...element(), getContext: () => context, sprites, width: 0, height: 0 };
  }
  const create = tag => tag === 'canvas' ? canvas() : element();
  globalThis.document = { createElement: create, createElementNS: (_ns, tag) => create(tag) };
  const group = element();
  const figure = await createWhaleFigure(null, {
    model, scheme: 'deepseek-maid', asset: path => path,
    loadImage: path => ({ name: path.split('/').pop().replace('.png', '') }),
    kit: {
      createRig: () => ({
        upload(name, texture) { if (name === 'faceFx') faceSprites = texture.sprites; },
        render(state) { rigFrame = state; },
        point(_part, _state, x, y) { return [x, y]; },
        dispose: noop,
      }),
    },
  });
  return {
    reset() { figure.reset(); },
    draw(face, frame) {
      figure.draw(group, face, frame);
      const channel = (name, key = 'a') => rigFrame[name]?.[key] ?? 0;
      const leg = name => {
        const [x, y] = model.pivots[name], length = 48;
        const [dx, dy] = rigFrame[name].fn(.5, 1, x, y + length);
        return Math.atan2(-dx, length + dy) * 180 / Math.PI;
      };
      const [neckX, neckY] = model.pivots.neck;
      const [hairX] = rigFrame.hairSway.fn(.5, .85, neckX, neckY + 85);
      const [skirtX, skirtY] = rigFrame.skirt.fn(.5, .85);
      return {
        Neck: channel('neck'), NeckY: channel('neck', 'ty'), ArmNear: channel('armNear'),
        ArmFar: channel('armFar'), Tail: channel('tail'), Waist: channel('waist'),
        LegBack: leg('legBack'), LegFront: leg('legFront'), HairX: hairX,
        SkirtX: skirtX, SkirtY: skirtY,
      };
    },
    get sprites() { return faceSprites; },
  };
}
