import type { Metadata } from 'next';
import './globals.css';
import { AppShell } from '../components/layout/AppShell';

export const metadata: Metadata = {
  title: 'البوابة | مساحة إدارة الرواتب والحسابات 1.0',
  description: 'منظومة إدارة رواتب المناديب، الحسابات والعهد، ونماذج العمل والآيبان.',
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="ar" dir="rtl" data-theme="sand" suppressHydrationWarning>
      <head>
        <script
          dangerouslySetInnerHTML={{
            __html: `
              try {
                const t = localStorage.getItem('gate_theme') || 'sand';
                document.documentElement.setAttribute('data-theme', t);
              } catch (_) {}
            `,
          }}
        />
      </head>
      <body className="bg-[var(--bg)] text-[var(--ink)] min-h-screen">
        <AppShell>{children}</AppShell>
      </body>
    </html>
  );
}
