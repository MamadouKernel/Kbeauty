const CACHE_VERSION = 'keke-beauty-v16';
const STATIC_CACHE = `${CACHE_VERSION}-static`;
const PAGE_CACHE = `${CACHE_VERSION}-pages`;
const OFFLINE_URL = '/offline.html';
const STATIC_ASSETS = [
  '/', OFFLINE_URL, '/app.css?v=16', '/css/tailwind.css?v=16', '/KekeBeauty.Web.styles.css?v=16', '/manifest.webmanifest', '/img/logo.png',
  '/icons/icon-192.png', '/icons/icon-512.png', '/icons/icon-512-maskable.png',
  '/icons/apple-touch-icon.png', '/fonts/material-symbols-outlined.ttf',
  '/js/keke-pwa.js', '/js/keke-push.js', '/js/keke-google.js?v=15'
];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(STATIC_CACHE).then(cache => cache.addAll(STATIC_ASSETS)));
});

self.addEventListener('activate', event => {
  event.waitUntil(Promise.all([
    caches.keys().then(keys => Promise.all(keys.filter(key => !key.startsWith(CACHE_VERSION)).map(key => caches.delete(key)))),
    self.clients.claim()
  ]));
});

self.addEventListener('message', event => {
  if (event.data?.type === 'SKIP_WAITING') self.skipWaiting();
});

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin || url.pathname.startsWith('/_blazor')) return;

  if (request.mode === 'navigate') {
    event.respondWith(fetch(request).then(response => {
      if (response.ok) caches.open(PAGE_CACHE).then(cache => cache.put(request, response.clone()));
      return response;
    }).catch(async () => {
      const cachedPage = await caches.match(request);
      if (cachedPage) return cachedPage;
      // Ne présenter le mode hors connexion que si le navigateur confirme réellement
      // l'absence de réseau. Une coupure serveur momentanée ne doit pas être confondue avec cela.
      if (!self.navigator.onLine) return caches.match(OFFLINE_URL);
      return new Response('Keke Beauty est momentanément indisponible. Réessayez dans quelques instants.', {
        status: 503,
        headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Retry-After': '5' }
      });
    }));
    return;
  }

  event.respondWith(caches.match(request).then(cached => {
    const network = fetch(request).then(response => {
      if (response.ok) caches.open(STATIC_CACHE).then(cache => cache.put(request, response.clone()));
      return response;
    }).catch(() => cached);
    return cached || network;
  }));
});

self.addEventListener('push', event => {
  let payload = {};
  try { payload = event.data ? event.data.json() : {}; } catch { payload = { message: event.data?.text() || '' }; }
  event.waitUntil(self.registration.showNotification(payload.titre || 'Keke Beauty', {
    body: payload.message || '', icon: '/icons/icon-192.png', badge: '/icons/icon-192.png',
    tag: payload.tag || payload.url || 'keke-beauty', renotify: Boolean(payload.renotify),
    data: { url: payload.url || '/' }, actions: payload.actions || []
  }));
});

self.addEventListener('notificationclick', event => {
  event.notification.close();
  const target = new URL(event.notification.data?.url || '/', self.location.origin).href;
  event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(list => {
    const existing = list.find(client => client.url === target);
    return existing ? existing.focus() : self.clients.openWindow(target);
  }));
});
