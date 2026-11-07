import { Capacitor } from '@capacitor/core';
import { PushNotifications } from '@capacitor/push-notifications';
import { api } from './api';
import { getActiveFcmToken, setActiveFcmToken, getSession } from './state';

export async function initPushNotifications(): Promise<string> {
  if (!Capacitor.isNativePlatform()) {
    return getActiveFcmToken();
  }

  try {
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
        setActiveFcmToken(fcmToken);
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
