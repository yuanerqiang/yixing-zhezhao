const canvas = document.getElementById("mask");
const ctx = canvas.getContext("2d");
const paper = document.getElementById("paper");
const scrollBox = document.getElementById("scroll");

const statusText = document.getElementById("statusText");
const swatch = document.getElementById("swatch");
const widthInput = document.getElementById("brushWidth");
const widthValue = document.getElementById("widthValue");

const toolBrush = document.getElementById("toolBrush");
const toolErase = document.getElementById("toolErase");
const toolSample = document.getElementById("toolSample");
const toolRead = document.getElementById("toolRead");
const undoButton = document.getElementById("undo");
const redoButton = document.getElementById("redo");
const clearButton = document.getElementById("clear");

const toolButtons = [toolBrush, toolErase, toolSample, toolRead];
const dpr = Math.min(window.devicePixelRatio || 1, 2);

let mode = "read";
let brushColor = "#f7f7f8";
let brushWidth = 14;
let strokes = [];
let history = [];
let undone = [];
let liveStroke = null;

function syncToolbar() {
  toolButtons.forEach((button) => {
    const active =
      (button === toolBrush && mode === "brush") ||
      (button === toolErase && mode === "erase") ||
      (button === toolSample && mode === "sample") ||
      (button === toolRead && mode === "read");
    button.classList.toggle("active", active);
    button.setAttribute("aria-pressed", String(active));
  });

  paper.dataset.mode = mode;
  canvas.style.cursor =
    mode === "brush"
      ? "crosshair"
      : mode === "erase"
        ? "cell"
        : mode === "sample"
          ? "crosshair"
          : "";
  undoButton.disabled = history.length === 0;
  redoButton.disabled = undone.length === 0;
  clearButton.disabled = strokes.length === 0;
}

function setMode(next) {
  if (next === "sample") {
    statusText.textContent = "点击题纸任意处取底色";
  } else if (next === "brush" && mode === "sample") {
    statusText.textContent = `已取样 ${brushColor}`;
  } else if (next === "brush") {
    statusText.textContent = "按住左键涂抹遮罩 · 按 ESC 切回阅读模式";
  } else if (next === "erase") {
    statusText.textContent = "按住左键擦除 · 按 ESC 切回阅读模式";
  } else if (next === "read") {
    statusText.textContent = "阅读模式 · 可正常点击和滚动";
  }
  mode = next;
  syncToolbar();
}

function setBrushColor(color) {
  brushColor = color;
  swatch.style.background = color;
  swatch.style.borderColor = color === "#f7f7f8" ? "#101114" : color;
  statusText.textContent = `当前底色 ${color}`;
}

function resizeCanvas() {
  const width = Math.max(1, paper.clientWidth);
  const height = Math.max(1, paper.scrollHeight);
  canvas.width = Math.round(width * dpr);
  canvas.height = Math.round(height * dpr);
  canvas.style.width = `${width}px`;
  canvas.style.height = `${height}px`;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  redraw();
}

function pointFromEvent(event) {
  const rect = canvas.getBoundingClientRect();
  return {
    x: event.clientX - rect.left,
    y: event.clientY - rect.top,
  };
}

function drawAction(action) {
  ctx.globalCompositeOperation = action.erase
    ? "destination-out"
    : "source-over";
  ctx.strokeStyle = action.color;
  ctx.fillStyle = action.color;
  ctx.lineWidth = action.width;
  ctx.lineCap = "round";
  ctx.lineJoin = "round";

  const points = action.points;
  const single =
    points.length < 2 ||
    (points[0].x === points[points.length - 1].x &&
      points[0].y === points[points.length - 1].y);
  ctx.beginPath();
  if (single) {
    ctx.arc(points[0].x, points[0].y, action.width / 2, 0, Math.PI * 2);
    ctx.fill();
  } else {
    ctx.moveTo(points[0].x, points[0].y);
    for (let i = 1; i < points.length; i += 1) {
      ctx.lineTo(points[i].x, points[i].y);
    }
    ctx.stroke();
  }
  ctx.globalCompositeOperation = "source-over";
}

function redraw() {
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  strokes.forEach(drawAction);
}

function rebuild() {
  strokes = [];
  history.forEach((action) => {
    if (action.kind === "clear") {
      strokes.length = 0;
    } else {
      strokes.push(action);
    }
  });
  redraw();
  syncToolbar();
}

function commitStroke() {
  if (!liveStroke) return;
  history.push(liveStroke);
  strokes.push(liveStroke);
  undone.length = 0;
  liveStroke = null;
  syncToolbar();
}

function beginStroke(event) {
  const point = pointFromEvent(event);
  liveStroke = {
    erase: mode === "erase",
    color: mode === "erase" ? "#000000" : brushColor,
    width: brushWidth,
    points: [point],
  };
  canvas.setPointerCapture(event.pointerId);
  drawDot(point);
}

function extendStroke(point) {
  if (!liveStroke) return;
  const previous = liveStroke.points[liveStroke.points.length - 1];
  if (previous.x === point.x && previous.y === point.y) return;
  liveStroke.points.push(point);
  ctx.globalCompositeOperation = liveStroke.erase
    ? "destination-out"
    : "source-over";
  ctx.strokeStyle = liveStroke.color;
  ctx.lineWidth = liveStroke.width;
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
  ctx.beginPath();
  ctx.moveTo(previous.x, previous.y);
  ctx.lineTo(point.x, point.y);
  ctx.stroke();
  ctx.globalCompositeOperation = "source-over";
}

function drawDot(point) {
  if (!liveStroke) return;
  ctx.globalCompositeOperation = liveStroke.erase
    ? "destination-out"
    : "source-over";
  ctx.fillStyle = liveStroke.color;
  ctx.beginPath();
  ctx.arc(point.x, point.y, liveStroke.width / 2, 0, Math.PI * 2);
  ctx.fill();
  ctx.globalCompositeOperation = "source-over";
}

function parseRgbColor(value) {
  const match = value.match(/rgba?\(([^)]+)\)/i);
  if (!match) return null;
  const parts = match[1].split(",").map((part) => parseFloat(part.trim()));
  const alpha = parts.length === 4 ? parts[3] : 1;
  return {
    css: `rgb(${Math.round(parts[0])}, ${Math.round(parts[1])}, ${Math.round(parts[2])})`,
    alpha,
  };
}

function resolveBackgroundColor(element) {
  let node = element;
  while (node && node !== document.documentElement) {
    const color = parseRgbColor(getComputedStyle(node).backgroundColor);
    if (color && color.alpha === 1) return color.css;
    node = node.parentElement;
  }
  const bodyColor = parseRgbColor(getComputedStyle(document.body).backgroundColor);
  return bodyColor && bodyColor.alpha === 1 ? bodyColor.css : "#f7f7f8";
}

function sampleAt(event) {
  canvas.style.pointerEvents = "none";
  const target = document.elementFromPoint(event.clientX, event.clientY);
  canvas.style.pointerEvents = "auto";
  const color = target ? resolveBackgroundColor(target) : "#f7f7f8";
  setBrushColor(color);
  setMode("brush");
}

canvas.addEventListener("pointerdown", (event) => {
  if (mode === "read") return;
  if (mode === "sample") {
    sampleAt(event);
    return;
  }
  event.preventDefault();
  beginStroke(event);
});

canvas.addEventListener("pointermove", (event) => {
  if (!liveStroke || mode === "read" || mode === "sample") return;
  extendStroke(pointFromEvent(event));
});

canvas.addEventListener("pointerup", commitStroke);
canvas.addEventListener("pointercancel", commitStroke);

toolBrush.addEventListener("click", () => setMode("brush"));
toolErase.addEventListener("click", () => setMode("erase"));
toolSample.addEventListener("click", () => setMode("sample"));
toolRead.addEventListener("click", () => setMode("read"));

undoButton.addEventListener("click", () => {
  if (!history.length) return;
  undone.push(history.pop());
  rebuild();
});

redoButton.addEventListener("click", () => {
  if (!undone.length) return;
  history.push(undone.pop());
  rebuild();
});

clearButton.addEventListener("click", () => {
  if (!strokes.length) return;
  history.push({ kind: "clear" });
  undone.length = 0;
  rebuild();
});

widthInput.addEventListener("input", () => {
  brushWidth = Number(widthInput.value);
  widthValue.textContent = String(brushWidth);
});

let resizeTimer = 0;
window.addEventListener("resize", () => {
  window.clearTimeout(resizeTimer);
  resizeTimer = window.setTimeout(resizeCanvas, 80);
});

setBrushColor("#f7f7f8");
setMode("read");
resizeCanvas();
syncToolbar();

window.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    if (mode === "read") return;
    setMode("read");
  }
});
window.setTimeout(resizeCanvas, 250);

if (document.fonts && document.fonts.ready) {
  document.fonts.ready.then(resizeCanvas);
}
