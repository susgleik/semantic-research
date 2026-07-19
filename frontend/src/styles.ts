import type { CSSProperties } from 'react';

export const cardStyle: CSSProperties = {
  padding: 24,
  borderRadius: 12,
  border: '1px solid var(--border)',
  background: 'var(--bg-subtle)',
};

export const fieldStyle: CSSProperties = {
  display: 'block',
  marginTop: 6,
  padding: '9px 12px',
  width: '100%',
  borderRadius: 8,
  border: '1px solid var(--border)',
  background: 'var(--bg)',
  color: 'var(--text)',
};

export const fieldLabelStyle: CSSProperties = {
  fontSize: 14,
  fontWeight: 600,
};

export const primaryButtonStyle: CSSProperties = {
  padding: '11px 16px',
  borderRadius: 10,
  border: 'none',
  background: 'var(--accent)',
  color: 'var(--accent-contrast)',
  fontWeight: 600,
  fontSize: 15,
};

export const secondaryButtonStyle: CSSProperties = {
  padding: '8px 14px',
  borderRadius: 8,
  border: '1px solid var(--border)',
  background: 'transparent',
  color: 'var(--text)',
  fontSize: 14,
};

export const dangerButtonStyle: CSSProperties = {
  ...secondaryButtonStyle,
  color: 'var(--danger)',
  borderColor: 'color-mix(in srgb, var(--danger) 40%, var(--border))',
};

export function badgeStyle(color: string): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 6,
    padding: '3px 10px',
    borderRadius: 999,
    fontSize: 12,
    fontWeight: 600,
    color,
    background: `color-mix(in srgb, ${color} 15%, transparent)`,
  };
}

export function bannerStyle(color: string): CSSProperties {
  return {
    padding: '12px 16px',
    borderRadius: 10,
    border: `1px solid ${color}`,
    background: `color-mix(in srgb, ${color} 10%, transparent)`,
    color,
    fontSize: 14,
  };
}
