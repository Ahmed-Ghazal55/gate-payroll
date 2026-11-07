import './styles/app.css';
import { loadSession } from './state';
import { renderLogin } from './screens/login';
import { PortalController } from './screens/portal';
import { initPushNotifications, routeNotificationLink } from './push';

const root = document.getElementById('app') as HTMLElement;

function initOfflineBanner(onRetry: () => void) {
  let banner = document.getElementById('mob-offline-banner');
  if (!banner) {
    banner = document.createElement('div');
    banner.id = 'mob-offline-banner';
    banner.className = 'mob-offline-bar hidden';
    banner.innerHTML = `
      <span>⚠️ لا يوجد اتصال بالإنترنت</span>
      <button class="secondary small" id="mob-offline-retry" style="margin:0; padding:4px 10px; font-size:12px;">إعادة المحاولة ↻</button>
    `;
    root.parentElement?.insertBefore(banner, root);
    document.getElementById('mob-offline-retry')?.addEventListener('click', () => {
      if (navigator.onLine) {
        banner?.classList.add('hidden');
        onRetry();
      }
    });
  }

  const update = () => {
    if (!navigator.onLine) {
      banner?.classList.remove('hidden');
    } else {
      banner?.classList.add('hidden');
    }
  };

  window.addEventListener('online', () => {
    update();
    onRetry();
  });
  window.addEventListener('offline', update);
  update();
}

async function startApp() {
  const session = await loadSession();
  const portal = new PortalController(root, (msg?: string) => {
    renderLogin(root, () => portal.render(), msg);
  });

  initOfflineBanner(() => {
    if (session?.accessToken && session?.iqama) {
      portal.render();
    }
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
