const CACHE_VERSION = '__CACHE_VERSION__';
const STATIC_CACHE = `household-static-${CACHE_VERSION}`;

const CACHEABLE_DESTINATIONS = new Set(['style', 'script', 'image', 'font', 'manifest']);
// Only precache stable non-fingerprinted assets. Fingerprinted assets (bootstrap, site.css, site.js)
// use runtime caching via the fetch handler — their URLs change per build so precache would 404.
const PRECACHE_URLS = [
  '/manifest.json',
  '/favicon.ico',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/icons/icon-512-maskable.png',
];

self.addEventListener('install', (event) => {
  event.waitUntil((async () => {
    const cache = await caches.open(STATIC_CACHE);
    await cache.addAll(PRECACHE_URLS);
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(keys.map((key) => {
      if (key !== STATIC_CACHE) return caches.delete(key);
      return Promise.resolve(false);
    }));
    await clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  const request = event.request;

  if (request.method !== 'GET') return;
  // SSE streams are long-lived; the browser can terminate an idle service worker
  // mid-stream and break the connection. Let these bypass the worker entirely.
  if (request.headers.get('accept') === 'text/event-stream') return;

  const url = new URL(request.url);
  const isAssetRequest = CACHEABLE_DESTINATIONS.has(request.destination)
    && !url.pathname.endsWith('/sw.js');

  if (!isAssetRequest) {
    event.respondWith(fetch(request));
    return;
  }

  event.respondWith((async () => {
    const cache = await caches.open(STATIC_CACHE);
    const cached = await cache.match(request);
    if (cached) return cached;

    try {
      const response = await fetch(request);
      if (response && (response.ok || response.type === 'opaque')) {
        cache.put(request, response.clone());
      }
      return response;
    } catch (_error) {
      return cached ?? Response.error();
    }
  })());
});

self.addEventListener('push', (event) => {
  if (!event.data) return;
  const payload = event.data.json();
  event.waitUntil(self.registration.showNotification(payload.title, {
    body: payload.body,
    icon: '/icons/icon-192.png',
    badge: '/icons/icon-192.png',
    data: { url: payload.url },
  }));
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  // Client.url from the Clients API is always absolute — resolve the (relative)
  // notification url against our own origin so the equality check below can match.
  const targetUrl = new URL(event.notification.data?.url ?? '/', self.location.origin).href;
  event.waitUntil((async () => {
    const clientsList = await clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of clientsList) {
      if (client.url === targetUrl && 'focus' in client) return client.focus();
    }
    return clients.openWindow(targetUrl);
  })());
});
