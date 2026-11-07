import './styles/app.css';
import { getSession } from './state';
import { renderLogin } from './screens/login';
import { PortalController } from './screens/portal';
import { initPushNotifications } from './push';

const root = document.getElementById('app') as HTMLElement;

function startApp() {
  const session = getSession();
  const portal = new PortalController(root, (msg?: string) => {
    renderLogin(root, () => portal.render(), msg);
  });

  // Handle incoming push notification click
  window.addEventListener('gate-notification-click', () => {
    portal.setTab('notifications');
  });

  if (session?.accessToken && session?.iqama) {
    // If logged in, initialize push notifications in the background
    initPushNotifications().catch(() => {});
    portal.render();
  } else {
    renderLogin(root, () => portal.render());
  }
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startApp);
} else {
  startApp();
}
