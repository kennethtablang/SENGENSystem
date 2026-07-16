import { useEffect, useState } from 'react';
import { clearToken, fetchCurrentUser, setToken } from './api';
import { AuthContext } from './auth-context';

export function AuthProvider({ children }) {
    const [user, setUser] = useState(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        fetchCurrentUser()
            .then(current => {
                if (current) setUser(current);
                else clearToken();
            })
            .finally(() => setLoading(false));
    }, []);

    const login = (token, loggedInUser) => {
        setToken(token);
        setUser(loggedInUser);
    };

    const logout = () => {
        clearToken();
        setUser(null);
    };

    // Refresh the in-memory user after profile edits.
    const updateUser = (updated) => setUser(updated);

    return (
        <AuthContext.Provider value={{ user, loading, login, logout, updateUser }}>
            {children}
        </AuthContext.Provider>
    );
}
