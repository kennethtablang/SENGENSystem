import { useState } from 'react';

function EyeIcon({ off }) {
    return (
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
            strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M2 12s3.5-6.5 10-6.5S22 12 22 12s-3.5 6.5-10 6.5S2 12 2 12Z" />
            <circle cx="12" cy="12" r="2.6" />
            {off && <line x1="4" y1="20" x2="20" y2="4" />}
        </svg>
    );
}

function PasswordInput({ id, value, onChange, autoComplete }) {
    const [visible, setVisible] = useState(false);

    return (
        <div className="field-input">
            <input
                id={id}
                type={visible ? 'text' : 'password'}
                autoComplete={autoComplete}
                value={value}
                onChange={onChange}
                required
            />
            <button
                type="button"
                className="pw-toggle"
                onClick={() => setVisible(v => !v)}
                aria-label={visible ? 'Hide password' : 'Show password'}
                title={visible ? 'Hide password' : 'Show password'}
            >
                <EyeIcon off={visible} />
            </button>
        </div>
    );
}

export default PasswordInput;
