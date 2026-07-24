import { useEffect, useState } from 'react';

// Full-screen "Thinking…" overlay shown while the CSP engine searches for a
// conflict-free timetable. The rotating lines mirror the engine's real phases so
// the wait reads as deliberate reasoning — like an AI working toward a suitable
// schedule — rather than a dead spinner. Rendered whenever a generation is in flight.
const THINKING_STEPS = [
    'Reading your faculty load allocation…',
    'Laying out lecture and lab meetings…',
    'Checking room capacities and lab requirements…',
    'Avoiding room and faculty double-booking…',
    'Keeping every student block clash-free…',
    'Honoring preferred teaching windows…',
    'Trimming idle gaps between classes…',
    'Weighing a fairer, more balanced layout…'
];

function ThinkingOverlay() {
    const [step, setStep] = useState(0);

    // Advance the reasoning line on a gentle cadence, and lock body scroll while
    // the overlay owns the screen (same pattern as the confirm dialog).
    useEffect(() => {
        const id = setInterval(
            () => setStep(n => (n + 1) % THINKING_STEPS.length),
            1500
        );
        document.body.style.overflow = 'hidden';
        return () => {
            clearInterval(id);
            document.body.style.overflow = '';
        };
    }, []);

    return (
        <div
            className="thinking-overlay"
            role="status"
            aria-live="polite"
            aria-label="Generating schedule"
        >
            <div className="thinking-card">
                <div className="thinking-grid" aria-hidden="true" />
                <div className="thinking-orb" aria-hidden="true">
                    <span />
                    <span />
                    <span />
                </div>
                <p className="thinking-title">
                    Thinking
                    <span className="thinking-dots" aria-hidden="true">
                        <i>.</i><i>.</i><i>.</i>
                    </span>
                </p>
                <p className="thinking-step" key={step}>{THINKING_STEPS[step]}</p>
                <p className="thinking-hint">
                    Working out a conflict-free arrangement — this can take a moment.
                </p>
            </div>
        </div>
    );
}

export default ThinkingOverlay;
