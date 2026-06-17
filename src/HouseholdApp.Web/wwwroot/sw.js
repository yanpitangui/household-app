const CACHE_VERSION = 'v3';
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

// Stub: wire up push notifications here when ready
self.addEventListener('push', (_event) => {});

// Stub: handle notification clicks here when ready
self.addEventListener('notificationclick', (_event) => {});
