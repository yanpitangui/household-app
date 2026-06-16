const VERSION = 'v1';

self.addEventListener('install', () => {
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(clients.claim());
});

self.addEventListener('fetch', (event) => {
  event.respondWith(fetch(event.request));
});

// Stub: wire up push notifications here when ready
self.addEventListener('push', (_event) => {});

// Stub: handle notification clicks here when ready
self.addEventListener('notificationclick', (_event) => {});
