// Lets CashFlowChart.razor track the mouse over the chart's SVG for the hover tooltip. Needs JS
// because converting a raw mouse position into the SVG's own viewBox coordinate space (which is
// what all the chart's point geometry is expressed in) requires getScreenCTM() - Blazor's
// MouseEventArgs only gives element-relative pixels, which don't account for the responsive
// width:100% scaling between the SVG's on-screen size and its viewBox units. Throttled before
// calling into .NET since mousemove fires far faster than the hover lookup needs to run, and
// each call is a real network round trip over the Blazor Server SignalR connection.
const THROTTLE_MS = 40;

let currentHandlers = null;

export function attach(svgId, dotNetRef) {
    detach();

    const svg = document.getElementById(svgId);
    if (!svg) return;

    let lastCallAt = 0;

    const onMouseMove = (event) => {
        const now = performance.now();
        if (now - lastCallAt < THROTTLE_MS) return;
        lastCallAt = now;

        const point = toSvgPoint(svg, event.clientX, event.clientY);
        if (point) {
            dotNetRef.invokeMethodAsync('OnChartHover', point.x, point.y);
        }
    };

    const onMouseLeave = () => {
        dotNetRef.invokeMethodAsync('OnChartHoverEnd');
    };

    svg.addEventListener('mousemove', onMouseMove);
    svg.addEventListener('mouseleave', onMouseLeave);

    currentHandlers = { svg, onMouseMove, onMouseLeave };
}

export function detach() {
    if (!currentHandlers) return;

    currentHandlers.svg.removeEventListener('mousemove', currentHandlers.onMouseMove);
    currentHandlers.svg.removeEventListener('mouseleave', currentHandlers.onMouseLeave);
    currentHandlers = null;
}

function toSvgPoint(svg, clientX, clientY) {
    const ctm = svg.getScreenCTM();
    if (!ctm) return null;

    const point = svg.createSVGPoint();
    point.x = clientX;
    point.y = clientY;
    const transformed = point.matrixTransform(ctm.inverse());
    return { x: transformed.x, y: transformed.y };
}
