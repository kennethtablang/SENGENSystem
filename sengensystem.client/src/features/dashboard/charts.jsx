/* Dependency-free SVG charts for the dashboard. All colors come from CSS classes
   (see dashboard.css) so every chart follows the design tokens and the dark theme. */

/* Ring chart with a center figure and a legend. `segments`: [{ label, value, tone }]
   where tone is one of the .tone-* classes. Zero-value segments stay in the legend
   but draw nothing. */
export function Donut({ segments, centerValue, centerLabel }) {
    const total = segments.reduce((sum, s) => sum + s.value, 0);
    const r = 40;
    const c = 2 * Math.PI * r;
    let progress = 0;

    return (
        <div className="chart-donut">
            <svg viewBox="0 0 100 100" className="donut-svg" role="img"
                aria-label={`${centerLabel}: ${centerValue}`}>
                <circle className="donut-track" cx="50" cy="50" r={r} />
                {total > 0 && segments.filter(s => s.value > 0).map(s => {
                    const frac = s.value / total;
                    const seg = (
                        <circle
                            key={s.label}
                            className={`donut-seg ${s.tone}`}
                            cx="50" cy="50" r={r}
                            strokeDasharray={`${frac * c} ${c}`}
                            strokeDashoffset={-progress * c}
                            transform="rotate(-90 50 50)"
                        >
                            <title>{`${s.label}: ${s.value}`}</title>
                        </circle>
                    );
                    progress += frac;
                    return seg;
                })}
                <text className="donut-center" x="50" y="49" textAnchor="middle">{centerValue}</text>
                <text className="donut-sub" x="50" y="62" textAnchor="middle">{centerLabel}</text>
            </svg>
            <ul className="chart-legend">
                {segments.map(s => (
                    <li key={s.label}>
                        <span className={`legend-dot ${s.tone}`} aria-hidden="true" />
                        <span className="legend-label">{s.label}</span>
                        <strong>{s.value}</strong>
                    </li>
                ))}
            </ul>
        </div>
    );
}

/* Cumulative-intake area with daily-count columns underneath.
   `points`: [{ date: 'yyyy-mm-dd', count, cumulative }] in chronological order. */
export function TrendChart({ points }) {
    const W = 560, H = 150;
    const pad = { top: 12, right: 12, bottom: 22, left: 34 };
    const innerW = W - pad.left - pad.right;
    const innerH = H - pad.top - pad.bottom;

    const maxCum = Math.max(1, ...points.map(p => p.cumulative));
    const maxDay = Math.max(1, ...points.map(p => p.count));
    const x = i => pad.left + (points.length === 1 ? innerW / 2 : (i / (points.length - 1)) * innerW);
    const yCum = v => pad.top + innerH - (v / maxCum) * innerH;

    const line = points.map((p, i) => `${x(i).toFixed(1)},${yCum(p.cumulative).toFixed(1)}`).join(' ');
    const baseline = pad.top + innerH;
    const barW = Math.max(2, (innerW / points.length) * 0.5);
    const monthDay = iso => iso.slice(5).replace('-', '/');

    return (
        <svg viewBox={`0 0 ${W} ${H}`} className="chart-svg" role="img"
            aria-label={`Cumulative registrations, now ${points[points.length - 1].cumulative}`}>
            {[0.5, 1].map(f => (
                <line key={f} className="chart-grid"
                    x1={pad.left} x2={pad.left + innerW}
                    y1={yCum(maxCum * f)} y2={yCum(maxCum * f)} />
            ))}
            <text className="chart-tick" x={pad.left - 6} y={yCum(maxCum) + 3} textAnchor="end">{maxCum}</text>
            <text className="chart-tick" x={pad.left - 6} y={baseline + 3} textAnchor="end">0</text>

            {/* daily new registrations, scaled to the lower half so they never crowd the area */}
            {points.map((p, i) => p.count > 0 && (
                <rect key={p.date} className="trend-bar"
                    x={x(i) - barW / 2}
                    y={baseline - (p.count / maxDay) * innerH * 0.45}
                    width={barW}
                    height={(p.count / maxDay) * innerH * 0.45}
                    rx="1"
                >
                    <title>{`${p.date}: +${p.count} registration(s)`}</title>
                </rect>
            ))}

            <polygon className="trend-area" points={`${pad.left},${baseline} ${line} ${pad.left + innerW},${baseline}`} />
            <polyline className="trend-line" points={line} />
            <circle className="trend-dot" cx={x(points.length - 1)} cy={yCum(points[points.length - 1].cumulative)} r="3.5" />

            <text className="chart-tick" x={pad.left} y={H - 6} textAnchor="start">{monthDay(points[0].date)}</text>
            <text className="chart-tick" x={pad.left + innerW} y={H - 6} textAnchor="end">{monthDay(points[points.length - 1].date)}</text>
        </svg>
    );
}

/* Assigned-units columns per faculty member against a shared scale, each with a
   tick at that member's own load ceiling and a dashed line at the mean.
   `members`: [{ name, assignedUnits, maxLoadUnits, flag }]. */
export function LoadColumns({ members, mean }) {
    const shown = [...members]
        .sort((a, b) => b.assignedUnits - a.assignedUnits)
        .slice(0, 14);
    const W = 560, H = 170;
    const pad = { top: 12, right: 12, bottom: 30, left: 34 };
    const innerW = W - pad.left - pad.right;
    const innerH = H - pad.top - pad.bottom;

    const maxY = Math.max(1, ...shown.map(m => Math.max(m.assignedUnits, m.maxLoadUnits)));
    const y = v => pad.top + innerH - (v / maxY) * innerH;
    const slot = innerW / Math.max(1, shown.length);
    const colW = Math.min(34, slot * 0.55);
    const baseline = pad.top + innerH;

    const initials = name => name.split(/\s+/).filter(Boolean).map(w => w[0]).join('').slice(0, 3).toUpperCase();
    const toneFor = flag =>
        flag === 'Overloaded' ? 'col-over'
            : flag === 'AboveAverage' ? 'col-warn'
                : flag === 'Unassigned' ? 'col-idle'
                    : 'col-ok';

    return (
        <svg viewBox={`0 0 ${W} ${H}`} className="chart-svg" role="img" aria-label="Faculty load distribution">
            <line className="chart-grid" x1={pad.left} x2={pad.left + innerW} y1={y(maxY)} y2={y(maxY)} />
            <text className="chart-tick" x={pad.left - 6} y={y(maxY) + 3} textAnchor="end">{maxY}u</text>
            <text className="chart-tick" x={pad.left - 6} y={baseline + 3} textAnchor="end">0</text>

            {shown.map((m, i) => {
                const cx = pad.left + slot * i + slot / 2;
                return (
                    <g key={m.name}>
                        <title>{`${m.name}: ${m.assignedUnits}/${m.maxLoadUnits} units · ${m.flag}`}</title>
                        {m.assignedUnits > 0 && (
                            <rect className={`col-fill ${toneFor(m.flag)}`}
                                x={cx - colW / 2} y={y(m.assignedUnits)}
                                width={colW} height={baseline - y(m.assignedUnits)} rx="2" />
                        )}
                        {/* the member's own ceiling */}
                        <line className="col-cap"
                            x1={cx - colW / 2 - 3} x2={cx + colW / 2 + 3}
                            y1={y(m.maxLoadUnits)} y2={y(m.maxLoadUnits)} />
                        <text className="chart-tick" x={cx} y={H - 6} textAnchor="middle">{initials(m.name)}</text>
                    </g>
                );
            })}

            {mean > 0 && (
                <>
                    <line className="mean-line" x1={pad.left} x2={pad.left + innerW} y1={y(mean)} y2={y(mean)} />
                    <text className="chart-tick mean-label" x={pad.left + innerW} y={y(mean) - 4} textAnchor="end">
                        mean {mean}u
                    </text>
                </>
            )}
        </svg>
    );
}
