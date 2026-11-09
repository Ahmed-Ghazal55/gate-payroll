'use client';

import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { api, ApiError } from './api';
import type { UserProfile } from './types';

interface AuthContextValue {
  user: UserProfile | null;
  loading: boolean;
  can: (permission: string) => boolean;
  isAdmin: boolean;
  canReview: boolean;
  canManageForms: boolean;
  refreshUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(null);
  const [loading, setLoading] = useState(true);

  const fetchMe = useCallback(async () => {
    if (typeof window !== 'undefined') {
      const p = window.location.pathname;
      if (p.includes('/login') || p.includes('/setup')) {
        setLoading(false);
        return;
      }
    }
    try {
      const u = await api.getMe();
      setUser(u);
      // The server opens nothing else until a temporary password is replaced.
      if (typeof window !== 'undefined' && (u as { mustChangePassword?: boolean }).mustChangePassword && !window.location.pathname.includes('/change-password')) {
        window.location.href = '/app/change-password/';
      }
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 401) {
        if (typeof window !== 'undefined') {
          const p = window.location.pathname;
          if (!p.startsWith('/app/login') && !p.startsWith('/app/setup')) {
            const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
            window.location.href = `/app/login/?returnUrl=${returnUrl}`;
          }
        }
      }
      setUser(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchMe();
  }, [fetchMe]);

  const isAdmin = user?.role === 'Admin' || user?.role === 'SuperAdmin';

  const can = useCallback(
    (permission: string): boolean => {
      if (!user) return false;
      if (isAdmin) return true;
      if (user.permissions && Array.isArray(user.permissions)) {
        if (user.permissions.includes('all')) return true;
        return user.permissions.includes(permission);
      }
      return false;
    },
    [user, isAdmin]
  );

  const canReview = isAdmin || can('forms.review') || can('forms.manage');
  const canManageForms = isAdmin || can('forms.manage');

  return (
    <AuthContext.Provider
      value={{
        user,
        loading,
        can,
        isAdmin,
        canReview,
        canManageForms,
        refreshUser: fetchMe,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    return {
      user: null,
      loading: false,
      can: () => true,
      isAdmin: true,
      canReview: true,
      canManageForms: true,
      refreshUser: async () => {},
    };
  }
  return context;
}
