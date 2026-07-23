import { Fragment } from 'react';
import Tip, { TipBody } from '../shell/Tooltip';

/* Dependency-free SVG charts for the dashboard. All colors come from CSS classes
   (see dashboard.css) so every chart follows the design tokens and the dark theme.
   Every data mark carries a hover/focus tooltip via <Tip>, so the charts read at a
   glance and disclose their exact figures on demand. */

const pct = (part, whole) => (whole === 0 ? '0%' : `${Math.round((100 * part) / whole)}%`);

/* Ring chart with a center figure and a legend. `segments`: [{ label, value, tone }]
   where tone is one of the .tone-* classes. Zero-value segments stay in the legend
   but draw nothing. */
export function Donut({ segments, centerValue, centerLabel, tipNote }) {
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
                        <Tip
                            as="g"
                            key={s.label}
                            content={<TipBody
                                title={s.label}
                                rows={[[centerLabel, s.value], ['Share', pct(s.value, total)], ['Total', total]]}
                                note={tipNote}
                            />}
                        >
                            <circle
                                className={`donut-seg ${s.tone}`}
                                cx="50" cy="50" r={r}
                                strokeDasharray={`${frac * c} ${c}`}
                                strokeDashoffset={-progress * c}
                                transform="rotate(-90 50 50)"
                            />
                        </Tip>
                    );
                    progress += frac;
                    return seg;
                })}
                <text className="donut-center" x="50" y="49" textAnchor="middle">{centerValue}</text>
                <text className="donut-sub" x="50" y="62" textAnchor="middle">{centerLabel}</text>
            </svg>
            <ul className="chart-legend">
                {segments.map(s => (
                    <Tip
                        as="li"
                        key={s.label}
                        content={<TipBody title={s.label} rows={[[centerLabel, s.value], ['Share', pct(s.value, total)]]} />}
                    >
                        <span className={`legend-dot ${s.tone}`} aria-hidden="true" />
                        <span className="legend-label">{s.label}</span>
                        <strong>{s.value}</strong>
                    </Tip>
                ))}
            </ul>
        </div>
    );
}

/* Cumulative-intake area with daily-count columns underneath. An invisible full-height
   band over each day carries the tooltip, so hovering anywhere in a day's column works.
   `points`: [{ date: 'yyyy-mm-dd', count, cumulative }] in chronological order. */
export function TrendChart({ points }) {
    // Drawn at 2× the old geometry. The wide cards stretch these charts to ~1100px, so a
    // 560-unit viewBox doubled every label; at 1120 units the scale is ~1:1 and the tick
    // text renders at its true CSS size.
    const W = 1120, H = 300;
    const pad = { top: 24, right: 24, bottom: 44, left: 68 };
    const innerW = W - pad.left - pad.right;
    const innerH = H - pad.top - pad.bottom;

    const maxCum = Math.max(1, ...points.map(p => p.cumulative));
    const maxDay = Math.max(1, ...points.map(p => p.count));
    const x = i => pad.left + (points.length === 1 ? innerW / 2 : (i / (points.length - 1)) * innerW);
    const yCum = v => pad.top + innerH - (v / maxCum) * innerH;

    const line = points.map((p, i) => `${x(i).toFixed(1)},${yCum(p.cumulative).toFixed(1)}`).join(' ');
    const baseline = pad.top + innerH;
    const barW = Math.max(4, (innerW / points.length) * 0.5);
    const bandW = innerW / points.length;
    const monthDay = iso => iso.slice(5).replace('-', '/');
    const longDate = iso => new Date(`${iso}T00:00:00`).toLocaleDateString(undefined,
        { weekday: 'short', month: 'short', day: 'numeric' });

    return (
        <svg viewBox={`0 0 ${W} ${H}`} className="chart-svg" role="img"
            aria-label={`Cumulative registrations, now ${points[points.length - 1].cumulative}`}>
            {[0.5, 1].map(f => (
                <line key={f} className="chart-grid"
                    x1={pad.left} x2={pad.left + innerW}
                    y1={yCum(maxCum * f)} y2={yCum(maxCum * f)} />
            ))}
            <text className="chart-tick" x={pad.left - 12} y={yCum(maxCum) + 6} textAnchor="end">{maxCum}</text>
            <text className="chart-tick" x={pad.left - 12} y={baseline + 6} textAnchor="end">0</text>

            {/* daily new registrations, scaled to the lower half so they never crowd the area */}
            {points.map((p, i) => p.count > 0 && (
                <rect key={p.date} className="trend-bar"
                    x={x(i) - barW / 2}
                    y={baseline - (p.count / maxDay) * innerH * 0.45}
                    width={barW}
                    height={(p.count / maxDay) * innerH * 0.45}
                    rx="2"
                />
            ))}

            <polygon className="trend-area" points={`${pad.left},${baseline} ${line} ${pad.left + innerW},${baseline}`} />
            <polyline className="trend-line" points={line} />
            <circle className="trend-dot" cx={x(points.length - 1)} cy={yCum(points[points.length - 1].cumulative)} r="7" />

            {/* hover bands: one per day, drawn last so they sit above the marks */}
            {points.map((p, i) => (
                <Tip
                    as="g"
                    key={`band-${p.date}`}
                    content={<TipBody
                        title={longDate(p.date)}
                        rows={[
                            ['New registrations', p.count],
                            ['Running total', p.cumulative],
                            ['Share of total', pct(p.count, points[points.length - 1].cumulative)]
                        ]}
                    />}
                >
                    <rect className="chart-band"
                        x={x(i) - bandW / 2} y={pad.top}
                        width={bandW} height={innerH} />
                    <line className="chart-band-rule" x1={x(i)} x2={x(i)} y1={pad.top} y2={baseline} />
                </Tip>
            ))}

            <text className="chart-tick" x={pad.left} y={H - 12} textAnchor="start">{monthDay(points[0].date)}</text>
            <text className="chart-tick" x={pad.left + innerW} y={H - 12} textAnchor="end">{monthDay(points[points.length - 1].date)}</text>
        </svg>
    );
}

/* Assigned-units columns per faculty member against a shared scale, each with a
   tick at that member's own load ceiling and a dashed line at the mean.
   `members`: [{ name, assignedUnits, maxLoadUnits, flag, programCode, scheduledHours }]. */
export function LoadColumns({ members, mean }) {
    const shown = [...members]
        .sort((a, b) => b.assignedUnits - a.assignedUnits)
        .slice(0, 14);
    const W = 1120, H = 340;
    const pad = { top: 24, right: 24, bottom: 60, left: 68 };
    const innerW = W - pad.left - pad.right;
    const innerH = H - pad.top - pad.bottom;

    const maxY = Math.max(1, ...shown.map(m => Math.max(m.assignedUnits, m.maxLoadUnits)));
    const y = v => pad.top + innerH - (v / maxY) * innerH;
    const slot = innerW / Math.max(1, shown.length);
    const colW = Math.min(68, slot * 0.55);
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
            <text className="chart-tick" x={pad.left - 12} y={y(maxY) + 6} textAnchor="end">{maxY}u</text>
            <text className="chart-tick" x={pad.left - 6} y={baseline + 3} textAnchor="end">0</text>

            {shown.map((m, i) => {
                const cx = pad.left + slot * i + slot / 2;
                const headroom = m.maxLoadUnits - m.assignedUnits;
                return (
                    <Tip
                        as="g"
                        key={m.name}
                        content={<TipBody
                            title={m.name}
                            rows={[
                                ['Assigned', `${m.assignedUnits} u`],
                                ['Ceiling', `${m.maxLoadUnits} u`],
                                [headroom < 0 ? 'Over by' : 'Headroom', `${Math.abs(headroom)} u`],
                                ['Utilization', pct(m.assignedUnits, m.maxLoadUnits || 1)],
                                ...(m.scheduledHours != null ? [['Scheduled', `${m.scheduledHours} h/wk`]] : []),
                                ...(m.programCode ? [['Program', m.programCode]] : [])
                            ]}
                            note={`${m.flag} · department mean ${mean} u`}
                        />}
                    >
                        {/* hit area, so thin or empty columns are still hoverable */}
                        <rect className="chart-band" x={cx - slot / 2} y={pad.top} width={slot} height={innerH} />
                        {m.assignedUnits > 0 && (
                            <rect className={`col-fill ${toneFor(m.flag)}`}
                                x={cx - colW / 2} y={y(m.assignedUnits)}
                                width={colW} height={baseline - y(m.assignedUnits)} rx="4" />
                        )}
                        {/* the member's own ceiling */}
                        <line className="col-cap"
                            x1={cx - colW / 2 - 6} x2={cx + colW / 2 + 6}
                            y1={y(m.maxLoadUnits)} y2={y(m.maxLoadUnits)} />
                        <text className="chart-tick" x={cx} y={H - 12} textAnchor="middle">{initials(m.name)}</text>
                    </Tip>
                );
            })}

            {mean > 0 && (
                <>
                    <line className="mean-line" x1={pad.left} x2={pad.left + innerW} y1={y(mean)} y2={y(mean)} />
                    <text className="chart-tick mean-label" x={pad.left + innerW} y={y(mean) - 8} textAnchor="end">
                        mean {mean}u
                    </text>
                </>
            )}
        </svg>
    );
}

/* A single-line sparkline for KPI tiles — shape only, no axes. */
export function Sparkline({ values, tone = 'spark-blue' }) {
    if (!values?.length) return null;
    const W = 120, H = 28, pad = 2;
    const max = Math.max(1, ...values);
    const min = Math.min(...values);
    const span = Math.max(1, max - min);
    const x = i => pad + (values.length === 1 ? (W - pad * 2) / 2 : (i / (values.length - 1)) * (W - pad * 2));
    const y = v => pad + (H - pad * 2) - ((v - min) / span) * (H - pad * 2);
    const line = values.map((v, i) => `${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ');

    return (
        <svg viewBox={`0 0 ${W} ${H}`} className={`spark ${tone}`} aria-hidden="true" preserveAspectRatio="none">
            <polygon className="spark-area" points={`${x(0)},${H} ${line} ${x(values.length - 1)},${H}`} />
            <polyline className="spark-line" points={line} />
        </svg>
    );
}

/* Enrollment funnel: each stage as a centred band, narrowing with its own count, and
   the stage-to-stage conversion between them. `stages`: [{ label, value, note }]. */
export function Funnel({ stages }) {
    const top = Math.max(1, stages[0]?.value ?? 0);
    return (
        <ol className="funnel">
            {stages.map((s, i) => {
                const prev = i === 0 ? null : stages[i - 1];
                const width = Math.max(6, (100 * s.value) / top);
                const conv = prev ? pct(s.value, prev.value) : '100%';
                return (
                    <li key={s.label}>
                        {prev && (
                            <span className="funnel-drop">
                                ↓ {conv} continue · {Math.max(0, prev.value - s.value)} drop off
                            </span>
                        )}
                        <Tip
                            as="div"
                            className="funnel-stage"
                            content={<TipBody
                                title={s.label}
                                rows={[
                                    ['At this stage', s.value],
                                    ['Of all intake', pct(s.value, top)],
                                    ...(prev ? [['From previous', conv]] : [])
                                ]}
                                note={s.note}
                            />}
                        >
                            <span className={`funnel-bar funnel-step-${i}`} style={{ width: `${width}%` }} />
                            <span className="funnel-meta">
                                <span className="funnel-label">{s.label}</span>
                                <span className="funnel-value">{s.value}</span>
                            </span>
                        </Tip>
                    </li>
                );
            })}
        </ol>
    );
}

/* Weekly density heatmap: weekday columns × hour rows, shaded by class count.
   `cells`: [{ day: 1-6, hour: 7-17, classes }]. */
export function Heatmap({ cells }) {
    const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
    const hours = [...new Set(cells.map(c => c.hour))].sort((a, b) => a - b);
    const max = Math.max(1, ...cells.map(c => c.classes));
    const at = (day, hour) => cells.find(c => c.day === day && c.hour === hour)?.classes ?? 0;
    const label = h => `${String(h).padStart(2, '0')}:00`;

    return (
        <div className="heat">
            <div className="heat-corner" />
            {DAYS.map(d => <div key={d} className="heat-head">{d}</div>)}
            {hours.map(h => (
                <Fragment key={h}>
                    <div className="heat-hour">{label(h)}</div>
                    {DAYS.map((d, di) => {
                        const n = at(di + 1, h);
                        return (
                            <Tip
                                as="div"
                                key={`${d}-${h}`}
                                className="heat-cell"
                                style={{ '--heat': max === 0 ? 0 : n / max }}
                                content={<TipBody
                                    title={`${d} ${label(h)}–${label(h + 1)}`}
                                    rows={[
                                        ['Classes running', n],
                                        ['Busiest hour', max],
                                        ['Relative load', pct(n, max)]
                                    ]}
                                    note={n === 0 ? 'Nothing scheduled — free capacity in this hour.' : undefined}
                                />}
                            >
                                {n > 0 && <span className="heat-num">{n}</span>}
                            </Tip>
                        );
                    })}
                </Fragment>
            ))}
        </div>
    );
}

/* Ranked horizontal bars. HTML rather than SVG, so labels stay at the page's own
   type size instead of scaling with the card. `items`: [{ label, value, sub, tip }]. */
export function RankedBars({ items, unit = '' }) {
    const max = Math.max(1, ...items.map(i => i.value));
    return (
        <ul className="ranked">
            {items.map(i => (
                <Tip as="li" key={i.label} content={i.tip}>
                    <span className="ranked-label">{i.label}</span>
                    <span className="ranked-track">
                        <span className="ranked-fill" style={{ width: `${(100 * i.value) / max}%` }} />
                    </span>
                    <span className="ranked-value">{i.value}{unit}</span>
                </Tip>
            ))}
        </ul>
    );
}

/* Stacked composition bars — one row per category, segments summing to 100%.
   `rows`: [{ label, segments: [{ label, value, tone }], tip }]. */
export function StackedBars({ rows }) {
    return (
        <ul className="stacked">
            {rows.map(r => {
                const total = r.segments.reduce((sum, s) => sum + s.value, 0);
                return (
                    <Tip as="li" key={r.label} content={r.tip}>
                        <span className="stacked-label">{r.label}</span>
                        <span className="stacked-track">
                            {r.segments.filter(s => s.value > 0).map(s => (
                                <span
                                    key={s.label}
                                    className={`stacked-seg ${s.tone}`}
                                    style={{ width: `${(100 * s.value) / Math.max(1, total)}%` }}
                                />
                            ))}
                        </span>
                        <span className="stacked-value">{pct(r.segments[0].value, total)}</span>
                    </Tip>
                );
            })}
        </ul>
    );
}

/* Horizontal meter with a labelled fill — used for the operational health panel. */
export function Meter({ pct: value, tone = '' }) {
    return (
        <span className="meter">
            <span className={`meter-fill ${tone}`} style={{ width: `${Math.min(100, Math.max(0, value))}%` }} />
        </span>
    );
}
