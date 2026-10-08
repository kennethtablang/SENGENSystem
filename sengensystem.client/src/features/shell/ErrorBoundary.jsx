import { Component } from 'react';
import './error-boundary.css';

/* The last line of defence for the SPA.

   React unmounts the whole tree when a render throws and nothing catches it, so before this existed
   a single bad property access in any one feature blanked the entire application — a white page,
   no message, nothing in the audit trail, and nothing the user could do but guess that a reload
   might help. For a system staff use to run enrollment, "the screen went white" is the worst
   possible failure report, because it says nothing about where to look.

   This turns that into a page that (a) says what happened, (b) keeps the error text on screen so it
   can be quoted in a report, and (c) offers the two recoveries that actually work — re-render the
   tree, or go back to the dashboard. Only render-time errors are caught: an error inside an event
   handler or a promise is not part of rendering and never reaches an error boundary, which is why
   the api modules still handle their own failures.

   Class component by necessity, not preference — componentDidCatch has no hooks equivalent. */

export default class ErrorBoundary extends Component {
    constructor(props) {
        super(props);
        this.state = { error: null, info: null };
    }

    static getDerivedStateFromError(error) {
        return { error };
    }

    componentDidCatch(error, info) {
        this.setState({ info });
        // Nothing else logs a render crash today, so at minimum put it where a developer with the
        // console open will find it. A real deployment would forward this to the server instead.
        console.error('[SEN-GEN] Unhandled render error', error, info?.componentStack);
    }

    /* Re-rendering the subtree is the cheap recovery and it genuinely works when the crash came
       from transient state (a half-loaded response, a stale id). When it doesn't, the boundary
       simply catches again and the user still has the reload and dashboard options. */
    reset = () => this.setState({ error: null, info: null });

    render() {
        const { error, info } = this.state;
        if (!error) return this.props.children;

        return (
            <div className="crash" role="alert">
                <div className="crash-card">
                    <h1>Something broke on this page</h1>
                    <p>
                        SEN-GEN hit an error while drawing this screen. Nothing you were viewing has been
                        saved or changed by this — but the page cannot finish rendering.
                    </p>

                    <div className="crash-actions">
                        <button type="button" className="btn btn-primary" onClick={this.reset}>
                            Try this page again
                        </button>
                        <button type="button" className="btn" onClick={() => window.location.reload()}>
                            Reload SEN-GEN
                        </button>
                        <a className="btn btn-ghost" href="/">Back to dashboard</a>
                    </div>

                    {/* Kept visible rather than hidden behind a toggle: the whole point is that the
                        person who hit it can copy this into a message to whoever maintains the
                        system. A collapsed panel is a panel nobody opens. */}
                    <details className="crash-detail" open>
                        <summary>Details to include in a report</summary>
                        <pre>{String(error?.stack || error?.message || error)}</pre>
                        {info?.componentStack && <pre>{info.componentStack}</pre>}
                    </details>
                </div>
            </div>
        );
    }
}
