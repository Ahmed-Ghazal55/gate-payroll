import './styles/app.css';
import { loadSession } from './state';
import { renderLogin } from './screens/login';
import { PortalController } from './screens/portal';
import { initPushNotifications, routeNotificationLink } from './push';

const root = document.getElementById('app') as HTMLElement;

async function startApp() {
  const session = await loadSession();
  const portal = new PortalController(root, (msg?: string) => {
    renderLogin(root, () => portal.render(), msg);
  });

  // Handle incoming push notification click
  window.addEventListener('gate-notification-click', (e: any) => {
    const link = e?.detail?.notification?.data?.link || '';
    const targetTab = routeNotificationLink(link);
    portal.setTab(targetTab);
  });

  // Handle 401 unauthorized
  window.addEventListener('gate-auth-unauthorized', (e: any) => {
    renderLogin(root, () => portal.render(), e?.detail?.message);
  });

  if (session?.accessToken && session?.iqama) {
    initPushNotifications().catch(() => {});
    portal.render();
  } else {
    renderLogin(root, () => portal.render());
  }
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    startApp();
  });
} else {
  startApp();
}
