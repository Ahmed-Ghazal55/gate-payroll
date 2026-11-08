import { Capacitor } from '@capacitor/core';
import { PushNotifications } from '@capacitor/push-notifications';
import { api } from './api';
import { getActiveFcmToken, setActiveFcmToken, getSession } from './state';

export function routeNotificationLink(link?: string): string {
  const l = (link || '').toLowerCase();
  if (l.includes('request') || l.includes('ask')) return 'requests';
  if (l.includes('statement') || l.includes('payslip')) return 'statement';
  if (l.includes('debt')) return 'debts';
  if (l.includes('device') || l.includes('session')) return 'devices';
  return 'notifications';
}

export async function initPushNotifications(): Promise<string> {
  if (!Capacitor.isNativePlatform()) {
    return getActiveFcmToken();
  }

  try {
    // Create Android notification channel with Arabic name
    if (Capacitor.getPlatform() === 'android') {
      try {
        await PushNotifications.createChannel({
          id: 'default',
          name: 'إشعارات البوابة',
          description: 'إشعارات التعميمات وطلبات السلف والبيانات المالية',
          importance: 5,
          visibility: 1,
          sound: 'default',
          vibration: true
        });
      } catch (chErr) {
        console.warn('Could not create notification channel:', chErr);
      }
    }

    let permStatus = await PushNotifications.checkPermissions();
    if (permStatus.receive === 'prompt') {
      permStatus = await PushNotifications.requestPermissions();
    }
    if (permStatus.receive !== 'granted') {
      console.warn('Push notification permission not granted:', permStatus.receive);
      return '';
    }

    await PushNotifications.register();

    return new Promise((resolve) => {
      PushNotifications.addListener('registration', async (token) => {
        const fcmToken = token.value;
        await setActiveFcmToken(fcmToken);
        const session = getSession();
        if (session?.accessToken) {
          await updateDeviceTokenOnServer(fcmToken);
        }
        resolve(fcmToken);
      });

      PushNotifications.addListener('registrationError', (error) => {
        console.error('Error on push registration:', error);
        resolve(getActiveFcmToken());
      });

      PushNotifications.addListener('pushNotificationReceived', (notification) => {
        console.log('Push notification received:', notification);
      });

      PushNotifications.addListener('pushNotificationActionPerformed', (action) => {
        console.log('Push notification action performed:', action);
        window.dispatchEvent(new CustomEvent('gate-notification-click', { detail: action }));
      });
    });
  } catch (err) {
    console.error('Failed to initialize push notifications:', err);
    return getActiveFcmToken();
  }
}

export async function updateDeviceTokenOnServer(fcmDeviceToken: string): Promise<boolean> {
  if (!fcmDeviceToken) return false;
  const session = getSession();
  if (!session?.accessToken) return false;

  try {
    await api('/api/rider-mobile/device-token', { fcmDeviceToken });
    return true;
  } catch (err) {
    console.warn('Failed to update device token on server:', err);
    return false;
  }
}
