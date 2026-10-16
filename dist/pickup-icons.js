// Shared pickup silhouettes for the radar and the local peer map.
export const PICKUP_COLORS = Object.freeze({
  ammo: '#4ddcff', nitro: '#64ff83', rocket: '#ff9c4d',
});

export const BOLT_POINTS = [
  [-.1, 1.65], [.56, 1.65], [.08, .83], [.48, .83],
  [-.4, .05], [-.16, .68], [-.57, .68],
];
export const BOLT_TRIANGLES = [
  [6, 0, 1], [6, 1, 2], [6, 2, 3], [3, 4, 5], [3, 5, 6],
];

export function drawPickupIcon(ctx, type, x, y, size = 20) {
  const color = PICKUP_COLORS[type];
  if (!color) return;
  ctx.save();
  ctx.translate(x, y);
  ctx.scale(size / 20, size / 20);
  ctx.fillStyle = '#06131b';
  ctx.strokeStyle = color;
  ctx.lineWidth = 1.4;
  ctx.beginPath();
  ctx.arc(0, 0, 10, 0, Math.PI * 2);
  ctx.fill();
  ctx.stroke();
  ctx.fillStyle = color;
  ctx.strokeStyle = '#06131b';
  ctx.lineWidth = 1.1;
  ctx.lineJoin = 'round';
  if (type === 'ammo') {
    for (const offset of [-5, 0, 5]) {
      ctx.beginPath();
      ctx.moveTo(offset - 1.8, 6);
      ctx.lineTo(offset + 1.8, 6);
      ctx.lineTo(offset + 1.8, -3);
      ctx.lineTo(offset + 1.1, -6);
      ctx.lineTo(offset, -8);
      ctx.lineTo(offset - 1.1, -6);
      ctx.lineTo(offset - 1.8, -3);
      ctx.closePath();
      ctx.fill();
      ctx.stroke();
    }
  } else if (type === 'nitro') {
    ctx.beginPath();
    BOLT_POINTS.forEach(([px, py], index) => {
      const vx = px * 14, vy = (py - .85) * -11;
      if (index) ctx.lineTo(vx, vy);
      else ctx.moveTo(vx, vy);
    });
    ctx.closePath();
    ctx.fill();
    ctx.stroke();
  } else {
    ctx.beginPath();
    ctx.moveTo(0, -9);
    ctx.lineTo(3, -4);
    ctx.lineTo(3, 4);
    ctx.lineTo(6, 7);
    ctx.lineTo(6, 9);
    ctx.lineTo(2, 7);
    ctx.lineTo(-2, 7);
    ctx.lineTo(-6, 9);
    ctx.lineTo(-6, 7);
    ctx.lineTo(-3, 4);
    ctx.lineTo(-3, -4);
    ctx.closePath();
    ctx.fill();
    ctx.stroke();
    ctx.fillStyle = '#ffe6ab';
    ctx.beginPath();
    ctx.arc(0, -2, 1.5, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();
}
